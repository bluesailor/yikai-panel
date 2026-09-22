using System.Text.RegularExpressions;

namespace YikaiLocal;

public sealed record AdminEntry(string Path,string Source,int Confidence);
public sealed record AdminScanResult(IReadOnlyList<AdminEntry> Entries,bool Limited,int Skipped);

public static class AdminEntryDetector
{
    public static bool TryNormalize(string? value,out string path)
    {
        path="";var text=value?.Trim()??"";if(text.Length==0)return true;
        if(text.Length>2048||text.Any(char.IsControl)||text.Contains('\\')||text.StartsWith("//")||Regex.IsMatch(text,@"^[a-z][a-z0-9+.-]*:",RegexOptions.IgnoreCase))return false;
        try{
            var decoded=Uri.UnescapeDataString(text.Split('?', '#')[0]);
            if(decoded.Contains('\\')||decoded.StartsWith("//")||decoded.Split('/').Any(p=>p is "." or ".."))return false;
            if(!Uri.TryCreate(new Uri("http://project.invalid/"),text.TrimStart('/'),out var uri)||uri.Host!="project.invalid"||uri.Scheme!="http")return false;
            path=uri.PathAndQuery+uri.Fragment;return true;
        }catch(UriFormatException){return false;}
    }
    public static AdminScanResult Scan(string root,CancellationToken cancellation=default)
    {
        if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
        var found=new Dictionary<string,AdminEntry>(StringComparer.OrdinalIgnoreCase);bool limited=false;int skipped=0;
        void Add(string path,string source,int confidence){if(TryNormalize(path,out var normalized)&&normalized!=""&&(!found.TryGetValue(normalized,out var old)||old.Confidence<confidence))found[normalized]=new(normalized,source,confidence);}
        bool FileAt(string name)=>File.Exists(System.IO.Path.Combine(root,name));
        if(FileAt("wp-admin/index.php")&&FileAt("wp-login.php"))Add("/wp-admin/","WordPress",100);
        var known=new HashSet<string>(["admin","administrator","manage","manager","backend","console","dede"],StringComparer.OrdinalIgnoreCase);
        foreach(var file in new[]{"admin.php","backend.php","manage.php"})if(FileAt(file))Add("/"+file,file,80);
        try{
            int count=0;
            foreach(var directory in new DirectoryInfo(root).EnumerateDirectories()){
                cancellation.ThrowIfCancellationRequested();if(++count>160){limited=true;break;}
                if((directory.Attributes&FileAttributes.ReparsePoint)!=0){skipped++;continue;}
                var name=directory.Name;if(name.StartsWith('.')||new[]{"vendor","node_modules","storage","cache","uploads","assets","themes","plugins","install","member"}.Contains(name,StringComparer.OrdinalIgnoreCase))continue;
                bool index=File.Exists(System.IO.Path.Combine(directory.FullName,"index.php")),login=File.Exists(System.IO.Path.Combine(directory.FullName,"login.php"));
                if(index&&login&&FileAt("config/version.php")&&File.Exists(System.IO.Path.Combine(directory.FullName,"includes/header.php")))Add("/"+Uri.EscapeDataString(name)+"/","YikaiCMS · "+name,100);
                else if(known.Contains(name)&&(index||login))Add("/"+Uri.EscapeDataString(name)+(index?"/":"/login.php"),name+"/"+(index?"index.php":"login.php"),80);
                else if(index&&login)Add("/"+Uri.EscapeDataString(name)+"/",name+"/login.php",50);
            }
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException){skipped++;}
        var env=System.IO.Path.Combine(root,".env");
        try{
            if(File.Exists(env)){
                if(new FileInfo(env).Length>65536)limited=true;
                else foreach(var line in File.ReadLines(env)){
                    cancellation.ThrowIfCancellationRequested();var match=Regex.Match(line,@"^\s*(?:ADMIN_PATH|ADMIN_URL|BACKEND_PATH|BACKEND_URL)\s*=\s*(.+?)\s*$",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(50));
                    if(!match.Success)continue;var value=match.Groups[1].Value.Split(" #",2)[0].Trim().Trim('\'', '"');
                    if(!value.Contains('$'))Add(value,".env",90);
                }
            }
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or RegexMatchTimeoutException){skipped++;}
        return new(found.Values.OrderByDescending(x=>x.Confidence).ThenBy(x=>x.Path).ToArray(),limited,skipped);
    }
    public static string Url(string siteUrl,string path)
    {
        if(!TryNormalize(path,out var normalized)||normalized=="")throw new ArgumentException("Invalid local admin path.");
        return new Uri(new Uri(siteUrl),normalized).AbsoluteUri;
    }
    public static bool Exists(string root,string path)
    {
        if(!TryNormalize(path,out var normalized)||normalized=="")return false;
        var physical=System.IO.Path.Combine(root,Uri.UnescapeDataString(normalized.Split('?', '#')[0]).TrimStart('/').Replace('/',System.IO.Path.DirectorySeparatorChar));
        return File.Exists(physical)||Directory.Exists(physical)&&(File.Exists(System.IO.Path.Combine(physical,"index.php"))||File.Exists(System.IO.Path.Combine(physical,"login.php")));
    }
}
