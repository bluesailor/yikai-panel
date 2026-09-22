using System.Text;

namespace YikaiLocal;

// Web 服务器（Nginx / Apache 二选一，共用项目端口）与单个服务的启动、停止、重启。
public sealed partial class Runtime
{
    public const string ApacheVersion="2.4.39";
    public static readonly string[] PhpVersions=["8.0","8.2","8.5"];
    // "php" 表示全部项目的 PHP；"php8.2" 等只针对使用该版本的项目。
    public static readonly string[] Services=["web","php","php8.0","php8.2","php8.5","mysql80","mysql57","dbpage"];
    public static string? PhpVersionOf(string service)=>service.Length>3&&service.StartsWith("php")&&PhpVersions.Contains(service[3..])?service[3..]:null;
    public bool PhpInstalled(string version)=>File.Exists(Path.Combine(Root,"soft","php",version,"php-cgi.exe"));
    // 已安装的版本（最小包只带一个 PHP 时，界面只应列出实际存在的）
    public string[] InstalledPhpVersions=>PhpVersions.Where(PhpInstalled).ToArray();
    public bool MysqlInstalled(string kind)=>File.Exists(Path.Combine(Root,"soft","mysql",Settings.DatabaseVersion(kind),"bin","mysqld.exe"));
    // 面板自己用哪个 PHP 跑数据库页面与检查脚本：优先 8.2（完整包），否则用默认版本，再否则用已安装的最高版本。
    // 最小包可以只带 PHP 8.5，这条规则保证面板自身功能不依赖某个固定版本。
    public string InternalPhpVersion => InternalPhpVersionFor(Settings);
    // 静态版本：PhpStudyImport 等非 Runtime 代码也要用同一套解析
    public static string InternalPhpVersionFor(Settings settings)
    {
        var root=settings.Root;
        foreach(var candidate in new[]{settings.PhpDefault,"8.2","8.5","8.0"})
            if(File.Exists(Path.Combine(root,"soft","php",candidate,"php-cgi.exe")))return candidate;
        return settings.PhpDefault;
    }
    IEnumerable<Site> PhpSites(string? version)=>Settings.Sites.Where(s=>version==null||s.Php==version);
    string[] PhpKeys(string? version)=>version==null?SitePhpKeys.ToArray():PhpSites(version).Select(s=>"php-"+s.Id).Where(Alive).ToArray();
    public string WebKey=>Settings.WebServer;
    public static string WebServerName(string kind)=>kind=="apache"?"Apache "+ApacheVersion:"Nginx";
    string NginxExecutable=>Path.Combine(Root,"soft","nginx","nginx.exe");
    string ApacheHome=>Path.Combine(Root,"soft","apache",ApacheVersion);
    string ApacheExecutable=>Path.Combine(ApacheHome,"bin","httpd.exe");
    string ApacheConfig=>Path.Combine(Root,"config","panel-apache.conf");
    public bool WebServerInstalled(string kind)=>File.Exists(kind=="apache"?ApacheExecutable:NginxExecutable);
    bool WebAlive=>Alive(WebKey);
    public bool WebServerRunning=>WebAlive;
    IEnumerable<string> SitePhpKeys=>processes.Keys.Where(k=>k.StartsWith("php-")&&k!="php-db").ToArray();

    public async Task ReloadWebServer()
    {
        await StopWebServer(WebKey=="apache"?"nginx":"apache");
        if(WebKey=="apache")await ReloadApache();else await ReloadNginxServer();
    }
    async Task ReloadApache()
    {
        if(!WebServerInstalled("apache"))throw new FileNotFoundException(ResetText("未找到 Apache "+ApacheVersion+" 组件，请确认 soft\\apache\\"+ApacheVersion+" 完整。","Apache "+ApacheVersion+" is not installed in soft\\apache.","Apache "+ApacheVersion+" が soft\\apache にありません。"),ApacheExecutable);
        var previous=File.Exists(ApacheConfig)?File.ReadAllText(ApacheConfig):null;
        WriteApache();
        try{await Run(ApacheExecutable,["-t","-f",Slash(ApacheConfig)]);}
        catch{if(previous!=null)File.WriteAllText(ApacheConfig,previous);throw;}
        // Windows 控制台模式的 Apache 不支持 -k restart 信号，重载即重启父子进程。
        await StopWebServer("apache");
        Start("apache",ApacheExecutable,"-f",Slash(ApacheConfig));
        await WaitPort("apache",Settings.DbManagerPort);
    }
    async Task StopWebServer(string kind)
    {
        if(!Alive(kind)){processes.Remove(kind);return;}
        var process=processes[kind];
        if(kind=="nginx")
        {
            try{await Run(NginxExecutable,["-p",Slash(Path.Combine(Root,"soft","nginx"))+"/","-c",Slash(NginxConfig),"-s","quit"]);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));}
            catch(Exception e) when(e is IOException or TimeoutException){if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
        }
        else{process.Kill(true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));File.Delete(Path.Combine(Root,"temp","panel-apache.pid"));}
        processes.Remove(kind);executablePaths.Remove(kind);SaveProcesses();Log("Stopped · "+WebServerName(kind));
    }
    public async Task SwitchWebServerAsync(string kind)
    {
        if(kind is not ("nginx" or "apache"))throw new ArgumentException("Unknown web server.");
        if(!WebServerInstalled(kind))throw new FileNotFoundException(ResetText($"未找到 {WebServerName(kind)} 组件。",$"{WebServerName(kind)} is not installed.",$"{WebServerName(kind)} がありません。"));
        if(kind==Settings.WebServer)return;
        var previous=Settings.WebServer;var wasRunning=Alive("nginx")||Alive("apache");
        Settings.WebServer=kind;Settings.Save();Log("Web server · "+WebServerName(kind));
        if(!wasRunning)return;
        try{await StopWebServer(previous);PreparePorts();await ReloadWebServer();}
        catch
        {
            Settings.WebServer=previous;Settings.Save();
            try{await StopWebServer(kind);await ReloadWebServer();}catch(Exception e) when(e is IOException or TimeoutException){Log("Rollback failed · "+e.Message);}
            throw;
        }
    }

    public async Task StartServiceAsync(string service)
    {
        switch(service)
        {
            case "web":PreparePorts();await ReloadWebServer();break;
            case "php" or "php8.0" or "php8.2" or "php8.5":
            {
                var version=PhpVersionOf(service);
                if(version!=null&&!PhpInstalled(version))throw new FileNotFoundException(ResetText($"未找到 PHP {version} 组件（soft\\php\\{version}）。",$"PHP {version} is not installed (soft\\php\\{version}).",$"PHP {version} がありません（soft\\php\\{version}）。"));
                PreparePorts();
                foreach(var site in PhpSites(version).Where(s=>s.Enabled))await StartSitePhp(site);
                if(WebAlive)await ReloadWebServer();
                break;
            }
            case "mysql80" or "mysql57":
                await EnsureDatabaseAsync(service);
                // 补建 PHP 先于 MySQL 单独启动时跳过的项目数据库（CREATE DATABASE IF NOT EXISTS，可重复执行）。
                foreach(var site in Settings.Sites.Where(s=>s.Enabled&&s.Database==service&&Alive("php-"+s.Id)))await PrepareSiteDatabase(site);
                break;
            case "dbpage":PreparePorts();await StartDatabasePage();if(WebAlive)await ReloadWebServer();break;
            default:throw new ArgumentException("Unknown service.");
        }
        Log("Started · "+service);
    }
    public async Task StopServiceAsync(string service)
    {
        switch(service)
        {
            case "web":await StopWebServer("nginx");await StopWebServer("apache");break;
            case "php" or "php8.0" or "php8.2" or "php8.5":foreach(var key in PhpKeys(PhpVersionOf(service)))await StopProcess(key);break;
            case "mysql80" or "mysql57":await StopOwnedDatabase(service);break;
            case "dbpage":await StopProcess("php-db");break;
            default:throw new ArgumentException("Unknown service.");
        }
        Log("Stopped · "+service);
    }
    public async Task RestartServiceAsync(string service){await StopServiceAsync(service);await StartServiceAsync(service);}
    async Task StopProcess(string key)
    {
        if(Alive(key)){processes[key].Kill();await processes[key].WaitForExitAsync();}
        processes.Remove(key);executablePaths.Remove(key);SaveProcesses();
    }

    void WriteApache()
    {
        var root=Slash(Root);var sb=new StringBuilder();
        sb.Append($"# 由易开面板生成，手动修改会被覆盖。\nServerRoot \"{Slash(ApacheHome)}\"\nServerName localhost\nPidFile \"{root}/temp/panel-apache.pid\"\nDefaultRuntimeDir \"{root}/temp\"\nErrorLog \"{root}/logs/panel-apache-error.log\"\nLogLevel warn\n");
        foreach(var module in new[]{"access_compat","alias","auth_basic","authn_core","authn_file","authz_core","authz_groupfile","authz_host","authz_user","autoindex","deflate","dir","env","expires","filter","headers","log_config","mime","proxy","proxy_fcgi","rewrite","setenvif","ssl","version"})
            sb.Append($"LoadModule {module}_module modules/mod_{module}.so\n");
        sb.Append($"TypesConfig conf/mime.types\nAcceptFilter http none\nAcceptFilter https none\nEnableSendfile Off\nEnableMMAP Off\nThreadsPerChild 64\nTimeout 600\nProxyTimeout 600\nLimitRequestBody 167772160\nKeepAlive On\nServerTokens Prod\n");
        sb.Append($"LogFormat \"%h %l %u %t \\\"%r\\\" %>s %b \\\"%{{Referer}}i\\\" \\\"%{{User-Agent}}i\\\"\" combined\nCustomLog \"{root}/logs/panel-apache-access.log\" combined\nDirectoryIndex index.php index.html\n");
        sb.Append("<Directory />\n    Options FollowSymLinks\n    AllowOverride None\n    Require all denied\n</Directory>\n<Files \".ht*\">\n    Require all denied\n</Files>\n");
        // 自定义配置（config/custom-apache.conf）：在面板默认值之后、各 VirtualHost 之前载入，同名指令覆盖默认值。
        sb.Append($"IncludeOptional \"{Slash(CustomConfigPath("apache"))}\"\n");
        var secure=Settings.Sites.Where(SslReady).ToList();
        foreach(var port in Settings.Sites.Select(s=>s.HttpPort).Concat(secure.Select(s=>s.HttpsPort)).Distinct())sb.Append($"Listen {WebBind}:{port}\n");
        sb.Append($"Listen 127.0.0.1:{Settings.DbManagerPort}\n");
        foreach(var (site,port,ssl) in Settings.Sites.Select(s=>(s,s.HttpPort,false)).Concat(secure.Select(s=>(s,s.HttpsPort,true))))
        {
            var dir=Slash(site.Directory);
            sb.Append($"\n<VirtualHost {WebBind}:{port}>\n    ServerName {site.Domain}\n    DocumentRoot \"{dir}\"\n");
            if(ssl){var (certificate,key)=CertificatePaths(site);sb.Append($"    SSLEngine on\n    SSLProtocol -all +TLSv1.2\n    SSLCertificateFile \"{Slash(certificate)}\"\n    SSLCertificateKeyFile \"{Slash(key)}\"\n");}
            if(!site.Enabled){sb.Append("    Redirect 503 /\n</VirtualHost>\n");continue;}
            sb.Append($"    <Directory \"{dir}\">\n        Options FollowSymLinks\n        AllowOverride All\n        Require all granted\n    </Directory>\n");
            sb.Append("    <LocationMatch \"/\\.\">\n        Require all denied\n    </LocationMatch>\n    <LocationMatch \"^/storage/\">\n        Require all denied\n    </LocationMatch>\n");
            // 与 Nginx 通用模板一致：非 YikaiCMS 项目把不存在的地址交给 index.php；YikaiCMS 使用自带 .htaccess。
            if(site.Template!="yikaicms")sb.Append("    FallbackResource /index.php\n");
            sb.Append(ApachePhpHandler(site.FastCgiPort)).Append("</VirtualHost>\n");
        }
        var manager=root+"/soft/db-manager";
        sb.Append($"\n<VirtualHost 127.0.0.1:{Settings.DbManagerPort}>\n    ServerName localhost\n    DocumentRoot \"{manager}\"\n    <Directory \"{manager}\">\n        Options None\n        AllowOverride None\n        Require all granted\n    </Directory>\n");
        sb.Append("    <LocationMatch \"^/vendor/\">\n        Require all denied\n    </LocationMatch>\n    <LocationMatch \"^/(bootstrap|common|lang)\\.php\">\n        Require all denied\n    </LocationMatch>\n");
        sb.Append("    <LocationMatch \"^/(?!adminer\\.php/[a-z0-9_]+/(zh|en|ja)$)[^/]+\\.php/\">\n        Require all denied\n    </LocationMatch>\n");
        sb.Append(ApachePhpHandler(Settings.DbFastCgiPort)).Append("</VirtualHost>\n");
        Directory.CreateDirectory(Path.Combine(Root,"config"));Directory.CreateDirectory(Path.Combine(Root,"logs"));Directory.CreateDirectory(Path.Combine(Root,"temp"));
        File.WriteAllText(ApacheConfig,sb.ToString(),new UTF8Encoding(false));
    }
    // Windows 路径以盘符开头，mod_proxy_fcgi 会把 SCRIPT_FILENAME 写成 proxy:fcgi://host:port/D:/...，
    // php-cgi 不识别该前缀，这里去掉前缀并清除同样带前缀的 PATH_TRANSLATED。
    static string ApachePhpHandler(int port)=>
        $"    <FilesMatch \"\\.php$\">\n        <If \"-f %{{REQUEST_FILENAME}}\">\n            SetHandler \"proxy:fcgi://127.0.0.1:{port}/\"\n        </If>\n    </FilesMatch>\n"+
        "    ProxyFCGISetEnvIf \"reqenv('SCRIPT_FILENAME') =~ m#^proxy:fcgi://[^/]+/(.+)$#\" SCRIPT_FILENAME \"$1\"\n    ProxyFCGISetEnvIf \"true\" !PATH_TRANSLATED\n";

    // Any：至少一个进程在运行；All：该服务应有的进程全部在运行。
    public (bool Any,bool All) ServiceState(string service)
    {
        if(service is "php" or "php8.0" or "php8.2" or "php8.5")
        {
            var version=PhpVersionOf(service);
            var enabled=PhpSites(version).Where(s=>s.Enabled).Select(s=>Alive("php-"+s.Id)).ToList();
            return (PhpKeys(version).Length>0,enabled.Count>0&&enabled.All(alive=>alive));
        }
        var alive=Alive(service switch{"web"=>WebKey,"dbpage"=>"php-db",_=>service});
        return (alive,alive);
    }
    public IReadOnlyList<Site> AffectedSites(string service)=>Settings.Sites.Where(s=>s.Enabled&&(service is "web" or "php"||s.Php==PhpVersionOf(service)||s.Database==service)).ToList();
}
