using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YikaiLocal;

public sealed class ImportDatabase
{
    public string Kind {get;set;}="generic";
    public string ConfigFile {get;set;}="";
    public string Driver {get;set;}="unknown";
    public string Host {get;set;}="127.0.0.1";
    public int Port {get;set;}=3306;
    public string Name {get;set;}="";
    public string User {get;set;}="root";
    public string Password {get;set;}="";
    public string SqlitePath {get;set;}="";
}
public sealed class ImportCandidate
{
    public bool Selected {get;set;}
    public string Domain {get;set;}="";
    public string TargetDomain {get;set;}="";
    public string Folder {get;set;}="";
    public string Php {get;set;}="8.2";
    public string Engine {get;set;}="mysql80";
    public string Status {get;set;}="";
    public ImportDatabase Database {get;set;}=new();
    public string DatabaseSummary=>Database.Driver=="mysql"?Database.Name:Database.Driver;
}
public sealed class PhpStudyImport(Settings settings,Runtime runtime)
{
    public async Task<List<ImportCandidate>> ScanAsync(string root)
    {
        var extensions=Path.Combine(Path.GetFullPath(root),"Extensions");if(!Directory.Exists(extensions))throw new IOException("Choose the PHPStudy folder containing Extensions.");
        var sites=new List<ImportCandidate>();
        foreach(var server in Directory.GetDirectories(extensions).Where(p=>Path.GetFileName(p).StartsWith("Apache",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(p).StartsWith("Nginx",StringComparison.OrdinalIgnoreCase)))
        {
            var vhosts=Path.Combine(server,"conf","vhosts");if(!Directory.Exists(vhosts))continue;
            foreach(var file in Directory.GetFiles(vhosts,"*.conf"))
            {
                var text=Regex.Replace(File.ReadAllText(file),@"(?m)^\s*#.*$","");
                var host=Regex.Match(text,@"(?im)^\s*(?:ServerName|server_name)\s+([^\s;]+)").Groups[1].Value;
                var path=Regex.Match(text,"(?im)^\\s*(?:DocumentRoot|root)\\s+(?:\"([^\"]+)\"|([^;\\r\\n]+))");
                var dir=path.Groups[1].Success?path.Groups[1].Value:path.Groups[2].Value.Trim();
                if(!Settings.ValidDomain(host)||!Path.IsPathFullyQualified(dir)||!Directory.Exists(dir))continue;
                dir=Path.GetFullPath(dir);if(sites.Any(s=>s.Domain==host&&s.Folder.Equals(dir,StringComparison.OrdinalIgnoreCase)))continue;
                var php=Regex.Match(text,@"(?i)php(\d+\.\d+)").Groups[1].Value;if(!new[]{"8.0","8.2","8.5"}.Contains(php))php="8.2";
                var target=host;var n=1;while(settings.Sites.Any(s=>s.Domain==target)||Directory.Exists(Path.Combine(settings.Root,"wwwroot",target))||sites.Any(s=>s.TargetDomain==target)){target=host+"-import"+(n++==1?"":n.ToString())+".yikai";}
                sites.Add(new(){Domain=host,TargetDomain=target,Folder=dir,Php=php});
            }
        }
        sites=sites.OrderBy(s=>s.Domain).ToList();if(sites.Count==0)return sites;
        var db=await CallAsync<List<ImportDatabase>>(new{action="inspect",folders=sites.Select(s=>s.Folder).ToArray()});
        for(var i=0;i<sites.Count;i++){sites[i].Database=db[i];if(db[i].Kind=="yikaicms"&&sites[i].Php=="8.0")sites[i].Php="8.2";}
        return sites;
    }
    public Task<DatabaseScanResult> ScanSourceAsync(string folder)=>CallAsync<DatabaseScanResult>(new{action="scan-source",folder});
    async Task<T> CallAsync<T>(object request,IProgress<ImportProgress>? progress=null)
    {
        BundledDatabaseTools.EnsureImportTools(settings.Root);
        Directory.CreateDirectory(Path.Combine(settings.Root,"temp"));var file=Path.Combine(settings.Root,"temp","transfer-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(file,JsonSerializer.Serialize(request,Settings.Json));
        try
        {
            var info=new ProcessStartInfo(Path.Combine(settings.Root,"soft","php",Runtime.InternalPhpVersionFor(settings),"php.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-c",Path.Combine(settings.Root,"soft","php",Runtime.InternalPhpVersionFor(settings),"php.ini"),Path.Combine(settings.Root,"soft","db-manager","phpstudy-transfer.php"),file})info.ArgumentList.Add(arg);
            using var p=Process.Start(info)??throw new IOException("Cannot start transfer helper.");var output=p.StandardOutput.ReadToEndAsync();var errors=new System.Text.StringBuilder();
            while(await p.StandardError.ReadLineAsync() is {} line){if(line.StartsWith("YIKAI_PROGRESS ",StringComparison.Ordinal)){progress?.Report(JsonSerializer.Deserialize<ImportProgress>(line[15..],Settings.Json)!);}else errors.AppendLine(line);}
            await p.WaitForExitAsync();if(p.ExitCode!=0)throw new IOException(errors.ToString().Trim());return JsonSerializer.Deserialize<T>(await output,Settings.Json)??throw new IOException("Empty transfer result.");
        }
        finally{File.Delete(file);}
    }
    public async Task<ImportResult> ImportAsync(ImportCandidate candidate,IProgress<ImportProgress>? progress=null)
    {
        var domain=candidate.TargetDomain.Trim().ToLowerInvariant();
        if(!Settings.ValidDomain(domain)||settings.Sites.Any(s=>s.Domain==domain))throw new IOException("Invalid or duplicate target domain.");
        if(!new[]{"mysql","sqlite","none"}.Contains(candidate.Database.Driver))throw new IOException("Choose database settings before importing.");
        if(!new[]{"8.0","8.2","8.5"}.Contains(candidate.Php))throw new IOException("Choose an installed PHP version.");
        if(candidate.Database.Driver=="mysql"&&(candidate.Database.Port<1||candidate.Database.Port>65535||string.IsNullOrWhiteSpace(candidate.Database.Name)))throw new IOException("Enter a source database and port.");
        if(candidate.Database.Driver=="sqlite"&&!File.Exists(candidate.Database.SqlitePath))throw new IOException("Choose the SQLite database file.");
        if(candidate.Database.Driver=="mysql"&&!new[]{"localhost","127.0.0.1","::1"}.Contains(candidate.Database.Host))throw new IOException("Only local PHPStudy databases are supported.");
        if(candidate.Database.Driver=="mysql"&&!new[]{"mysql80","mysql57"}.Contains(candidate.Engine))throw new IOException("Invalid target engine.");
        if(Path.GetFullPath(settings.Root).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(candidate.Folder).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||Path.GetFullPath(settings.Root).StartsWith(Path.GetFullPath(candidate.Folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("The source folder contains the destination environment.");
        if(!File.Exists(Path.Combine(settings.Root,"soft","php",candidate.Php,"php-cgi.exe")))throw new IOException("The project's PHP version is not installed: "+candidate.Php);
        var destination=Path.Combine(settings.Root,"wwwroot",domain);if(Directory.Exists(destination))throw new IOException("Target folder already exists.");
        var token=Guid.NewGuid().ToString("N")[..16];var stage=Path.Combine(settings.Root,"temp","import-"+token);
        if(Path.GetFullPath(stage).StartsWith(Path.GetFullPath(candidate.Folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("The source folder contains the migration staging directory.");
        Directory.CreateDirectory(stage);
        progress?.Report(new(){Phase="files"});
        var files=await Task.Run(()=>CopyTree(candidate.Folder,stage));
        var dbName="yk_import_"+token;var engine=candidate.Database.Driver=="mysql"?candidate.Engine:candidate.Database.Driver;
        if(engine is "mysql80" or "mysql57")await runtime.EnsureDatabaseAsync(engine);
        var target=new{engine,name=dbName,host="127.0.0.1",port=settings.DatabasePort(engine),user=settings.MysqlUser,password=settings.DatabasePassword(engine)};
        TransferResponse? transferred=null;Site? site=null;bool moved=false;
        try{
        transferred=await CallAsync<TransferResponse>(new{action="transfer",root=settings.Root,folder=stage,finalDirectory=destination,source=candidate.Database,target},progress);
        if(!transferred.Ok||candidate.Database.Driver!="none"&&!transferred.Database.Verified)throw new IOException("Database migration was not verified.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);Directory.Move(stage,destination);moved=true;
        site=new Site{Id="import_"+token,Title=candidate.Domain,Domain=domain,Directory=destination,Php=candidate.Php,Database=engine,DatabaseName=dbName,Template=candidate.Database.Kind=="yikaicms"?"yikaicms":"import",Enabled=false,HttpPort=Math.Max(8080,settings.Sites.Select(s=>s.HttpPort).DefaultIfEmpty(8080).Max())+1,FastCgiPort=Math.Max(settings.DbFastCgiPort,settings.Sites.Select(s=>s.FastCgiPort).DefaultIfEmpty(9082).Max())+1};
        var result=new ImportResult(site,files,transferred.Database,transferred.ManualConfig);
        settings.Sites.Add(site);settings.Save();
        try{Directory.CreateDirectory(Path.Combine(settings.Root,"logs"));File.WriteAllText(Path.Combine(settings.Root,"logs","import-"+token+".json"),JsonSerializer.Serialize(result,Settings.Json));}catch(Exception reportError) when(reportError is IOException or UnauthorizedAccessException){/* A report failure does not invalidate the registered, verified copy. */}
        return result;
        }catch(Exception error){
            if(site!=null)settings.Sites.Remove(site);
            var cleanup=new List<string>();
            if(transferred?.Ok==true&&candidate.Database.Driver=="mysql"){try{await CallAsync<JsonElement>(new{action="rollback",root=settings.Root,folder=stage,source=candidate.Database,target});}catch(Exception failure){cleanup.Add("Database cleanup: "+failure.Message);}}
            if(moved){try{Directory.Move(destination,stage);}catch(Exception failure){cleanup.Add("Copied folder: "+failure.Message);}}
            if(cleanup.Count>0)throw new IOException(error.Message+"\n"+string.Join("\n",cleanup),error);
            throw;
        }
    }
    static CopiedFiles CopyTree(string source,string target)
    {
        long count=0,bytes=0;
        var root=new DirectoryInfo(source);if((root.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked source folders need to be resolved before import.");
        foreach(var file in root.GetFiles())
        {
            if(file.Name==".git")continue;if((file.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked file: "+file.FullName);
            var destination=Path.Combine(target,file.Name);file.CopyTo(destination,false);File.SetAttributes(destination,FileAttributes.Normal);count++;bytes+=file.Length;
        }
        foreach(var dir in root.GetDirectories())
        {
            if(dir.Name is ".git" or ".svn")continue;if((dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked folder: "+dir.FullName);
            var next=Path.Combine(target,dir.Name);Directory.CreateDirectory(next);var copied=CopyTree(dir.FullName,next);count+=copied.Count;bytes+=copied.Bytes;
        }
        return new(count,bytes);
    }
}

public sealed class DatabaseScanResult { public List<ImportDatabase> Matches {get;set;}=[]; public int Files {get;set;} public int Skipped {get;set;} public bool Limited {get;set;} }
public sealed class ImportProgress { public string Phase {get;set;}=""; }
public sealed record CopiedFiles(long Count,long Bytes);
public sealed class TransferSummary {public int Tables {get;set;} public long Rows {get;set;} public int Views {get;set;} public int Triggers {get;set;} public int Routines {get;set;} public int Events {get;set;} public bool Verified {get;set;}}
public sealed class TransferResponse {public bool Ok {get;set;} public bool ManualConfig {get;set;} public TransferSummary Database {get;set;}=new();}
public sealed record ImportResult(Site Site,CopiedFiles Files,TransferSummary Database,bool ManualConfig);
