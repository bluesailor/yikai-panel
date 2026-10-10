using System.Text.RegularExpressions;

namespace YikaiLocal;

// PHP 扩展：列出某个版本 ext 目录里的 DLL，并与 php.ini 里的 extension / zend_extension 行对应。
// 勾选后的保存复用配置文件的流程：先用 php.exe 检查（加载失败会报错），备份、写入、重启该版本 PHP，失败自动回滚。
// Custom：该项目相对 PHP 版本设置做了改动（额外启用或单独关闭）。
public sealed record PhpExtensionInfo(string Name,string FileName,bool Zend,bool Enabled,bool Required,bool Custom=false);

public sealed partial class Runtime
{
    // 这些必须用 zend_extension 载入。
    static readonly string[] ZendNames=["opcache","xdebug","ixed"];
    // 面板不允许关掉的扩展：数据库页面、安装向导和 YikaiCMS 需要它们。
    public static readonly string[] RequiredExtensions=["mbstring","fileinfo","curl","openssl","gd","pdo_mysql","pdo_sqlite","sqlite3","zip","mysqli"];
    // PHP 官方自带的测试扩展，不列给用户。
    static readonly string[] HiddenExtensions=["dl_test","zend_test"];

    public IReadOnlyList<string> InstalledPhp()=>PhpVersions.Where(PhpInstalled).ToList();
    public string PhpIniPath(string version)=>Path.Combine(Root,"soft","php",version,"php.ini");
    static string ExtensionNameOf(string fileName)=>fileName.StartsWith("php_",StringComparison.OrdinalIgnoreCase)?Path.GetFileNameWithoutExtension(fileName)[4..]:Path.GetFileNameWithoutExtension(fileName);
    static bool IsZend(string name)=>ZendNames.Any(z=>name.StartsWith(z,StringComparison.OrdinalIgnoreCase));
    // 匹配 php.ini 中引用某个扩展的行（含被注释掉的）：extension=curl / ;extension="php_curl.dll" / zend_extension=opcache
    static Regex ExtensionLine(string name)=>new(@"(?im)^[ \t]*;*[ \t]*(zend_)?extension[ \t]*=[ \t]*""?(php_)?"+Regex.Escape(name)+@"(\.dll)?""?[ \t]*(;.*)?$");

    // site 为空时返回 PHP 版本本身的设置；给了 site 则叠加该项目的增删。
    public IReadOnlyList<PhpExtensionInfo> PhpExtensionList(string version,Site? site=null)
    {
        var list=new List<PhpExtensionInfo>();
        var folder=Path.Combine(Root,"soft","php",version,"ext");
        if(!Directory.Exists(folder))return list;
        // 统一换行再匹配：随包 php.ini 是 CRLF，而正则的 $ 只认 \n 前的位置，不统一会把所有扩展都读成“未启用”
        var ini=File.Exists(PhpIniPath(version))?File.ReadAllText(PhpIniPath(version)).ReplaceLineEndings("\n"):"";
        foreach(var file in Directory.EnumerateFiles(folder,"php_*.dll").Select(Path.GetFileName).OrderBy(f=>f,StringComparer.OrdinalIgnoreCase))
        {
            var name=ExtensionNameOf(file!);
            if(HiddenExtensions.Contains(name,StringComparer.OrdinalIgnoreCase))continue;
            var match=ExtensionLine(name).Match(ini);
            var enabled=match.Success&&!match.Value.TrimStart().StartsWith(';');
            var custom=site!=null&&(Has(site.ExtensionsOn,name)||Has(site.ExtensionsOff,name));
            if(site!=null)enabled=Has(site.ExtensionsOn,name)||(enabled&&!Has(site.ExtensionsOff,name));
            list.Add(new(name,file!,IsZend(name),enabled,RequiredExtensions.Contains(name,StringComparer.OrdinalIgnoreCase),custom));
        }
        return list;
    }
    static bool Has(List<string>? names,string name)=>names?.Contains(name,StringComparer.OrdinalIgnoreCase)==true;
    static List<(string Name,bool Zend,bool Enable)> SiteExtensionChanges(Site site)=>
        (site.ExtensionsOn??[]).Select(n=>(n,IsZend(n),true)).Concat((site.ExtensionsOff??[]).Select(n=>(n,IsZend(n),false))).ToList();
    public bool SiteHasOwnExtensions(Site site)=>SiteExtensionChanges(site).Count>0;

    // 保存某个项目的扩展选择：先用该 PHP 版本检查生成的配置，再写入。
    // 项目会换到“版本 + 扩展增删”对应的进程池（与扩展设置相同的项目共用），不重启其它项目在用的进程。调用方持有 Runtime.Lock。
    public async Task<string> SaveSiteExtensionsAsync(Site site,IReadOnlyCollection<string> enabled)
    {
        var defaults=PhpExtensionList(site.Php);
        var on=defaults.Where(e=>!e.Enabled&&enabled.Contains(e.Name,StringComparer.OrdinalIgnoreCase)).Select(e=>e.Name).ToList();
        var off=defaults.Where(e=>e.Enabled&&!enabled.Contains(e.Name,StringComparer.OrdinalIgnoreCase)).Select(e=>e.Name).ToList();
        if(off.Any(name=>RequiredExtensions.Contains(name,StringComparer.OrdinalIgnoreCase)))
            throw new IOException(ResetText("这些扩展是面板和 YikaiCMS 需要的，不能关闭。","These extensions are required by the panel and YikaiCMS.","これらの拡張は無効にできません。"));
        var previous=(site.ExtensionsOn,site.ExtensionsOff);
        var changes=on.Select(n=>(n,IsZend(n),true)).Concat(off.Select(n=>(n,IsZend(n),false))).ToList();
        var candidate=changes.Count==0?File.ReadAllText(PhpIniPath(site.Php)):ApplyPhpExtensions(File.ReadAllText(PhpIniPath(site.Php)),changes);
        await CheckConfigAsync(new ConfigFile("php"+site.Php,"",PhpIniPath(site.Php),"php",site.Php),candidate);
        var wasRunning=site.Enabled&&PhpRunning(site);
        site.ExtensionsOn=on.Count>0?on:null;site.ExtensionsOff=off.Count>0?off:null;
        try
        {
            Settings.Save();
            if(!wasRunning)return ResetText($"已保存，{site.Domain} 下次启动时生效。",$"Saved. {site.Domain} uses it on next start.",$"保存しました。{site.Domain} の次回起動時に反映します。");
            await SyncPhpPoolsAsync();
            return ResetText($"已保存，{site.Domain} 已按新的扩展设置运行。",$"Saved. {site.Domain} now runs with the new extensions.",$"保存しました。{site.Domain} は新しい拡張設定で動作しています。");
        }
        catch
        {
            (site.ExtensionsOn,site.ExtensionsOff)=previous;Settings.Save();
            if(wasRunning)try{await SyncPhpPoolsAsync();}catch(IOException){/* 恢复失败时保留原错误给用户 */}
            throw;
        }
    }
    // 让运行中的进程池与项目设置一致：拉起新需要的进程池、重载 Web 服务器、停掉没人用的进程池。
    async Task SyncPhpPoolsAsync()
    {
        PreparePorts();
        var failures=await StartPools(NeededPools());
        if(WebAlive)await ReloadWebServer();
        await StopUnusedPhp();
        if(failures.Count>0)throw new IOException(string.Join("\n\n",failures));
    }
    // 重启这个项目所在的 PHP 进程池（同组的其它项目也会一起重启，通常只需一两秒）。
    public async Task RestartSitePhpAsync(Site site)
    {
        await StopPool(PoolOf(site).Id);
        await SyncPhpPoolsAsync();
        Log("PHP restarted · "+site.Domain);
    }

    // 生成新的 php.ini 内容：已有的行就地启用或注释，重复行一律注释，缺少的行追加到面板管理的小节里。
    public static string ApplyPhpExtensions(string ini,IEnumerable<(string Name,bool Zend,bool Enable)> wanted)
    {
        const string header="; 易开面板 · 扩展";
        var text=ini.ReplaceLineEndings("\n");
        foreach(var (name,zend,enable) in wanted)
        {
            var directive=(zend?"zend_extension":"extension")+"="+name;
            var matches=ExtensionLine(name).Matches(text).ToList();
            if(matches.Count>0)
            {
                for(var i=matches.Count-1;i>=0;i--)
                {
                    var replacement=i==0&&enable?directive:";"+directive;
                    text=text[..matches[i].Index]+replacement+text[(matches[i].Index+matches[i].Length)..];
                }
                continue;
            }
            if(!enable)continue;
            if(!text.Contains(header))text=text.TrimEnd('\n')+"\n\n"+header+"\n";
            text=text.TrimEnd('\n')+"\n"+directive+"\n";
        }
        return text;
    }
}
