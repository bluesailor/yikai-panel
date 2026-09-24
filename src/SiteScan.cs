using System.Text.RegularExpressions;

namespace YikaiLocal;

// 扫描一个目录（默认 wwwroot），把子目录批量登记成项目。
// 用途：手工放进 wwwroot、从别处复制过来的站点目录，不用一个个“接入已有目录”。
//
// 目录名的规则：
//   · 带点的目录名本身就是域名（demo.yikai），可以直接用；
//   · 不带点的目录名可以额外补 .yikai 当域名（yikaiflow → yikaiflow.yikai），名字只能含
//     英文字母、数字、- 和 _，且以字母或数字开头；中文、空格等一律跳过；
//   · 隐藏目录（.git 等）跳过。
// 域名要合法，所以目录名会做两步换算：统一转小写；下划线换成连字符（域名里不能有 _）。
// 换算结果会显示在对话框的“域名”列里，例如 my_shop-2 → my-shop-2.localhost。
// 同名冲突：带点的目录优先占用域名。例如同时存在 yikaiflow 和 yikaiflow.yikai 时，
// yikaiflow.yikai 得到该域名，yikaiflow 会被跳过并说明原因。
//
// 识别：有 config/version.php 视为 YikaiCMS（登记后使用 CMS 的伪静态规则，但**不复制任何文件**）；
//       有 storage/database.sqlite 视为 SQLite；数据库名尽量从项目自己的配置里读出来。
// 登记结果不会动已安装站点的文件：只写 panel.json 里的一条项目记录。
public sealed partial class Settings
{
    public sealed record ScanCandidate(string Directory,string Name,string Domain,string Template,string Database,string DatabaseName);
    // Code 是给命令行和验收脚本用的固定英文标识（不随语言变），Reason 是给人看的原因
    public sealed record ScanSkip(string Name,string Code,string Reason);
    public sealed record ScanOutcome(List<Site> Added,List<ScanSkip> Skipped);

    // 不带点的目录名：以字母或数字开头结尾，中间可以有 - 和 _，总长不超过一个域名标段（62 字符）
    static readonly Regex PlainNamePattern=new("^[A-Za-z0-9](?:[A-Za-z0-9_-]{0,60}[A-Za-z0-9])?$",RegexOptions.Compiled);

    // 列出候选与跳过原因，供对话框预览（不写配置）
    public (List<ScanCandidate> Candidates,List<ScanSkip> Skipped) ScanFolder(string folder,bool includePlainNames)
    {
        if(!Directory.Exists(folder))throw new IOException("Folder not found: "+folder);
        var candidates=new List<ScanCandidate>();var skipped=new List<ScanSkip>();
        // 带点的先处理：域名归属优先给真正的域名目录
        var directories=Directory.EnumerateDirectories(folder)
            .OrderByDescending(x=>Path.GetFileName(x).Contains('.'))
            .ThenBy(x=>x,StringComparer.OrdinalIgnoreCase);
        foreach(var directory in directories)
        {
            var name=Path.GetFileName(directory);
            void Skip(string code,string reason)=>skipped.Add(new ScanSkip(name,code,name+"（"+reason+"）"));
            if(name.StartsWith('.')){Skip("hidden",Tr("隐藏目录，跳过","hidden folder, skipped","隠しフォルダーのため省略"));continue;}
            string domain;
            if(name.Contains('.'))
            {
                domain=name.ToLowerInvariant();
                if(!ValidDomain(domain)){Skip("not-a-domain",Tr("不是可用域名：只能用英文字母、数字、- 和 .","not a usable domain: use letters, digits, - and . only","ドメインとして不可：英数字・-・. のみ"));continue;}
            }
            else
            {
                if(!includePlainNames){Skip("plain-name",Tr("名字不带点（勾选“同时添加不带点的目录”可自动补 .localhost）","no dot in the name (enable “also add folders without a dot” to append .localhost)","ドットなし（オプションで .localhost を補えます）"));continue;}
                if(!PlainNamePattern.IsMatch(name)){Skip("plain-name-invalid",Tr("名字只能用英文字母、数字、- 和 _，且以字母或数字开头结尾（不能是中文）","use letters, digits, - and _ only, starting and ending with a letter or digit","英数字・-・_ のみ、先頭と末尾は英数字（日本語不可）"));continue;}
                domain=name.ToLowerInvariant().Replace('_','-')+Settings.DefaultSuffix;
                if(!ValidDomain(domain)){Skip("plain-name-invalid",Tr("换算出来的域名不合法（名字太长？）","the resulting domain is not usable (name too long?)","生成したドメインが不正（名前が長すぎますか）"));continue;}
            }
            var full=Path.GetFullPath(directory);
            if(Sites.Any(s=>s.Domain.Equals(domain,StringComparison.OrdinalIgnoreCase)))
            {Skip("domain-taken",Tr($"域名 {domain} 已被已登记的项目占用",$"domain {domain} is already used by a registered project",$"ドメイン {domain} は登録済み"));continue;}
            if(Sites.Any(s=>Path.GetFullPath(s.Directory).TrimEnd('\\').Equals(full.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)))
            {Skip("folder-registered",Tr("这个目录已经登记过","this folder is already registered","このフォルダーは登録済み"));continue;}
            if(!name.Contains('.')&&Directory.Exists(Path.Combine(folder,domain)))
            {Skip("name-conflict",Tr($"同名的 {domain} 目录已存在，域名归它",$"{domain} exists next to it and keeps the domain",$"同名の {domain} が存在するため省略"));continue;}
            if(candidates.Any(c=>c.Domain.Equals(domain,StringComparison.OrdinalIgnoreCase)))
            {Skip("duplicate-in-scan",Tr($"域名 {domain} 已分配给本次扫描中的另一个目录",$"domain {domain} already taken in this scan",$"ドメイン {domain} は今回のスキャンで使用済み"));continue;}
            var cms=File.Exists(Path.Combine(directory,"config","version.php"));
            var sqlite=File.Exists(Path.Combine(directory,"storage","database.sqlite"));
            candidates.Add(new ScanCandidate(directory,name,domain,cms?"yikaicms":"php",sqlite?"sqlite":"mysql80",DetectDatabaseName(directory)??""));
        }
        return (candidates,skipped);
    }

    // YikaiCMS 从 8.2 起才支持：批量登记时选的 8.0 自动改用 8.2，别让整批里的 CMS 目录全部失败
    public static string ScanPhpFor(string template,string php)=>template=="yikaicms"&&php=="8.0"?"8.2":php;

    // 按预览结果登记（对话框传回勾选的候选，命令行直接全选）
    public ScanOutcome RegisterCandidates(IEnumerable<ScanCandidate> candidates,string php)
    {
        var added=new List<Site>();var skipped=new List<ScanSkip>();
        foreach(var candidate in candidates)
        {
            try
            {
                var site=AddSite(candidate.Domain,ScanPhpFor(candidate.Template,php),candidate.Database,candidate.Template,candidate.Directory,TitleFor(candidate.Domain),
                    candidate.DatabaseName.Length>0?candidate.DatabaseName:null,null,null,null,true);
                added.Add(site);
            }
            catch(Exception error) when(error is InvalidOperationException or IOException)
            {skipped.Add(new ScanSkip(candidate.Name,"register-failed",candidate.Name+"（"+error.Message+"）"));}
        }
        return new(added,skipped);
    }

    // 一步到位：扫描并登记（命令行与测试用）
    public ScanOutcome AddSitesFromFolder(string folder,string php,bool includePlainNames=false)
    {
        var (candidates,skipped)=ScanFolder(folder,includePlainNames);
        var outcome=RegisterCandidates(candidates,php);
        return new(outcome.Added,[..skipped,..outcome.Skipped]);
    }

    string Tr(string zh,string en,string ja)=>Language=="en"?en:Language=="ja"?ja:zh;

    // 项目标题：域名第一段（www.example.com → www），与面板新建项目的习惯一致
    static string TitleFor(string domain)
    {
        var dot=domain.IndexOf('.');
        return dot>0?domain[..dot]:domain;
    }

    // 尽量从项目自己的配置里读出数据库名（YikaiCMS 的 config/config.php、WordPress 的 wp-config.php、.env）
    static string? DetectDatabaseName(string directory)
    {
        foreach(var relative in new[]{"config\\config.php","wp-config.php",".env","config\\database.php"})
        {
            var file=Path.Combine(directory,relative);
            if(!File.Exists(file))continue;
            try
            {
                var text=File.ReadAllText(file);
                var match=Regex.Match(text,@"DB_NAME['""\s]*[=,]\s*['""]([A-Za-z0-9_]{1,64})['""]");
                if(match.Success)return match.Groups[1].Value;
            }
            catch(IOException){/* 读不到就按域名生成 */}
        }
        return null;
    }
}
