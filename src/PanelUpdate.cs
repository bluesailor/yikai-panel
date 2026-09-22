using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace YikaiLocal;

public sealed record PanelRelease(string Version,string DownloadUrl,string Sha256,string Notes);

public static class PanelUpdate
{
    public const string Website="https://panel.yikai.cn";
    public const string ManifestUrl=Website+"/update/latest.json";
    public static string CurrentVersion=>typeof(PanelUpdate).Assembly.GetName().Version!.ToString(3);
    public static Version ParseVersion(string value)
    {
        if(!Version.TryParse(value,out var v)||v.Major<0||v.Minor<0||v.Build<0)throw new InvalidDataException("Invalid update version.");
        return new Version(v.Major,v.Minor,v.Build,Math.Max(0,v.Revision));
    }
    public static async Task<PanelRelease?> CheckAsync(HttpClient client,CancellationToken token)
    {
        using var response=await client.GetAsync(ManifestUrl,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
        using var stream=await response.Content.ReadAsStreamAsync(token);using var data=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=await stream.ReadAsync(buffer,token))>0){if(data.Length+read>65536)throw new InvalidDataException("Update metadata is too large.");data.Write(buffer,0,read);}
        var release=JsonSerializer.Deserialize<PanelRelease>(data.ToArray(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Missing update metadata.");
        if(ParseVersion(release.Version)<=ParseVersion(CurrentVersion))return null;
        if(!Uri.TryCreate(release.DownloadUrl,UriKind.Absolute,out var url)||url.Scheme!="https"||!string.IsNullOrEmpty(url.UserInfo)||release.Sha256==null||release.Sha256.Length!=64||!release.Sha256.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid update metadata.");
        return release;
    }
    public static async Task<string> DownloadAsync(Settings settings,PanelRelease release,HttpClient client,IProgress<int>? progress,CancellationToken token)
    {
        ParseVersion(release.Version);var directory=Path.Combine(settings.Root,"temp","updates");Directory.CreateDirectory(directory);var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".exe");var partial=path+".part";
        try{
            using var response=await client.GetAsync(release.DownloadUrl,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            const long limit=256L*1024*1024;var length=response.Content.Headers.ContentLength;if(length>limit)throw new InvalidDataException("Update is too large.");
            await using(var input=await response.Content.ReadAsStreamAsync(token))await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
                var buffer=new byte[65536];long total=0;int read;while((read=await input.ReadAsync(buffer,token))>0){total+=read;if(total>limit)throw new InvalidDataException("Update is too large.");await output.WriteAsync(buffer.AsMemory(0,read),token);if(length>0)progress?.Report((int)(total*100/length.Value));}
                if(length.HasValue&&total!=length.Value)throw new InvalidDataException("Incomplete update download.");
            }
            await using(var input=File.OpenRead(partial)){var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,token));if(!hash.Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update checksum mismatch.");}
            var version=FileVersionInfo.GetVersionInfo(partial).FileVersion;if(version==null||ParseVersion(version)!=ParseVersion(release.Version))throw new InvalidDataException("Update executable version mismatch.");
            token.ThrowIfCancellationRequested();File.Move(partial,path);return path;
        }finally{if(File.Exists(partial))File.Delete(partial);}
    }
    public static ProcessStartInfo PrepareApply(Settings settings,string source,string expectedHash,int parentId,long parentStarted,string errorMessage,bool showErrors=true)
    {
        var root=Path.GetFullPath(settings.Root);var target=Path.Combine(root,"soft","panel","YikaiLocal.exe");
        var temp=Path.Combine(root,"temp","updates");Directory.CreateDirectory(temp);var id=Guid.NewGuid().ToString("N");var script=Path.Combine(temp,"apply-"+id+".ps1");var planFile=Path.Combine(temp,"apply-"+id+".json");
        var previousHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
        File.WriteAllText(planFile,JsonSerializer.Serialize(new{Root=root,Target=target,Source=Path.GetFullPath(source),ExpectedHash=expectedHash,PreviousHash=previousHash,ParentId=parentId,ParentStarted=parentStarted,Backup=Path.Combine(root,"backups","panel-update-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+id[..6]),ErrorMessage=errorMessage,ShowErrors=showErrors}));
        File.WriteAllText(script,ApplyScript);
        var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-WindowStyle","Hidden","-File",script,"-PlanFile",planFile})start.ArgumentList.Add(arg);return start;
    }
    // The helper waits for this panel only. PHP/MySQL/Nginx remain owned by the runtime ledger.
    const string ApplyScript="""
param([string]$PlanFile)
$ErrorActionPreference='Stop'
$plan=Get-Content -LiteralPath $PlanFile -Raw | ConvertFrom-Json
function Get-UpdateHash([string]$Path) {
    $inputFile=[System.IO.File]::OpenRead($Path)
    $algorithm=[System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($inputFile)).Replace('-','') }
    finally { $algorithm.Dispose();$inputFile.Dispose() }
}
$backedUp=$false
$replaced=$false
try {
    $parent=Get-Process -Id $plan.ParentId -ErrorAction SilentlyContinue
    if($parent -and $parent.StartTime.ToUniversalTime().Ticks -eq $plan.ParentStarted) {
        if(-not $parent.WaitForExit(30000)){throw 'Panel did not exit in time.'}
    }
    if((Get-UpdateHash $plan.Source) -ne $plan.ExpectedHash){throw 'Update checksum changed.'}
    if((Get-UpdateHash $plan.Target) -ne $plan.PreviousHash){throw 'Installed panel changed.'}
    New-Item -ItemType Directory -Path $plan.Backup -Force | Out-Null
    Copy-Item -LiteralPath $plan.Target -Destination (Join-Path $plan.Backup 'YikaiLocal.exe')
    $backedUp=$true
    $replaced=$true
    Copy-Item -LiteralPath $plan.Source -Destination $plan.Target -Force
    if((Get-UpdateHash $plan.Target) -ne $plan.ExpectedHash){throw 'Installed checksum mismatch.'}
    $started=Start-Process -FilePath $plan.Target -ArgumentList @('--root',('"'+$plan.Root+'"')) -WindowStyle Hidden -PassThru
    if($started.WaitForExit(2500) -and $started.ExitCode -ne 0){throw 'New panel could not start.'}
    @{success=$true;backup=$plan.Backup} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $plan.Root 'temp\updates\result.json') -Encoding UTF8
} catch {
    $failure=$_.Exception.Message
    if($backedUp -and $replaced){Copy-Item -LiteralPath (Join-Path $plan.Backup 'YikaiLocal.exe') -Destination $plan.Target -Force}
    @{success=$false;error=$failure;backup=$plan.Backup} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $plan.Root 'temp\updates\result.json') -Encoding UTF8
    $existing=Get-Process -Id $plan.ParentId -ErrorAction SilentlyContinue
    if(-not $existing -or $existing.StartTime.ToUniversalTime().Ticks -ne $plan.ParentStarted){Start-Process -FilePath $plan.Target -ArgumentList @('--root',('"'+$plan.Root+'"')) -WindowStyle Hidden}
    if($plan.ShowErrors){Add-Type -AssemblyName System.Windows.Forms;[System.Windows.Forms.MessageBox]::Show($plan.ErrorMessage,'Yikai Panel') | Out-Null}
    exit 1
}
""";
}
