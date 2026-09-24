using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YikaiLocal;

public sealed record OwnedProcess(string Key, int Pid, long Started, string Executable);

public sealed partial class Runtime(Settings settings)
{
    public Settings Settings { get; } = settings;
    readonly Dictionary<string, Process> processes = new();
    readonly Dictionary<string, string> executablePaths = new();
    public event Action<string>? Progress;
    string Root => Settings.Root;
    string NginxConfig => Path.Combine(Root, "config", "panel-nginx.conf");
    string ProcessState => Path.Combine(Root, "temp", "panel-processes.json");
    static string Slash(string path) => path.Replace('\\', '/');
    bool Alive(string key)=>processes.TryGetValue(key,out var p)&&!p.HasExited;
    public bool ServiceRunning(string key)=>Alive(key);
    public int? ServicePid(string key)=>Alive(key)?processes[key].Id:null;
    public bool IsRunning(Site site)=>site.Enabled&&Alive(WebKey)&&Alive("php-"+site.Id)&&(site.Database is "sqlite" or "none"||Alive(site.Database));
    public bool Running => Alive(WebKey)&&Alive("php-db")&&Alive(Settings.MysqlActive)&&Settings.Sites.Where(s=>s.Enabled).All(IsRunning);
    public bool AnyRunning => processes.Values.Any(p => !p.HasExited);
    void Log(string text) { Progress?.Invoke(text); Directory.CreateDirectory(Path.Combine(Root,"logs")); File.AppendAllText(Path.Combine(Root,"logs","panel.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}\n"); }
    public static IDisposable Lock(Settings settings){Directory.CreateDirectory(Path.Combine(settings.Root,"temp"));return new FileStream(Path.Combine(settings.Root,"temp","panel-operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    public void Adopt()
    {
        if (!File.Exists(ProcessState)) return;
        var entries=JsonSerializer.Deserialize<List<OwnedProcess>>(File.ReadAllText(ProcessState))??[];
        processes.Clear();executablePaths.Clear();
        foreach (var entry in entries)
        {
            try
            {
                var p = Process.GetProcessById(entry.Pid);
                if (p.StartTime.ToUniversalTime().Ticks == entry.Started && string.Equals(p.MainModule?.FileName, entry.Executable, StringComparison.OrdinalIgnoreCase)) { processes[entry.Key] = p; executablePaths[entry.Key]=entry.Executable; }
            }
            catch { /* The recorded child has already exited. */ }
        }
    }
    void SaveProcesses()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProcessState)!);
        var records = processes.Where(x => !x.Value.HasExited).Select(x => new OwnedProcess(x.Key,x.Value.Id,x.Value.StartTime.ToUniversalTime().Ticks,executablePaths[x.Key])).ToList();
        File.WriteAllText(ProcessState+".tmp", JsonSerializer.Serialize(records));
        File.Move(ProcessState+".tmp",ProcessState,true);
    }
    static ProcessStartInfo Info(string executable, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach(var arg in args) info.ArgumentList.Add(arg);
        return info;
    }
    async Task<string> Run(string executable, string[] args, int timeout = 60)
    {
        var info = Info(executable,args); info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        using var p = Process.Start(info) ?? throw new IOException("Process could not start.");
        var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeout)); }
        catch { if(!p.HasExited) p.Kill(); throw; }
        var text = await output + await error;
        if(p.ExitCode != 0) throw new IOException($"{Path.GetFileName(executable)}: "+(string.IsNullOrWhiteSpace(text)?$"exit code {p.ExitCode}. See {Root}\\logs.":text.Trim()));
        return text;
    }
    void Start(string key, string executable, params string[] args)
    {
        if(processes.TryGetValue(key,out var old) && !old.HasExited) return;
        executablePaths[key]=executable;
        processes[key] = Process.Start(Info(executable,args)) ?? throw new IOException("Process could not start.");
        SaveProcesses();
    }
    static int FreePort(int desired, HashSet<int> used)
    {
        for(var port=desired;port < desired+500;port++)
        {
            if(used.Contains(port)) continue;
            if(System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint=>endpoint.Port==port))continue;
            used.Add(port);return port;
        }
        throw new IOException("No available local port.");
    }
    async Task WaitPort(string key, int port)
    {
        // 成功条件是“端口由本面板启动的进程监听”，而不是“端口能连通”：
        // 端口被其他程序占用时也能连通，会把启动失败误判为成功（Windows 上 mysqld 父壳在子进程绑定失败后仍短暂存活）。
        for(var i=0;i<300;i++)
        {
            if(processes[key].HasExited) throw new IOException(StartupFailure(key,port,$"{key} stopped."));
            if(PortOwnedBy(key,port)) return;
            await Task.Delay(100);
        }
        throw new IOException(StartupFailure(key,port,$"{key}: startup timed out."));
    }
    bool PortOwnedBy(string key,int port)
    {
        if(PortDiagnostics.ListenerPid(port) is not { } pid) return false;
        if(pid==processes[key].Id) return true;
        // 服务可能在子进程里监听（Windows 上 mysqld 会把自己重启为子进程；nginx 有 worker）：沿父链向上找，最多四层防 PID 复用成环。
        var current=pid;
        for(var depth=0;depth<4;depth++)
        {
            if(PortDiagnostics.ParentPid(current) is not { } parent) return false;
            if(parent==processes[key].Id) return true;
            current=parent;
        }
        return false;
    }
    // 启动失败时把最可能的原因直接带出来：端口被哪个程序占用、服务日志的最后几行，不再只说“看日志”。
    string StartupFailure(string key,int port,string reason)
    {
        var text=new StringBuilder(reason);
        if(PortDiagnostics.ListenerPid(port) is { } pid) text.Append($" Port {port} is in use by {PortDiagnostics.Describe(pid).Replace("\n"," ")}.");
        text.Append($" See {Root}\\logs.");
        var tail=ServiceLogTail(key);
        if(tail.Length>0) text.Append('\n').Append(tail);
        return text.ToString();
    }
    string ServiceLogTail(string key)
    {
        var file=Path.Combine(Root,"logs",key switch {
            "mysql57"=>"panel-mysql57.log","mysql80"=>"panel-mysql80.log",
            "nginx"=>"panel-nginx-error.log","apache"=>"panel-apache-error.log","php-db"=>"phpmyadmin-php.log",
            _=>Settings.Sites.FirstOrDefault(s=>"php-"+s.Id==key) is { } site?$"php-{site.Php}.log":"panel.log"});
        try { return File.Exists(file)?string.Join("\n",File.ReadLines(file).Where(l=>l.Trim().Length>0).TakeLast(6)):""; }
        catch (IOException) { return ""; }
    }
    string Php => Path.Combine(Root,"soft","php",InternalPhpVersion,"php.exe");
    // 分配端口时保留正在运行的监听端口：已安装项目依赖它们。
    void PreparePorts()
    {
        Directory.CreateDirectory(Path.Combine(Root,"temp"));
        var used=new HashSet<int>();var web=WebAlive;
        if(!web)Settings.DbManagerPort=FreePort(Settings.DbManagerPort,used);else used.Add(Settings.DbManagerPort);
        if(!Alive("php-db"))Settings.DbFastCgiPort=FreePort(Settings.DbFastCgiPort,used);else used.Add(Settings.DbFastCgiPort);
        foreach(var kind in new[]{"mysql57","mysql80"})
        {
            var port=Settings.DatabasePort(kind);
            // MySQL 端口会写进已装站点的连接配置，数据初始化之后不再换；首次初始化之前被其他程序占用则自动换一个。
            // 用户指定过端口（常用端口 3306 等）时不换：被占用会在启动时报错并指名占用者。
            if(!Settings.DatabasePortPinned(kind)&&!Alive(kind)&&PortDiagnostics.PortBusy(port)&&!Directory.Exists(Path.Combine(Root,"data",kind,"mysql"))) port=FreePort(port,used);
            Settings.SetDatabasePort(kind,port);
            used.Add(port);
        }
        foreach(var site in Settings.Sites.Where(s=>Alive("php-"+s.Id))){used.Add(site.HttpPort);used.Add(site.FastCgiPort);if(site.Https&&site.HttpsPort>0)used.Add(site.HttpsPort);}
        foreach(var site in Settings.Sites.Where(s=>!Alive("php-"+s.Id)))
        {
            // 指定过端口的项目（常用端口 80 等）保持原样：被占用时由启动报错说明，不静默换端口。
            if(site.PortPinned)used.Add(site.HttpPort);
            else if(!web||!File.Exists(Path.Combine(Root,"config","panel-rewrite-"+site.Id+".conf")))site.HttpPort=FreePort(site.HttpPort,used);
            else used.Add(site.HttpPort);
            site.FastCgiPort=FreePort(site.FastCgiPort,used);
            if(site.Https){if(site.PortPinned&&site.HttpsPort>0)used.Add(site.HttpsPort);else if(!web||site.HttpsPort<=0)site.HttpsPort=FreePort(site.HttpsPort>0?site.HttpsPort:8443,used);else used.Add(site.HttpsPort);}
        }
        Settings.Save();
    }
    // 端口排查：面板管理的每个端口及其归属服务（网站的 http/https 端口由 Web 服务器进程监听）。
    public sealed record PortUse(string Key,string Service,int Port);
    public List<PortUse> PortUses()
    {
        var list=new List<PortUse>();
        foreach(var site in Settings.Sites)
        {
            list.Add(new("http-"+site.Id,WebKey,site.HttpPort));
            if(site.Https&&site.HttpsPort>0)list.Add(new("https-"+site.Id,WebKey,site.HttpsPort));
            list.Add(new("php-"+site.Id,"php-"+site.Id,site.FastCgiPort));
        }
        list.Add(new("php-db","php-db",Settings.DbFastCgiPort));
        list.Add(new("dbpage",WebKey,Settings.DbManagerPort));
        list.Add(new("mysql80","mysql80",Settings.Mysql80Port));
        list.Add(new("mysql57","mysql57",Settings.Mysql57Port));
        return list;
    }
    // 诊断文本：CLI --ports 与端口排查窗口的“复制诊断信息”共用同一份。
    public string PortReport()
    {
        var text=new StringBuilder($"Yikai Panel ports · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · root {Root}\n");
        foreach(var use in PortUses())
        {
            string state;
            if(Alive(use.Service)) state=$"panel PID {ServicePid(use.Service)}";
            else if(PortDiagnostics.ListenerPid(use.Port) is { } pid) state="in use by "+PortDiagnostics.Describe(pid).Replace("\n"," ");
            else state="free";
            text.AppendLine($"{use.Key,-28} {use.Port,-6} {state}");
        }
        return text.ToString();
    }
    async Task StartSitePhp(Site site)
    {
        if(Alive("php-"+site.Id))return;
        Directory.CreateDirectory(Path.Combine(site.Directory,"storage"));
        // 单独启动 PHP 时 MySQL 可能未运行：PHP 照常启动，建库推迟到该 MySQL 启动时（见 StartServiceAsync）。
        if(site.Database is not ("mysql80" or "mysql57")||Alive(site.Database))await PrepareSiteDatabase(site);
        else Log($"MySQL {Settings.DatabaseVersion(site.Database)} not running · database deferred · {site.Domain}");
        Log($"PHP {site.Php} · {site.Domain}");
        var siteIni=SitePhpIni(site);
        Start("php-"+site.Id,Path.Combine(Root,"soft","php",site.Php,"php-cgi.exe"),"-c",siteIni,"-b",$"127.0.0.1:{site.FastCgiPort}");
        await WaitPort("php-"+site.Id,site.FastCgiPort);
    }
    async Task StartDatabasePage()
    {
        // 数据库页面脚本随面板更新；本机被手动改过时保留原文件，只记录日志，不阻止启动。
        try{BundledDatabaseTools.Ensure(Root);}catch(IOException e){Log("Database tools not updated · "+e.Message);}
        Start("php-db",Path.Combine(Root,"soft","php",InternalPhpVersion,"php-cgi.exe"),"-c",Path.Combine(Root,"config","phpmyadmin-php.ini"),"-b",$"127.0.0.1:{Settings.DbFastCgiPort}");
        await WaitPort("php-db",Settings.DbFastCgiPort);
    }
    // 默认一起启动：所选 Web 服务器、启用项目的 PHP、所选 MySQL，以及项目用到的另一版本 MySQL。
    public async Task StartAsync()
    {
        if(Running)return;
        PreparePorts();
        foreach(var kind in Settings.Sites.Where(s=>s.Enabled).Select(s=>s.Database).Prepend(Settings.MysqlActive).Where(k=>k is "mysql80" or "mysql57").Distinct())
            if(!Alive(kind))await StartDatabase(kind);
        foreach(var site in Settings.Sites.Where(s=>s.Enabled))await StartSitePhp(site);
        await StartDatabasePage();
        await ReloadWebServer();Log("Ready");
    }
    public async Task StartSiteAsync(Site site){site.Enabled=true;Settings.Save();await StartAsync();}
    public async Task StopSiteAsync(Site site)
    {
        site.Enabled=false;Settings.Save();
        if(WebAlive)await ReloadWebServer();
        var key="php-"+site.Id;
        if(Alive(key)){processes[key].Kill();await processes[key].WaitForExitAsync();}
        processes.Remove(key);SaveProcesses();Log("Stopped · "+site.Domain);
    }
    async Task ReloadNginxServer()
    {
        var previous=File.Exists(NginxConfig)?File.ReadAllText(NginxConfig):null;
        WriteNginx();var nginx=Path.Combine(Root,"soft","nginx","nginx.exe");
        try{await Run(nginx,["-p",Slash(Path.Combine(Root,"soft","nginx"))+"/","-c",Slash(NginxConfig),"-t"]);}
        catch{if(previous!=null)File.WriteAllText(NginxConfig,previous);throw;}
        if(Alive("nginx"))await Run(nginx,["-p",Slash(Path.Combine(Root,"soft","nginx"))+"/","-c",Slash(NginxConfig),"-s","reload"]);
        else{Start("nginx",nginx,"-p",Slash(Path.Combine(Root,"soft","nginx"))+"/","-c",Slash(NginxConfig));await WaitPort("nginx",Settings.DbManagerPort);}
    }
    public async Task EnsureDatabaseAsync(string kind){if(kind is not ("mysql80" or "mysql57"))throw new IOException("Invalid database engine");if(!Alive(kind))await StartDatabase(kind);}
    async Task StartDatabase(string kind)
    {
        var version=Settings.DatabaseVersion(kind); var data=Path.Combine(Root,"data",kind);
        Directory.CreateDirectory(data); Log($"MySQL {version}");
        var ini=Path.Combine(Root,"config","panel-"+kind+".ini");
        var baseDir=Path.Combine(Root,"soft","mysql",version);
        WriteDatabaseConfig(kind);
        var pending=Path.Combine(data,".yikai-initializing");
        var executable=Path.Combine(baseDir,"bin","mysqld.exe");
        if(!Directory.Exists(Path.Combine(data,"mysql")))
        {
            if(Directory.EnumerateFileSystemEntries(data).Any()) throw new IOException($"Database initialization incomplete: {data}. Preserve the folder and check logs.");
            await Run(executable,["--defaults-file="+ini,"--initialize-insecure"],120);
            File.WriteAllText(pending,"Initialize password on first start");
        }
        var args=new List<string>{"--defaults-file="+ini};
        if(File.Exists(pending))
        {
            var initial=Path.Combine(Root,"temp",kind+"-initial.sql");
            File.WriteAllText(initial,"ALTER USER 'root'@'localhost' IDENTIFIED BY '"+Settings.DatabasePassword(kind).Replace("\\","\\\\").Replace("'","\\'")+"';\n");
            args.Add("--init-file="+initial);
        }
        Start(kind,executable,args.ToArray()); await WaitPort(kind,Settings.DatabasePort(kind));
        await Run(Php,["-c",Path.Combine(Root,"soft","php",InternalPhpVersion,"php.ini"),"-d","display_errors=stderr",Path.Combine(Root,"soft","db-manager","bootstrap.php"),"--check",kind]);
        if(File.Exists(pending)) File.Delete(pending);
    }
    void WriteNginx()
    {
        var root=Slash(Root);
        var text=new StringBuilder($"worker_processes 1;\npid \"{root}/temp/panel-nginx.pid\";\nerror_log \"{root}/logs/panel-nginx-error.log\";\nevents {{ worker_connections 512; }}\nhttp {{\ninclude \"{root}/config/mime.types\";\naccess_log \"{root}/logs/panel-nginx-access.log\";\n");
        // 自定义配置（config/custom-nginx.conf）在 http 块内载入；写了同名指令时不再输出面板默认值，避免 nginx 报重复指令。
        EnsureCustomConfig(new ConfigFile("nginx","",CustomConfigPath("nginx"),"nginx"));var custom=CustomConfigText("nginx");
        if(!HasDirective(custom,"client_max_body_size"))text.Append("client_max_body_size 160m;\n");
        if(!HasDirective(custom,"fastcgi_read_timeout"))text.Append("fastcgi_read_timeout 600s;\n");
        text.Append($"include \"{Slash(CustomConfigPath("nginx"))}\";\n");
        foreach(var site in Settings.Sites)
        {
            var rulePath=Path.Combine(Root,"config","panel-rewrite-"+site.Id+".conf");
            // config/yikaicms-rewrite.conf 由 CMS 模板的 deploy/nginx-server.conf 生成（更换随包 CMS 版本时必须重新生成）：
            // 下面的 9082 占位端口和 `include fastcgi_params;` 字面量是生成脚本与这里的约定，换 CMS 版本后必须重新生成。
            var rules=File.ReadAllText(Path.Combine(Root,"config","yikaicms-rewrite.conf")).Replace("127.0.0.1:9082",$"127.0.0.1:{site.FastCgiPort}").Replace("include fastcgi_params;",$"include \"{root}/config/fastcgi_params\";");
            if(site.Template!="yikaicms")rules=$"location / {{ try_files $uri $uri/ /index.php?$query_string; }} location ~ \\.php$ {{ try_files $uri =404; include \"{root}/config/fastcgi_params\"; fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name; fastcgi_pass 127.0.0.1:{site.FastCgiPort}; }} location ~ /\\. {{ deny all; }} location ^~ /storage/ {{ deny all; }}";
            if(site.RewriteRules is not null)rules=ExpandRewrite(site,site.RewriteRules);
            if(!site.Enabled)rules="location / { return 503; }";
            File.WriteAllText(rulePath,rules);
            text.AppendLine($"server {{ listen {WebBind}:{site.HttpPort}; server_name {site.Domain}; root \"{Slash(site.Directory)}\"; index index.php index.html; include \"{Slash(rulePath)}\"; }}");
            if(SslReady(site)){var (certificate,key)=CertificatePaths(site);text.AppendLine($"server {{ listen {WebBind}:{site.HttpsPort} ssl; server_name {site.Domain}; root \"{Slash(site.Directory)}\"; index index.php index.html; ssl_certificate \"{Slash(certificate)}\"; ssl_certificate_key \"{Slash(key)}\"; ssl_protocols TLSv1.2 TLSv1.3; include \"{Slash(rulePath)}\"; }}");}
        }
        text.AppendLine($"server {{ listen 127.0.0.1:{Settings.DbManagerPort}; server_name localhost 127.0.0.1; root \"{root}/soft/db-manager\"; index index.php; location ^~ /vendor/ {{ deny all; }} location ~ ^/(bootstrap|common|lang)\\.php$ {{ deny all; }} location ~ ^/adminer\\.php/([a-z0-9_]+)/(zh|en|ja)$ {{ include \"{root}/config/fastcgi_params\"; fastcgi_param SCRIPT_FILENAME \"{root}/soft/db-manager/adminer.php\"; fastcgi_param PATH_INFO /$1/$2; fastcgi_pass 127.0.0.1:{Settings.DbFastCgiPort}; }} location / {{ try_files $uri $uri/ =404; }} location ~ \\.php$ {{ try_files $uri =404; include \"{root}/config/fastcgi_params\"; fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name; fastcgi_pass 127.0.0.1:{Settings.DbFastCgiPort}; }} }}\n}}");
        File.WriteAllText(NginxConfig,text.ToString());
    }
    public async Task StopAsync()
    {
        await StopWebServer("nginx");await StopWebServer("apache");
        foreach(var pair in processes.Where(x=>x.Key.StartsWith("php-")).ToArray()) if(!pair.Value.HasExited){pair.Value.Kill();await pair.Value.WaitForExitAsync();}
        foreach(var pair in processes.Where(x=>x.Key.StartsWith("mysql")).ToArray())
        {
            if(pair.Value.HasExited) continue;
            var client=Path.Combine(Root,"config","panel-"+pair.Key+"-client.ini");
            File.WriteAllText(client,DatabaseClientConfig(pair.Key,Settings.DatabasePassword(pair.Key),Settings.MysqlUser));
            await Run(Path.Combine(Root,"soft","mysql",Settings.DatabaseVersion(pair.Key),"bin","mysqladmin.exe"),["--defaults-file="+client,"shutdown"]);
            await pair.Value.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        processes.Clear(); SaveProcesses(); Log("Stopped");
    }
    public bool HasHosts(Site site)
    {
        return !Settings.NeedsHosts(site.Domain)||File.ReadLines(HostsFile).Any(l=>HasHostsEntry(l,site.Domain));
    }
    static string HostsFile=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"drivers","etc","hosts");
    static bool HasHostsEntry(string line,string domain)=>Regex.IsMatch(line,@"^\s*127\.0\.0\.1\s+"+Regex.Escape(domain)+@"(?:\s|$)",RegexOptions.IgnoreCase);
    // 网站地址：http+80 / https+443 时省略端口号（常用端口下就是干净的 http://demo.yikai/）。
    // 全面板拼站点地址都走这里（含局域网地址与 SSL 窗口），避免各处规则不一致。
    public static string WebUrl(string scheme,string host,int port)=>
        port==(scheme=="https"?443:80)?$"{scheme}://{host}/":$"{scheme}://{host}:{port}/";
    // 开启 HTTPS 后打开网站走 https；证书包含 127.0.0.1，未同步 hosts 时也能用。
    public string SiteUrl(Site site) => site.Https&&site.HttpsPort>0
        ?WebUrl("https",HasHosts(site)?site.Domain:"127.0.0.1",site.HttpsPort)
        :WebUrl("http",HasHosts(site)?site.Domain:"127.0.0.1",site.HttpPort);
    public string DatabaseUrl(Site site) => $"http://127.0.0.1:{Settings.DbManagerPort}/?site={Uri.EscapeDataString(site.Id)}&lang={Settings.Language}";
    // 只有项目域名与面板管理的 hosts 区块不一致时才需要请求管理员权限。
    public static bool HostsSyncRequired(Settings settings)=>HostsSyncRequired(settings,File.ReadAllText(HostsFile));
    public static bool HostsSyncRequired(Settings settings,string contents)
    {
        const string blockPattern=@"(?ms)^# BEGIN YIKAI LOCAL\r?\n(.*?)^# END YIKAI LOCAL(?:\r?\n)?";
        var blocks=Regex.Matches(contents,blockPattern);
        // .localhost 域名由浏览器直接解析到本机，不写进 hosts。
        var sites=settings.Sites.Where(site=>Settings.NeedsHosts(site.Domain)).ToList();
        if(blocks.Count==0)return sites.Any(site=>!contents.Split('\n').Any(line=>HasHostsEntry(line,site.Domain)));
        if(blocks.Count!=1)return true;
        var actual=blocks[0].Groups[1].Value.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>Regex.Replace(line.Trim(),@"\s+"," ")).Where(line=>line.Length>0).Order(StringComparer.OrdinalIgnoreCase);
        var expected=sites.Select(site=>"127.0.0.1 "+site.Domain).Order(StringComparer.OrdinalIgnoreCase);
        return !actual.SequenceEqual(expected,StringComparer.OrdinalIgnoreCase);
    }
    public static void SyncHosts(Settings settings)
    {
        if(settings.Sites.Any(s=>!Settings.ValidDomain(s.Domain))) throw new IOException("Invalid hostname");
        var file=HostsFile;
        var old=File.ReadAllText(file);
        if(!HostsSyncRequired(settings,old))return;
        var clean=Regex.Replace(old,@"(?ms)^# BEGIN YIKAI LOCAL\r?\n.*?^# END YIKAI LOCAL(?:\r?\n)?","");
        var sites=settings.Sites.Where(s=>Settings.NeedsHosts(s.Domain)).ToList();
        foreach(var site in sites)
            foreach(var line in clean.Split('\n'))
                if(Regex.IsMatch(line.Split('#')[0],@"(?:^|\s)"+Regex.Escape(site.Domain)+@"(?:\s|$)",RegexOptions.IgnoreCase) && !Regex.IsMatch(line,@"^\s*127\.0\.0\.1\s")) throw new IOException("Existing hosts entry conflicts: "+site.Domain);
        Directory.CreateDirectory(Path.Combine(settings.Root,"backups"));
        File.Copy(file,Path.Combine(settings.Root,"backups","hosts-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff")+".txt"));
        File.WriteAllText(file,clean.TrimEnd()+"\r\n\r\n# BEGIN YIKAI LOCAL\r\n"+string.Join("\r\n",sites.Select(s=>"127.0.0.1 "+s.Domain))+"\r\n# END YIKAI LOCAL\r\n",new UTF8Encoding(false));
    }
}
