using System.Text.RegularExpressions;

namespace YikaiLocal;

// 安装程序“默认网站”页：用户在安装时填网站名称和后台管理员账号。
// 安装器只写一份 config\setup-site.txt（UTF-8，每行 key=value），面板第一次加载配置时读入并删掉它：
//   · 后台账号存进 panel.json 的 cmsAdminUser / cmsAdminPassword——新建 YikaiCMS 项目、“重置管理员”都用它；
//   · 网站名称给默认站点（yikaicms.localhost）；
//   · SetupInstallPending 记下“首次启动环境后自动装好默认站点”，只尝试一次。
// 最简包不带 CMS：首次启动时联网获取最新版，再建默认站点并安装。
public sealed partial class Settings
{
    public const string SetupFileName="setup-site.txt";
    public const string DefaultCmsDomain="yikaicms.localhost";
    public bool SetupInstallPending { get; set; }
    public string SetupSiteName { get; set; } = "";
    // 与 CMS 安装向导的表单规则一致：用户名 4-20 位字母或数字；密码至少 6 位、首尾不能有空格（登录时会被 trim 掉）。
    public static bool ValidCmsAdminUser(string user)=>Regex.IsMatch(user,"^[A-Za-z0-9]{4,20}$");
    public static bool ValidCmsAdminPassword(string password)=>password.Length is >= 6 and <= 64&&password==password.Trim()&&!password.Any(char.IsControl);
    public Site? DefaultCmsSite()=>Sites.FirstOrDefault(s=>s.Template=="yikaicms"&&s.Domain.Equals(DefaultCmsDomain,StringComparison.OrdinalIgnoreCase));
    void ApplySetupFile()
    {
        var file=Path.Combine(Root,"config",SetupFileName);
        if(!File.Exists(file))return;
        try
        {
            var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var line in File.ReadAllLines(file))
            {
                var at=line.IndexOf('=');
                if(at>0)values[line[..at].Trim()]=line[(at+1)..];
            }
            // 不合规的值不采用（保留默认 admin / admin888），免得装出一个登不进去的后台
            if(values.TryGetValue("adminUser",out var user)&&ValidCmsAdminUser(user))CmsAdminUser=user;
            if(values.TryGetValue("adminPassword",out var password)&&ValidCmsAdminPassword(password))CmsAdminPassword=password;
            var name=values.TryGetValue("siteName",out var siteName)?siteName.Trim():"";
            SetupSiteName=name.Length>100?name[..100]:name;
            if(DefaultCmsSite() is { } site&&site.Title.Length==0)site.Title=SetupSiteName;
            SetupInstallPending=true;
        }
        finally{File.Delete(file);}
    }
}

public sealed partial class Runtime
{
    // 环境首次启动后调用：按安装时填写的信息装好默认 YikaiCMS 站点。
    // fetchSource 负责准备程序文件（界面里带进度占位行，命令行打印进度）；返回 null 表示没有可用的安装结果。
    public async Task<CmsInstallResult?> CompleteSetupSiteAsync(Func<Site,Task<string?>> fetchSource)
    {
        if(!Settings.SetupInstallPending)return null;
        // 先清标记：无论成败只自动尝试一次，失败时用户仍可在浏览器里走 CMS 安装向导
        Settings.SetupInstallPending=false;Settings.Save();
        var site=Settings.DefaultCmsSite();
        if(site==null)
        {
            if(Settings.Sites.Any(s=>s.Domain.Equals(Settings.DefaultCmsDomain,StringComparison.OrdinalIgnoreCase)))return null;
            var php=Settings.PhpDefault=="8.0"?"8.2":Settings.PhpDefault;
            try
            {
                var source=await fetchSource(new Site{Title=Settings.SetupSiteName,Domain=Settings.DefaultCmsDomain,Php=php});
                site=Settings.AddSite(Settings.DefaultCmsDomain,php,"mysql80","yikaicms",null,Settings.SetupSiteName,null,null,null,source);
            }
            catch(Exception error)
            {
                var text=ResetText("默认网站没有建成：","Could not create the default site: ","既定サイトを作成できませんでした：")+error.Message;
                Log(text);
                return new(false,text);
            }
        }
        Settings.SetupSiteName="";Settings.Save();
        return await InstallCmsAsync(site);
    }
}

public sealed partial class MainForm
{
    // 首次启动：环境起来后把默认网站装好，并在列表里选中它；结果写在底部状态栏（失败不弹框，浏览器里仍可手动安装）
    async Task FinishSetupSite()
    {
        if(!settings.SetupInstallPending)return;
        Runtime.CmsInstallResult? result=null;
        await Work(async()=>{
            result=await runtime.CompleteSetupSiteAsync(site=>DownloadProjectSource("yikaicms",site));
            if(settings.DefaultCmsSite() is { } site)selectedId=site.Id;
            PopulateProjects();
        });
        if(result==null)return;
        progress.Text=result.Installed
            ?T($"默认网站已装好 · 后台账号 {settings.CmsAdminUser}",$"Default site installed · admin {settings.CmsAdminUser}",$"既定サイトをインストールしました · 管理者 {settings.CmsAdminUser}")
            :T("默认网站没有自动装好，可在浏览器里打开网站完成安装：","The default site was not installed automatically; open it in the browser to finish: ","既定サイトを自動インストールできませんでした。ブラウザで開いて完了してください：")+result.Message;
    }
}
