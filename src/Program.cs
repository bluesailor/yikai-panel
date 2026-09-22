using System.Text.Json;

namespace YikaiLocal;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // 支持 "--out 文件" 与 "--out=文件" 两种写法：写了等号却静默忽略会让命令行结果与预期不符。
        string? Option(string key)
        {
            var i=Array.IndexOf(args,key);
            if(i>=0&&i+1<args.Length)return args[i+1];
            foreach(var arg in args)if(arg.StartsWith(key+"=",StringComparison.Ordinal))return arg[(key.Length+1)..];
            return null;
        }
        var root=Option("--root") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..",".."));
        try
        {
            var settings=Settings.Load(root);
            if(args.Contains("--sync-hosts")){Runtime.SyncHosts(settings);return 0;}
            var runtime=new Runtime(settings);runtime.Adopt();
            if(args.Contains("--start")){using var operation=Runtime.Lock(settings);runtime.Adopt();runtime.StartAsync().GetAwaiter().GetResult();return 0;}
            // --start-site <域名或 id>：只把某个项目设为启用并启动环境（等同界面里点这个项目的启动）。
            // 扫描进来的项目都是停止状态，脚本化启动单个项目用它。
            if(Option("--start-site") is { } startSite)
            {
                using var operation=Runtime.Lock(settings);runtime.Adopt();
                var site=settings.Sites.FirstOrDefault(s=>s.Domain.Equals(startSite,StringComparison.OrdinalIgnoreCase))??settings.Sites.FirstOrDefault(s=>s.Id==startSite)
                    ??throw new ArgumentException("Unknown project: "+startSite);
                runtime.StartSiteAsync(site).GetAwaiter().GetResult();
                Console.WriteLine($"started {site.Domain} port={site.HttpPort} fastcgi={site.FastCgiPort}");
                return 0;
            }
            if(args.Contains("--reload")){using var operation=Runtime.Lock(settings);runtime.Adopt();runtime.ReloadWebServer().GetAwaiter().GetResult();return 0;}
            if(Option("--web-server") is { } webServer){using var operation=Runtime.Lock(settings);runtime.Adopt();runtime.SwitchWebServerAsync(webServer).GetAwaiter().GetResult();return 0;}
            if((Option("--config-check")??Option("--config-save")) is { } configKey)
            {
                var input=Option("--input")??throw new ArgumentException("--input <file> is required.");
                using var operation=Runtime.Lock(settings);runtime.Adopt();var file=runtime.ConfigFileByKey(configKey);var text=File.ReadAllText(input);
                if(args.Contains("--config-check"))runtime.CheckConfigAsync(file,text).GetAwaiter().GetResult();else runtime.SaveConfigAsync(file,text).GetAwaiter().GetResult();
                return 0;
            }
            // HTTPS：--https <项目ID> on|off [--source auto|custom] [--cert 文件 --key 文件] [--renew]
            if(Option("--https") is { } httpsSite)
            {
                using var operation=Runtime.Lock(settings);runtime.Adopt();
                var site=settings.Sites.FirstOrDefault(s=>s.Id==httpsSite)??throw new ArgumentException("Unknown project: "+httpsSite);
                var mode=args[Array.IndexOf(args,"--https")+2];
                runtime.ConfigureHttpsAsync(site,mode=="on",Option("--source")??(Option("--cert")!=null?"custom":site.CertificateSource),Option("--cert"),Option("--key"),args.Contains("--renew")).GetAwaiter().GetResult();
                return 0;
            }
            if(Option("--lan-access") is { } lan)
            {
                using var operation=Runtime.Lock(settings);runtime.Adopt();
                runtime.SetLanAccessAsync(lan=="on").GetAwaiter().GetResult();
                return 0;
            }
            if(Option("--firewall") is { } firewall){runtime.ConfigureFirewall(firewall=="on");return 0;}
            // PHP 扩展：--php-extensions <版本> [--site <项目ID>] [--enable a,b] [--disable c,d]
            if(Option("--php-extensions") is { } phpVersion)
            {
                using var operation=Runtime.Lock(settings);runtime.Adopt();
                var target=Option("--site") is { } siteId?settings.Sites.FirstOrDefault(s=>s.Id==siteId)??throw new ArgumentException("Unknown project: "+siteId):null;
                if(target!=null&&target.Php!=phpVersion)throw new ArgumentException($"{target.Domain} uses PHP {target.Php}.");
                var available=runtime.PhpExtensionList(phpVersion,target);
                if(available.Count==0)throw new ArgumentException("No extensions found for PHP "+phpVersion);
                string[] Names(string key)=>(Option(key)??"").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                var enable=Names("--enable");var disable=Names("--disable");
                foreach(var name in enable.Concat(disable))
                    if(!available.Any(e=>e.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Unknown extension: "+name);
                foreach(var name in disable)
                    if(Runtime.RequiredExtensions.Contains(name,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("This extension is required and cannot be disabled: "+name);
                bool Wanted(PhpExtensionInfo e)=>enable.Contains(e.Name,StringComparer.OrdinalIgnoreCase)||(e.Enabled&&!disable.Contains(e.Name,StringComparer.OrdinalIgnoreCase));
                if(target!=null)
                {
                    if(args.Contains("--follow-version"))runtime.SaveSiteExtensionsAsync(target,runtime.PhpExtensionList(phpVersion).Where(e=>e.Enabled).Select(e=>e.Name).ToList()).GetAwaiter().GetResult();
                    else runtime.SaveSiteExtensionsAsync(target,available.Where(Wanted).Select(e=>e.Name).ToList()).GetAwaiter().GetResult();
                    return 0;
                }
                var extensionFile=runtime.ConfigFileByKey("php"+phpVersion);
                runtime.SaveConfigAsync(extensionFile,Runtime.ApplyPhpExtensions(runtime.ReadConfig(extensionFile),available.Select(e=>(e.Name,e.Zend,Wanted(e))))).GetAwaiter().GetResult();
                return 0;
            }
            if(Option("--service") is { } service)
            {
                if(!Runtime.Services.Contains(service))throw new ArgumentException("--service: "+string.Join(" | ",Runtime.Services));
                using var operation=Runtime.Lock(settings);runtime.Adopt();
                (Option("--action") switch{"start"=>runtime.StartServiceAsync(service),"stop"=>runtime.StopServiceAsync(service),"restart"=>runtime.RestartServiceAsync(service),_=>throw new ArgumentException("--action: start | stop | restart")}).GetAwaiter().GetResult();
                return 0;
            }
            if(args.Contains("--stop")){using var operation=Runtime.Lock(settings);runtime.Adopt();runtime.StopAsync().GetAwaiter().GetResult();return 0;}
            // 扫描目录添加项目：--scan-sites [--directory <目录>] [--php <版本>] [--include-plain]
            // --include-plain：名字不带点的目录也添加（自动补 .yikai，仅限英文、数字、- 和 _）
            if(args.Contains("--scan-sites"))
            {
                var folder=Option("--directory")??Path.Combine(settings.Root,"wwwroot");
                var outcome=settings.AddSitesFromFolder(folder,Option("--php")??settings.PhpDefault,args.Contains("--include-plain"));
                // folder= 是源目录名：不带点的目录名和域名不一样（my_shop-2 → my-shop-2.yikai），要能对上
                var addedLine=(Site site)=>$"added {site.Domain} folder={Path.GetFileName(site.Directory)} template={site.Template} db={site.Database}/{site.DatabaseName} php={site.Php} port={site.HttpPort}";
                foreach(var site in outcome.Added)Console.WriteLine(addedLine(site));
                // 跳过原因带一个固定英文标识（plain-name / name-conflict / not-a-domain …），验收脚本按它判断，
                // 不受控制台编码影响；--out 另写一份 UTF-8 报告，方便看中文原文。
                foreach(var skipped in outcome.Skipped)Console.WriteLine($"skipped {skipped.Name} {skipped.Code}");
                Console.WriteLine($"total added={outcome.Added.Count} skipped={outcome.Skipped.Count}");
                if(Option("--out") is { } scanReport)
                {
                    var lines=new List<string>();
                    foreach(var site in outcome.Added)lines.Add(addedLine(site));
                    foreach(var skipped in outcome.Skipped)lines.Add($"skipped {skipped.Name} {skipped.Code} {skipped.Reason}");
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(scanReport))!);
                    File.WriteAllText(scanReport,string.Join("\n",lines)+"\n",new System.Text.UTF8Encoding(true));
                }
                return 0;
            }
            // 端口排查：只读输出，不启动任何服务。重定向或 --out <文件> 可拿到完整文本。
            if(args.Contains("--ports")){var report=runtime.PortReport();Console.WriteLine(report);if(Option("--out") is { } target){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);File.WriteAllText(target,report,new System.Text.UTF8Encoding(true));}return 0;}
            if(Option("--create-site") is { } siteName){
                // --http-port：指定项目网站端口（常用端口 80 等）；填了就会固定，不再自动避让。
                // 先校验端口再创建项目：校验失败时不留下半个项目。
                int? requestedPort=null;
                if(Option("--http-port") is { } httpPortText){
                    if(!int.TryParse(httpPortText,out var httpPort))throw new ArgumentException("--http-port: 1-65535");
                    var problem=settings.PortProblem(httpPort);if(problem!=null)throw new IOException("--http-port: "+problem);
                    if(PortDiagnostics.ListenerPid(httpPort) is { } holder)
                        throw new IOException($"Port {httpPort} is in use by {PortDiagnostics.Describe(holder).Replace("\n"," ")}.");
                    requestedPort=httpPort;
                }
                var template=Option("--template")??"yikaicms";var report=new Progress<string>(Console.WriteLine);
                var source=template=="yikaicms"?ProjectSources.YikaiCmsAsync(settings,report,CancellationToken.None).GetAwaiter().GetResult():template=="wordpress"?ProjectSources.WordPressAsync(settings,report,CancellationToken.None).GetAwaiter().GetResult():null;
                var added=settings.AddSite(siteName,Option("--php")??settings.PhpDefault,Option("--database")??"mysql80",template,Option("--directory"),Option("--title")??"",Option("--db-name"),Option("--db-user"),Option("--db-password"),source);
                if(requestedPort is { } port){added.HttpPort=port;added.PortPinned=true;settings.Save();}
                if(settings.AutoInstallCms&&template=="yikaicms"&&!args.Contains("--no-auto-install"))
                {
                    using var operation=Runtime.Lock(settings);runtime.Adopt();
                    var installed=runtime.InstallCmsAsync(added).GetAwaiter().GetResult();
                    Console.WriteLine($"install={(installed.Installed?"ok":"failed")} message={installed.Message}");
                }
                return 0;
            }
            // 数据库端口：--db-port <mysql80|mysql57> <端口>（改成常用端口 3306 等）。
            if(Option("--db-port") is { } dbPortKind){
                var index=Array.IndexOf(args,"--db-port");var portText=index+2<args.Length?args[index+2]:throw new ArgumentException("--db-port <mysql80|mysql57> <port>");
                if(dbPortKind is not ("mysql80" or "mysql57"))throw new ArgumentException("--db-port: mysql80 | mysql57");
                if(!int.TryParse(portText,out var dbPort))throw new ArgumentException("--db-port: 1-65535");
                using var operation=Runtime.Lock(settings);runtime.Adopt();
                var result=runtime.ChangeDatabasePortAsync(dbPortKind,dbPort).GetAwaiter().GetResult();
                Console.WriteLine($"{dbPortKind} port={result.Port} changed={result.Changed} updated={string.Join(",",result.Updated)} manual={string.Join(",",result.Manual)}");
                return 0;
            }
            ApplicationConfiguration.Initialize();
            if(Option("--language") is { } lang) settings.Language=lang;
            if(Option("--render") is { } image)
            {
                using var form=new MainForm(settings,runtime,true);
                form.Show();Application.DoEvents();
                using var bitmap=new Bitmap(form.Width,form.Height);
                form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
                bitmap.Save(image,System.Drawing.Imaging.ImageFormat.Png);
                form.Dispose();return 0;
            }
            // 单实例：同一根目录只允许一个面板。第二次启动不再弹提示框，而是让已在运行的实例把窗口调到前台，自己静默退出。
            var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root)))[..16];
            using var mutex=new Mutex(true,"YikaiLocal-"+key,out var first);
            if(!first)
            {
                try{using var signal=EventWaitHandle.OpenExisting("YikaiLocal-Show-"+key);signal.Set();}
                catch(WaitHandleCannotBeOpenedException){/* 运行中的是旧版本（没有这个事件）：静默退出即可 */ }
                return 0;
            }
            using var showSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"YikaiLocal-Show-"+key);
            var main=new MainForm(settings,runtime,startEnvironment:settings.AutoStart&&!args.Contains("--no-autostart"));
            main.AcceptShowSignal(showSignal);
            Application.Run(main);return 0;
        }
        catch(Exception error)
        {
            Directory.CreateDirectory(Path.Combine(root,"logs"));
            File.WriteAllText(Path.Combine(root,"logs","panel-last-error.txt"),error.ToString());
            if(!args.Any(a=>a.StartsWith("--")))MessageBox.Show(error.Message,"Yikai Panel",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}
