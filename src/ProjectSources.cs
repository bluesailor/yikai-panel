using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YikaiLocal;

// 新建项目的程序来源：YikaiCMS 依次尝试——本机还没有模板时先取面板配置的下载地址（默认 down.yikai.cn）；
// 否则先问 YikaiCMS 官方更新服务器（国内，给出版本、完整包地址与 SHA-256），再退到 GitHub Release（SHA-256），
// 再退到 down.yikai.cn，最后用本机已有版本；
// WordPress 取 wordpress.org 官方最新版（按官方 .sha1 校验）。解压结果缓存在 soft\cache，同版本不重复下载。
public static class ProjectSources
{
    const string CmsReleaseApi="https://api.github.com/repos/bluesailor/yikaicms/releases/latest";
    const long Limit=200L*1024*1024;

    public static string BundledCms(Settings settings)=>Path.Combine(settings.Root,"soft","packages","yikaicms");

    // 本机可用的最新一份：随包模板与下载缓存里版本号更高的那个（没有缓存时就是随包模板）
    static string NewestLocal(Settings settings)
    {
        var bundled=BundledCms(settings);
        if(CmsVersion.Detect(bundled) is not {} bundledVersion)return bundled;
        if(!Version.TryParse(bundledVersion,out var best))return bundled;
        var result=bundled;
        try
        {
            var cache=Path.Combine(settings.Root,"soft","cache");
            if(!Directory.Exists(cache))return result;
            foreach(var directory in Directory.EnumerateDirectories(cache,"yikaicms-*"))
                if(CmsVersion.Detect(directory) is {} version&&Version.TryParse(version,out var parsed)&&parsed>best){best=parsed;result=directory;}
        }
        catch(IOException){/* 缓存目录读不到就用随包模板 */}
        return result;
    }

    // “更新 CMS 模板”：强制从官网下载一份最新版并缓存，返回缓存目录（失败时抛出原因）
    public static async Task<string> RefreshCmsAsync(Settings settings,IProgress<string> status,CancellationToken token)
    {
        var directory=await MirrorCmsAsync(settings,status,token,null);
        if(directory==null)throw new IOException("CMS package could not be downloaded from "+settings.CmsPackageUrl);
        return directory;
    }

    public static async Task<string> YikaiCmsAsync(Settings settings,IProgress<string> status,CancellationToken token)
    {
        var local=NewestLocal(settings);
        // 本机没有可用模板时（最简包），各条路都没拿到就退到 down.yikai.cn 镜像，而不是返回一个不存在的目录
        async Task<string> LocalOrMirror()=>File.Exists(Path.Combine(local,"config","version.php"))?local:await MirrorCmsAsync(settings,status,token,(string?)null)??local;
        // 先走 YikaiCMS 官方更新服务器（国内机房）：它给出最新版本、国内下载地址和 SHA-256。
        // 本机没有模板（最简包）时也先问它：down.yikai.cn 的 yikaicms-latest.zip 不一定及时更新（曾停在 2.0.0），只作兜底。
        // GitHub 在中国大陆经常很慢，只在更新服务器不可用时才用。
        if(await UpdateServerCmsAsync(settings,local,status,token) is {} fromUpdateServer)return fromUpdateServer;
        try
        {
            using var client=Client();
            status.Report("YikaiCMS · "+Tr(settings,"检查最新版本…","Checking for the latest version…","最新版を確認中…"));
            using var doc=JsonDocument.Parse(await client.GetStringAsync(CmsReleaseApi,token));
            var tag=doc.RootElement.GetProperty("tag_name").GetString()??"";var version=tag.TrimStart('v');
            if(!Version.TryParse(version,out var latest))return await LocalOrMirror();
            if(CmsVersion.Detect(local) is {} have&&Version.TryParse(have,out var haveVersion)&&haveVersion>=latest)return await LocalOrMirror();
            var target=Path.Combine(settings.Root,"soft","cache","yikaicms-"+latest);
            if(File.Exists(Path.Combine(target,"config","version.php")))return target;
            var asset=doc.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(a=>a.GetProperty("name").GetString()==$"yikaicms-v{latest}.zip");
            if(asset.ValueKind!=JsonValueKind.Object)return await LocalOrMirror();
            var url=asset.GetProperty("browser_download_url").GetString()!;
            var digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;
            if(digest==null||!digest.StartsWith("sha256:"))return await MirrorCmsAsync(settings,status,token,version)??await LocalOrMirror();
            await DownloadAndExtract(client,url,digest[7..],SHA256.Create(),target,"YikaiCMS "+latest,settings,status,token);
            return target;
        }
        catch(Exception e) when(e is HttpRequestException or IOException or InvalidDataException or JsonException or KeyNotFoundException or TaskCanceledException){
            var mirrored=await MirrorCmsAsync(settings,status,token,(string?)null);
            if(mirrored!=null)return mirrored;
            status.Report("YikaiCMS · "+Tr(settings,"无法获取最新版，使用本机已有版本","Latest version unavailable; using the local copy","最新版を取得できないためローカル版を使用"));return local;
        }
    }

    const string CmsUpdateCheck="https://update.yikaicms.com/api/update/check.php";

    // 从 YikaiCMS 更新服务器取最新完整包：check.php 返回 latest_version、download_url（完整包）和 hash（sha256:...）。
    // 本机已有同版本或更新的模板直接用本机的；服务器不可用、不给完整包地址或校验值时返回 null，交给 GitHub 兜底。
    static async Task<string?> UpdateServerCmsAsync(Settings settings,string local,IProgress<string> status,CancellationToken token)
    {
        try
        {
            using var client=Client();
            status.Report("YikaiCMS · "+Tr(settings,"检查最新版本…","Checking for the latest version…","最新版を確認中…"));
            var have=CmsVersion.Detect(local);
            var query=$"{CmsUpdateCheck}?version={Uri.EscapeDataString(have??"1.0.0")}&channel=stable&domain=localhost&site_name=&php=8.2&source=yikai-panel";
            using var doc=JsonDocument.Parse(await client.GetStringAsync(query,token));
            if(!doc.RootElement.TryGetProperty("code",out var code)||code.ValueKind!=JsonValueKind.Number||code.GetInt32()!=0)return null;
            if(!doc.RootElement.TryGetProperty("data",out var data)||data.ValueKind!=JsonValueKind.Object)return null;
            var version=data.TryGetProperty("latest_version",out var v)?v.GetString():null;
            if(version==null||!Version.TryParse(version,out var latest))return null;
            if(have!=null&&Version.TryParse(have,out var haveVersion)&&haveVersion>=latest)return local;
            var target=Path.Combine(settings.Root,"soft","cache","yikaicms-"+latest);
            if(File.Exists(Path.Combine(target,"config","version.php")))return target;
            var url=data.TryGetProperty("download_url",out var u)?u.GetString():null;
            var hash=data.TryGetProperty("hash",out var h)?h.GetString():null;
            if(url==null||hash==null||!hash.StartsWith("sha256:")||hash.Length!=71)return null;
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.Host.Equals("update.yikaicms.com",StringComparison.OrdinalIgnoreCase))return null;
            await DownloadAndExtract(client,url,hash[7..],SHA256.Create(),target,"YikaiCMS "+latest,settings,status,token);
            if(!File.Exists(Path.Combine(target,"config","version.php")))return null;
            return target;
        }
        catch(Exception e) when(e is HttpRequestException or IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    // 从面板配置的地址下载 CMS（默认 https://down.yikai.cn/soft/yikaicms/yikaicms-latest.zip）。
    // 该地址没有官方校验值，因此改校验“结构”（必须有 config/version.php）并按包内版本号缓存，
    // 同版本以后直接复用缓存；下载失败返回 null，由调用方继续尝试其它来源。
    static async Task<string?> MirrorCmsAsync(Settings settings,IProgress<string> status,CancellationToken token,string? expectedVersion)
    {
        var url=settings.CmsPackageUrl;
        if(string.IsNullOrWhiteSpace(url))return null;
        try
        {
            if(expectedVersion!=null)
            {
                var cached=Path.Combine(settings.Root,"soft","cache","yikaicms-"+expectedVersion);
                if(File.Exists(Path.Combine(cached,"config","version.php")))return cached;
            }
            using var client=Client();
            status.Report("YikaiCMS · "+Tr(settings,"从官网下载…","Downloading from the vendor site…","ベンダーサイトからダウンロード中…"));
            var cache=Path.Combine(settings.Root,"soft","cache");Directory.CreateDirectory(cache);
            var zip=Path.Combine(cache,Guid.NewGuid().ToString("N")+".zip");
            try
            {
                using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token))
                {
                    if(!response.IsSuccessStatusCode)return null;
                    var length=response.Content.Headers.ContentLength;var last=-1;
                    await using var input=await response.Content.ReadAsStreamAsync(token);
                    await using var output=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);
                    var buffer=new byte[65536];long total=0;int read;
                    while((read=await input.ReadAsync(buffer,token))>0)
                    {
                        total+=read;if(total>Limit)throw new InvalidDataException("Download is too large.");
                        await output.WriteAsync(buffer.AsMemory(0,read),token);
                        var shown=length>0?(int)(total*100/length.Value):(int)(total>>20);
                        if(shown!=last){last=shown;status.Report("YikaiCMS · "+Tr(settings,"下载","Downloading","ダウンロード")+" "+(length>0?$"{shown}%":$"{shown} MB"));}
                    }
                }
                var work=zip+".dir";
                try
                {
                    await Task.Run(()=>ZipFile.ExtractToDirectory(zip,work,true),token);
                    var top=Directory.GetFileSystemEntries(work);
                    var content=top.Length==1&&Directory.Exists(top[0])?top[0]:work;
                    var versionFile=Path.Combine(content,"config","version.php");
                    if(!File.Exists(versionFile))throw new InvalidDataException("Not a YikaiCMS package.");
                    var version=CmsVersion.Detect(content);
                    if(string.IsNullOrEmpty(version)||!Version.TryParse(version,out _))throw new InvalidDataException("YikaiCMS version not found.");
                    var target=Path.Combine(cache,"yikaicms-"+version);
                    if(File.Exists(Path.Combine(target,"config","version.php")))return target;
                    Directory.Move(content,target);
                    status.Report("YikaiCMS "+version+" · "+Tr(settings,"已就绪","ready","準備完了"));
                    return target;
                }
                finally{if(Directory.Exists(work))Directory.Delete(work,true);}
            }
            finally{if(File.Exists(zip))File.Delete(zip);}
        }
        catch(Exception e) when(e is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            status.Report("YikaiCMS · "+Tr(settings,"官网下载失败，尝试其它来源…","Vendor download failed; trying other sources…","ベンダーからの取得に失敗、他の取得元を試します…"));
            return null;
        }
    }

    public static async Task<string> WordPressAsync(Settings settings,IProgress<string> status,CancellationToken token)
    {
        using var client=Client();
        status.Report("WordPress · "+Tr(settings,"检查最新版本…","Checking for the latest version…","最新版を確認中…"));
        var locale=settings.Language=="ja"?"ja":settings.Language=="en"?"en_US":"zh_CN";
        using var doc=JsonDocument.Parse(await client.GetStringAsync("https://api.wordpress.org/core/version-check/1.7/?locale="+locale,token));
        var offer=doc.RootElement.GetProperty("offers")[0];
        var version=offer.GetProperty("version").GetString()!;var url=offer.GetProperty("download").GetString()!;var got=offer.GetProperty("locale").GetString()!;
        if(!Version.TryParse(version,out _)||!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.Host.EndsWith("wordpress.org"))throw new InvalidDataException("Invalid WordPress release data.");
        var target=Path.Combine(settings.Root,"soft","cache",$"wordpress-{got}-{version}");
        if(File.Exists(Path.Combine(target,"wp-config-sample.php")))return target;
        var sha1=(await client.GetStringAsync(url+".sha1",token)).Trim();
        if(sha1.Length!=40||!sha1.All(Uri.IsHexDigit))throw new InvalidDataException("WordPress checksum unavailable.");
        await DownloadAndExtract(client,url,sha1,SHA1.Create(),target,"WordPress "+version,settings,status,token);
        return target;
    }

    static string Tr(Settings s,string zh,string en,string ja)=>s.Language=="en"?en:s.Language=="ja"?ja:zh;
    static HttpClient Client(){var c=new HttpClient{Timeout=TimeSpan.FromMinutes(10)};c.DefaultRequestHeaders.UserAgent.ParseAdd("YikaiPanel/"+PanelUpdate.CurrentVersion);return c;}

    // 下载到临时文件并校验，解压到临时目录；压缩包只有一个顶层目录时去掉这一层，最后整体改名为缓存目录。
    static async Task DownloadAndExtract(HttpClient client,string url,string expected,HashAlgorithm hasher,string target,string label,Settings settings,IProgress<string> status,CancellationToken token)
    {
        using var _=hasher;
        var cache=Path.GetDirectoryName(target)!;Directory.CreateDirectory(cache);
        var zip=Path.Combine(cache,Guid.NewGuid().ToString("N")+".zip");var work=zip+".dir";
        try
        {
            using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token))
            {
                response.EnsureSuccessStatusCode();var length=response.Content.Headers.ContentLength;
                await using var input=await response.Content.ReadAsStreamAsync(token);
                await using var output=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);
                var buffer=new byte[65536];long total=0;int read;var last=-1;
                while((read=await input.ReadAsync(buffer,token))>0)
                {
                    total+=read;if(total>Limit)throw new InvalidDataException("Download is too large.");
                    await output.WriteAsync(buffer.AsMemory(0,read),token);
                    var shown=length>0?(int)(total*100/length.Value):(int)(total>>20);
                    if(shown!=last){last=shown;status.Report($"{label} · {Tr(settings,"下载","Downloading","ダウンロード")} "+(length>0?$"{shown}%":$"{shown} MB"));}
                }
            }
            await using(var check=File.OpenRead(zip))
                if(!Convert.ToHexString(await hasher.ComputeHashAsync(check,token)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(label+" checksum mismatch.");
            status.Report(label+" · "+Tr(settings,"解压…","Extracting…","展開中…"));
            await Task.Run(()=>{
                using var archive=ZipFile.OpenRead(zip);var root=Path.GetFullPath(work)+Path.DirectorySeparatorChar;
                foreach(var entry in archive.Entries)
                {
                    var path=Path.GetFullPath(Path.Combine(work,entry.FullName));
                    if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unsafe archive entry.");
                    if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(path);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path);
                }
            },token);
            var top=Directory.GetFileSystemEntries(work);
            var content=top.Length==1&&Directory.Exists(top[0])?top[0]:work;
            if(Directory.Exists(target))Directory.Delete(target,true);
            Directory.Move(content,target);
        }
        finally{try{if(File.Exists(zip))File.Delete(zip);if(Directory.Exists(work))Directory.Delete(work,true);}catch(IOException){}}
    }

    // 生成 wp-config.php：数据库连接指向面板的 MySQL，密钥随机生成。
    public static void WriteWordPressConfig(string directory,string database,string user,string password,int port)
    {
        var sample=File.ReadAllText(Path.Combine(directory,"wp-config-sample.php"));
        static string Php(string v)=>v.Replace("\\","\\\\").Replace("'","\\'");
        sample=sample.Replace("'database_name_here'","'"+Php(database)+"'").Replace("'username_here'","'"+Php(user)+"'").Replace("'password_here'","'"+Php(password)+"'").Replace("'localhost'","'127.0.0.1:"+port+"'");
        const string chars="abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#%^&*()-_=+[]{}<>~|;:,.?";
        while(sample.Contains("'put your unique phrase here'"))
        {
            var key=new string(Enumerable.Range(0,64).Select(_=>chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
            const string marker="'put your unique phrase here'";
            var at=sample.IndexOf(marker,StringComparison.Ordinal);
            sample=sample[..at]+"'"+key+"'"+sample[(at+marker.Length)..];
        }
        File.WriteAllText(Path.Combine(directory,"wp-config.php"),sample,new UTF8Encoding(false));
    }
}
