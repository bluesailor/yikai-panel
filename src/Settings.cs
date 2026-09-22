using System.Text.Json;
using System.Text.RegularExpressions;

namespace YikaiLocal;

public sealed class Site
{
    public bool Starred { get; set; }
    public string Title { get; set; } = "";
    public string Template { get; set; } = "yikaicms";
    public bool Enabled { get; set; } = true;
    public string Id { get; set; } = "yikaicms";
    public string Domain { get; set; } = "yikaicms.yikai";
    public string Directory { get; set; } = "";
    public string? RewriteRules { get; set; }
    public string AdminPath { get; set; } = "";
    public string AdminPathSource { get; set; } = "";
    public DateTime? AdminPathRecordedAt { get; set; }
    public string Php { get; set; } = "8.2";
    public string Database { get; set; } = "mysql80";
    public string DatabaseName { get; set; } = "yikaicms";
    // 项目专属数据库账号：只对本项目数据库有权限。旧项目为空时继续使用 root。
    public string? DatabaseUser { get; set; }
    public string? DatabasePassword { get; set; }
    public int HttpPort { get; set; } = 8081;
    public int FastCgiPort { get; set; } = 9082;
    // 用户指定过端口（例如常用端口 80）：运行期不再自动换成空闲端口，被占用时明确报错。
    public bool PortPinned { get; set; }
    // HTTPS：证书来源 auto（面板本地根证书签发）或 custom（用户导入，复制到 config/ssl/custom）。
    public bool Https { get; set; }
    public int HttpsPort { get; set; }
    public string CertificateSource { get; set; } = "auto";
    // PHP 扩展：相对该 PHP 版本 php.ini 的增减；两个都为空表示完全跟随版本设置。
    public List<string>? ExtensionsOn { get; set; }
    public List<string>? ExtensionsOff { get; set; }
    public override string ToString() => string.IsNullOrWhiteSpace(Title)?Domain:Title;
}

public sealed partial class Settings
{
    public int SchemaVersion { get; set; } = 2;
    public string Root { get; set; } = "";
    public string Language { get; set; } = "zh";
    public string WebServer { get; set; } = "nginx";
    public string PhpDefault { get; set; } = "8.2";
    public string MysqlActive { get; set; } = "mysql80";
    public bool AutoStart { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    // 外观：主题 light / dark / system（跟随系统），界面字号 9–13 磅。
    public string Theme { get; set; } = "light";
    public float FontSize { get; set; } = 10f;
    // 编辑框（配置文件、伪静态规则）用的等宽字体。
    public string CodeFont { get; set; } = "Consolas";
    public float CodeFontSize { get; set; } = 10f;
    // 局域网访问：开启后项目的 Web 端口监听 0.0.0.0；数据库页面、MySQL 和 FastCGI 始终只在本机。
    public bool LanAccess { get; set; }
    public int SidebarWidth { get; set; } = 360;
    // 窗口大小按 96 DPI 逻辑像素保存；为空时按屏幕可用区域决定首次大小。
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public int DbManagerPort { get; set; } = 8878;
    public int DbFastCgiPort { get; set; } = 9084;
    public int Mysql57Port { get; set; } = 3307;
    public int Mysql80Port { get; set; } = 3308;
    // 用户指定过 MySQL 端口：未初始化的实例也不再自动换端口（见 DatabasePortPinned）。
    public bool Mysql57PortPinned { get; set; }
    public bool Mysql80PortPinned { get; set; }
    public string MysqlUser { get; set; } = "root";
    public string MysqlPassword { get; set; } = "123456";
    public string? Mysql80Password { get; set; }
    public string? Mysql57Password { get; set; }
    // 新建 YikaiCMS 项目后自动完成安装（用 CMS 自带的安装接口），后台账号默认 admin / yikai888
    public bool AutoInstallCms { get; set; } = true;
    public string CmsAdminUser { get; set; } = "admin";
    public string CmsAdminPassword { get; set; } = "yikai888";
    // CMS 模板下载地址（最小包不带模板，新建 YikaiCMS 项目时按需下载）
    public string CmsPackageUrl { get; set; } = "https://down.yikai.cn/soft/yikaicms/yikaicms-latest.zip";
    public List<Site> Sites { get; set; } = [];
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public string FileName => Path.Combine(Root, "config", "panel.json");
    public static Settings Load(string root)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.Combine(root, "config", "panel.json");
        var settings = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json)! : new Settings();
        settings.Root = root;
        settings.Normalize();
        // 精简安装（最小包只带一个 PHP 版本）时，把默认版本落到实际存在的版本上：
        // 否则默认站点与新项目会指向没装的 8.2，启动时报错。
        foreach(var candidate in new[]{settings.PhpDefault,"8.2","8.5","8.0"})
            if(File.Exists(Path.Combine(root,"soft","php",candidate,"php-cgi.exe"))){settings.PhpDefault=candidate;break;}
        // 默认站点只在确实有内容时登记：完整包随包带默认站点，最小包不带（第一次新建项目时在线获取 CMS 模板）
        var defaultSite = Path.Combine(root, "wwwroot", "yikaicms.yikai");
        var cmsTemplate = Path.Combine(root, "soft", "packages", "yikaicms", "config", "version.php");
        if (!File.Exists(path) && settings.Sites.Count == 0 && (Directory.Exists(defaultSite) || File.Exists(cmsTemplate)))
        {
            Directory.CreateDirectory(defaultSite);
            settings.Sites.Add(new Site { Directory = defaultSite, Php = settings.PhpDefault=="8.0"?"8.2":settings.PhpDefault });
        }
        settings.Save();
        return settings;
    }
    void Normalize()
    {
        if(SchemaVersion < 2) SchemaVersion = 2;
        if(Language is not ("zh" or "en" or "ja")) Language = "zh";
        if(WebServer is not ("nginx" or "apache")) WebServer = "nginx";
        if(PhpDefault is not ("8.0" or "8.2" or "8.5")) PhpDefault = "8.2";
        if(MysqlActive is not ("mysql57" or "mysql80")) MysqlActive = "mysql80";
        if(Theme is not ("light" or "dark" or "system")) Theme = "light";
        FontSize = Math.Clamp(FontSize <= 0 ? 10f : FontSize, 9f, 13f);
        if(string.IsNullOrWhiteSpace(CodeFont)) CodeFont = "Consolas";
        CodeFontSize = Math.Clamp(CodeFontSize <= 0 ? 10f : CodeFontSize, 8f, 18f);
    }
    public void Save()
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);
        File.WriteAllText(FileName + ".tmp", JsonSerializer.Serialize(this, Json));
        File.Move(FileName + ".tmp", FileName, true);
    }
    public int DatabasePort(string kind) => kind == "mysql57" ? Mysql57Port : Mysql80Port;
    public void SetDatabasePort(string kind,int port){if(kind=="mysql57")Mysql57Port=port;else Mysql80Port=port;Save();}
    // 用户指定过端口的实例（常用端口 3306 等）：运行期不再自动换端口，被占用时明确报错。
    public bool DatabasePortPinned(string kind)=>kind=="mysql57"?Mysql57PortPinned:Mysql80PortPinned;
    public void SetDatabasePortPinned(string kind,bool pinned){if(kind=="mysql57")Mysql57PortPinned=pinned;else Mysql80PortPinned=pinned;}
    public string DatabaseVersion(string kind) => kind == "mysql57" ? "5.7" : "8.0";
    public string DatabasePassword(string kind)=>(kind=="mysql57"?Mysql57Password:Mysql80Password)??MysqlPassword;
    public void SetDatabasePassword(string kind,string password){if(kind=="mysql57")Mysql57Password=password;else Mysql80Password=password;Save();}
    public string SqlitePath(Site site) => Path.Combine(site.Directory, "storage", "database.sqlite");
    public static bool ValidDomain(string domain) => domain.Length <= 253 && Regex.IsMatch(domain, @"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{1,62}$");
    static readonly string[] ReservedDatabases=["mysql","information_schema","performance_schema","sys"];
    static readonly string[] ReservedUsers=["root","mysql.sys","mysql.session","mysql.infoschema"];
    public static string DatabaseNameFor(string id)=>id.Length>60?id[..48]+"_"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..8].ToLowerInvariant():id;
    public static string DatabaseUserFor(string databaseName)=>databaseName.Length>32?databaseName[..32]:databaseName;
    public static string GeneratePassword(int length=16)
    {
        const string letters="abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0,length).Select(_=>letters[System.Security.Cryptography.RandomNumberGenerator.GetInt32(letters.Length)]).ToArray());
    }
    // 返回问题代码：range / panel / site / mysql；null 表示可用。
    // 只做“面板自己知道的”冲突检查（范围、面板自身端口、其它项目、MySQL 端口）；
    // “端口被别的程序占用”由调用方用 PortDiagnostics 判断（需要运行时信息）。
    // 低位端口（80/443/3306 等）在 Windows 上不需要管理员，不做额外限制。
    public string? PortProblem(int port,Site? exclude=null)
    {
        if(port<1||port>65535)return "range";
        if(port==DbManagerPort||port==DbFastCgiPort)return "panel";
        if(port==Mysql57Port||port==Mysql80Port)return "mysql";
        foreach(var site in Sites.Where(s=>s!=exclude))
        {
            if(site.HttpPort==port||site.FastCgiPort==port)return "site";
            if(site.Https&&site.HttpsPort==port)return "site";
        }
        return null;
    }
    // 返回问题代码：name / name-used / user / user-used / password；null 表示可用。
    public string? DatabaseProblem(string engine,string databaseName,string? user,string? password)
    {
        if(engine is not ("mysql80" or "mysql57"))return null;
        if(!Regex.IsMatch(databaseName,"^[A-Za-z0-9_]{1,64}$")||ReservedDatabases.Contains(databaseName.ToLowerInvariant()))return "name";
        if(Sites.Any(s=>s.Database==engine&&s.DatabaseName.Equals(databaseName,StringComparison.OrdinalIgnoreCase)))return "name-used";
        if(user is null)return null;
        if(!Regex.IsMatch(user,"^[A-Za-z0-9_]{1,32}$")||ReservedUsers.Contains(user.ToLowerInvariant()))return "user";
        if(Sites.Any(s=>s.Database==engine&&string.Equals(s.DatabaseUser,user,StringComparison.OrdinalIgnoreCase)))return "user-used";
        if(string.IsNullOrEmpty(password)||password.Length>64||password.Any(char.IsControl))return "password";
        return null;
    }
    public Site AddSite(string name, string php, string database, string template="yikaicms", string? existingDirectory=null, string title="", string? databaseName=null, string? databaseUser=null, string? databasePassword=null, string? sourceDirectory=null, bool registerExisting=false)
    {
        if(!new[]{"mysql80","mysql57","sqlite"}.Contains(database)||!new[]{"8.0","8.2","8.5"}.Contains(php))throw new InvalidOperationException("Unsupported runtime selection.");
        name = name.Trim().ToLowerInvariant();
        var domain = name.Contains('.') ? name : name + ".yikai";
        if (!ValidDomain(domain)) throw new InvalidOperationException("Use a name such as demo or demo.yikai.");
        if (Sites.Any(s => s.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("The site already exists.");
        if(!new[]{"yikaicms","php","import","wordpress"}.Contains(template))throw new InvalidOperationException("Invalid project type.");
        if(template=="yikaicms" && php=="8.0")throw new InvalidOperationException("YikaiCMS requires PHP 8.2 or later.");
        if(template=="wordpress" && database=="sqlite")throw new InvalidOperationException("WordPress requires MySQL.");
        var id = Regex.Replace(domain, "[^a-z0-9]", "_");
        if(Sites.Any(s=>s.Id==id))id+="_"+Guid.NewGuid().ToString("N")[..8];
        var dbName=string.IsNullOrWhiteSpace(databaseName)?DatabaseNameFor(id):databaseName.Trim();
        var dbUser=string.IsNullOrWhiteSpace(databaseUser)?null:databaseUser.Trim();
        if(dbUser!=null&&database=="sqlite")dbUser=null;
        // 在复制任何文件之前校验数据库设置，失败时不留下半成品目录。
        switch(DatabaseProblem(database,dbName,dbUser,databasePassword))
        {
            case "name":throw new InvalidOperationException("Database name: use 1-64 letters, digits or underscores (not a system database).");
            case "name-used":throw new InvalidOperationException("Another project already uses this database name.");
            case "user":throw new InvalidOperationException("Database user: use 1-32 letters, digits or underscores (not root).");
            case "user-used":throw new InvalidOperationException("Another project already uses this database user.");
            case "password":throw new InvalidOperationException("Database password: 1-64 characters without line breaks.");
        }
        // 新建项目可指定目标目录（面板“…”选择的位置 + 域名，或命令行 --directory），默认 wwwroot\<域名>。
        var dir = template=="import"?Path.GetFullPath(existingDirectory??throw new InvalidOperationException("Choose a folder.")):string.IsNullOrWhiteSpace(existingDirectory)?Path.Combine(Root, "wwwroot", domain):Path.GetFullPath(existingDirectory);
        if(dir.IndexOfAny(['"','\r','\n'])>=0)throw new InvalidOperationException("Unsupported folder path.");
        if (template!="import" && !registerExisting && System.IO.Directory.Exists(dir)) throw new InvalidOperationException("The folder already exists.");
        if(template=="import" && !System.IO.Directory.Exists(dir))throw new InvalidOperationException("The folder does not exist.");
        if(Sites.Any(s=>Path.GetFullPath(s.Directory).TrimEnd('\\').Equals(dir.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("The folder is already registered.");
        if(!registerExisting && template is ("yikaicms" or "wordpress")){
        var source = sourceDirectory ?? (template=="yikaicms" ? Path.Combine(Root, "soft", "packages", "yikaicms") : throw new InvalidOperationException("Unsupported template."));
        if(!Directory.Exists(source))throw new InvalidOperationException("CMS package is missing. Download it first.");
        foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dir, Path.GetRelativePath(source, file));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        }
        // 扫描已有目录登记时（registerExisting）不动目录内容：既不覆盖它自己的 index.php，也不塞一个 storage 目录进去
        if(template=="php" && !registerExisting){System.IO.Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"index.php"),"<?php declare(strict_types=1); ?><!doctype html><meta charset=\"utf-8\"><title>Yikai Panel</title><h1>Yikai Panel</h1><p>PHP project is ready.</p>");}
        if(template=="wordpress")ProjectSources.WriteWordPressConfig(dir,dbName,dbUser??MysqlUser,dbUser==null?DatabasePassword(database):databasePassword!,DatabasePort(database));
        if(template!="wordpress" && !registerExisting)System.IO.Directory.CreateDirectory(Path.Combine(dir, "storage"));
        var site = new Site { Title=title.Trim(), Template=template, Enabled=false, Id = id, Domain = domain, Directory = dir, Php = php, Database = database, DatabaseName = dbName, DatabaseUser = dbUser, DatabasePassword = dbUser==null?null:databasePassword, HttpPort = Math.Max(8080,Sites.Select(s=>s.HttpPort).DefaultIfEmpty(8080).Max())+1, FastCgiPort = Math.Max(DbFastCgiPort,Sites.Select(s=>s.FastCgiPort).DefaultIfEmpty(9082).Max())+1 };
        Sites.Add(site); Save(); return site;
    }
}
