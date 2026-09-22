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
        var ini=File.Exists(PhpIniPath(version))?File.ReadAllText(PhpIniPath(version)):"";
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

    // 项目自己的 php.ini：该 PHP 版本的配置 + 本项目的扩展增删 + 安装向导预填脚本。启动项目 PHP 时生成。
    public string SitePhpIni(Site site)
    {
        var text=File.ReadAllText(PhpIniPath(site.Php));
        var changes=SiteExtensionChanges(site);
        if(changes.Count>0)text=ApplyPhpExtensions(text,changes);
        var path=Path.Combine(Root,"config","panel-php-"+site.Id+".ini");
        File.WriteAllText(path,text+"\nauto_prepend_file=\""+Slash(Path.Combine(Root,"soft","db-manager","install-prefill.php"))+"\"\n",Utf8NoBom);
        return path;
    }

    // 保存某个项目的扩展选择：先用该 PHP 版本检查生成的配置，再写入并只重启这个项目的 PHP。调用方持有 Runtime.Lock。
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
        site.ExtensionsOn=on.Count>0?on:null;site.ExtensionsOff=off.Count>0?off:null;
        try
        {
            Settings.Save();
            if(!Alive("php-"+site.Id))return ResetText($"已保存，{site.Domain} 下次启动时生效。",$"Saved. {site.Domain} uses it on next start.",$"保存しました。{site.Domain} の次回起動時に反映します。");
            await RestartSitePhpAsync(site);
            return ResetText($"已保存，{site.Domain} 的 PHP 已重启。",$"Saved and PHP for {site.Domain} restarted.",$"保存し、{site.Domain} の PHP を再起動しました。");
        }
        catch
        {
            (site.ExtensionsOn,site.ExtensionsOff)=previous;Settings.Save();throw;
        }
    }
    // 只重启这一个项目的 PHP，不动其他项目和共享服务。
    public async Task RestartSitePhpAsync(Site site)
    {
        var key="php-"+site.Id;
        if(Alive(key)){processes[key].Kill();await processes[key].WaitForExitAsync();}
        processes.Remove(key);SaveProcesses();
        await StartSitePhp(site);
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
