using System.Net.Http;
using System.Text.Json;

namespace YikaiLocal;

// 一键部署 YikaiCMS：项目创建后自动调用 CMS 自带的安装接口（POST /install/index.php, action=install），
// 数据库连接用面板为该项目准备好的信息，后台账号默认 admin / yikai888。
// 失败不阻塞项目创建：把 CMS 返回的原因写进日志，用户仍可在浏览器里完成安装。
public sealed partial class Runtime
{
    public sealed record CmsInstallResult(bool Installed,string Message);

    public async Task<CmsInstallResult> InstallCmsAsync(Site site,CancellationToken token=default)
    {
        if(site.Template!="yikaicms")return new(false,ResetText("不是 YikaiCMS 项目","Not a YikaiCMS project","YikaiCMS プロジェクトではありません"));
        if(File.Exists(Path.Combine(site.Directory,"installed.lock")))return new(true,ResetText("已安装过，跳过","Already installed","インストール済み"));
        // 安装要打 HTTP 接口，先确保这个项目在运行（命令行建项目时环境可能还没启动），
        // 再确认端口已经监听——否则会以“目标计算机积极拒绝”失败。
        if(!IsRunning(site))await StartSiteAsync(site);
        var ready=false;
        for(var i=0;i<60&&!ready;i++)
        {
            ready=PortDiagnostics.ListenerPid(site.HttpPort)!=null;
            if(!ready)await Task.Delay(500);
        }
        if(!ready)return new(false,ResetText($"项目端口 {site.HttpPort} 未监听，跳过自动安装。",$"Port {site.HttpPort} is not listening; skipping the automatic install.",$"ポート {site.HttpPort} が待ち受けていないため自動インストールを省略します。"));
        // 面板自己走 127.0.0.1 请求，避免依赖 hosts 是否同步
        var baseUrl=$"http://127.0.0.1:{site.HttpPort}";
        var publicUrl=HasHosts(site)?$"http://{site.Domain}/":baseUrl+"/";
        var sqlite=site.Database=="sqlite";
        var fields=new Dictionary<string,string>
        {
            ["action"]="install",
            ["db_driver"]=sqlite?"sqlite":"mysql",
            ["db_host"]="127.0.0.1",
            ["db_port"]=sqlite?"":Settings.DatabasePort(site.Database).ToString(),
            ["db_name"]=site.DatabaseName,
            ["db_user"]=sqlite?"":(site.DatabaseUser??Settings.MysqlUser),
            ["db_pass"]=sqlite?"":(site.DatabaseUser!=null?site.DatabasePassword??"":Settings.DatabasePassword(site.Database)),
            ["db_prefix"]="yikai_",
            ["db_create"]="1",
            ["admin_user"]=Settings.CmsAdminUser,
            ["admin_pass"]=Settings.CmsAdminPassword,
            ["admin_email"]="",
            ["admin_lang"]=InstallerLanguage(),
            ["site_name"]=string.IsNullOrWhiteSpace(site.Title)?site.Domain:site.Title,
            ["site_url"]=publicUrl,
            ["site_lang"]=InstallerLanguage(),
            ["install_demo"]="0",
        };
        try
        {
            using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(5)};
            client.DefaultRequestHeaders.UserAgent.ParseAdd("YikaiPanel/"+PanelUpdate.CurrentVersion);
            Log(ResetText("正在自动完成 CMS 安装…","Installing the CMS automatically…","CMS を自動インストールしています…"));
            using var response=await client.PostAsync(baseUrl+"/install/index.php",new FormUrlEncodedContent(fields),token);
            var body=await response.Content.ReadAsStringAsync(token);
            var (success,message)=ParseInstallResponse(body);
            if(!success)
            {
                Log(ResetText("自动安装未完成：","Automatic install did not complete: ","自動インストール未完了：")+message);
                return new(false,message);
            }
            // 以文件标记为准（CMS 成功时会写 config/config.php 与 installed.lock）
            if(!File.Exists(Path.Combine(site.Directory,"installed.lock"))||!File.Exists(Path.Combine(site.Directory,"config","config.php")))
            {
                var fallback=ResetText("安装接口返回成功，但未找到 installed.lock，请刷新后台确认。","The installer reported success but installed.lock is missing; check the site.","成功と返りましたが installed.lock がありません。サイトを確認してください。");
                Log("CMS · "+fallback);
                return new(false,fallback);
            }
            Log(ResetText($"CMS 已安装 · 后台 {Settings.CmsAdminUser} / {Settings.CmsAdminPassword}",$"CMS installed · admin {Settings.CmsAdminUser} / {Settings.CmsAdminPassword}",$"CMS インストール完了 · 管理 {Settings.CmsAdminUser} / {Settings.CmsAdminPassword}"));
            return new(true,message);
        }
        catch(Exception error) when(error is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            var text=ResetText("自动安装失败：","Automatic install failed: ","自動インストールに失敗：")+error.Message;
            Log(text);
            return new(false,text);
        }
    }
    string InstallerLanguage()=>Settings.Language switch{"en"=>"en","ja"=>"ja",_=>"zh"};
    static (bool Success,string Message) ParseInstallResponse(string body)
    {
        try
        {
            using var doc=JsonDocument.Parse(body);
            if(doc.RootElement.ValueKind==JsonValueKind.Object&&doc.RootElement.TryGetProperty("success",out var ok))
                return (ok.ValueKind==JsonValueKind.True,doc.RootElement.TryGetProperty("message",out var m)?m.GetString()??"":"");
        }
        catch(JsonException){/* 非 JSON：交给下面的兜底 */ }
        // 兼容非 JSON 响应：包含“已安装/成功”等字样时按成功处理（由调用方再用 installed.lock 复核）
        var text=body.Length>400?body[..400]:body;
        return (text.Contains("installed.lock")||text.Contains("success")||text.Contains("安装完成"),text.Replace('\n',' ').Trim());
    }
}
