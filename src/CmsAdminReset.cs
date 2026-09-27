using System.Text.Json;

namespace YikaiLocal;

// 重置 YikaiCMS 后台管理员：把 panel.json 里的 cmsAdminUser 恢复成 cmsAdminPassword（默认 admin / admin888）。
// 实际改库的是随包的 soft\db-manager\cms-admin-reset.php：它读项目 config/config.php 的数据库配置，
// 该用户存在就重设密码、启用账号、设为超级管理员并清掉两步验证，不存在就新建；再清空登录锁定记录。
// 账号密码经标准输入传给 PHP，不进命令行。
public sealed partial class Runtime
{
    public async Task<string> ResetCmsAdminAsync(Site site)
    {
        if(site.Template!="yikaicms")throw new IOException(ResetText("这不是 YikaiCMS 项目。","This is not a YikaiCMS project.","YikaiCMS プロジェクトではありません。"));
        if(!File.Exists(Path.Combine(site.Directory,"config","config.php")))
            throw new IOException(ResetText("这个项目还没有完成 YikaiCMS 安装，没有管理员可以重置。","YikaiCMS is not installed in this project yet, so there is no admin to reset.","このプロジェクトは YikaiCMS のインストールが済んでいないため、リセットできる管理者がいません。"));
        if(site.Database is "mysql80" or "mysql57")await EnsureDatabaseAsync(site.Database);
        BundledDatabaseTools.EnsureCmsAdminTool(Root);
        var script=Path.Combine(Root,"soft","db-manager","cms-admin-reset.php");
        var input=JsonSerializer.Serialize(new{dir=site.Directory,user=Settings.CmsAdminUser,password=Settings.CmsAdminPassword});
        string output;
        try{output=await RunWithInput(Php,["-c",Path.Combine(Root,"soft","php",InternalPhpVersion,"php.ini"),"-d","display_errors=stderr",script],input);}
        catch(IOException e){output=e.Message;}
        var start=output.IndexOf('{');
        try
        {
            using var doc=JsonDocument.Parse(start>=0?output[start..]:output);
            var root=doc.RootElement;
            if(root.TryGetProperty("ok",out var ok)&&ok.ValueKind==JsonValueKind.True)
            {
                var action=root.TryGetProperty("action",out var a)?a.GetString()??"":"";
                Log($"CMS admin reset · {site.Domain} · {Settings.CmsAdminUser} ({action})");
                return action;
            }
            throw new IOException(ResetText("重置失败：","Reset failed: ","リセットに失敗しました：")+(root.TryGetProperty("error",out var error)?error.GetString():output.Trim()));
        }
        catch(JsonException){throw new IOException(ResetText("重置失败：","Reset failed: ","リセットに失敗しました：")+output.Trim());}
    }
}

public sealed partial class MainForm
{
    bool CmsInstalled(Site site)=>site.Template=="yikaicms"&&File.Exists(Path.Combine(site.Directory,"config","config.php"));

    void ResetCmsAdmin(Site site)
    {
        var user=settings.CmsAdminUser;var password=settings.CmsAdminPassword;
        var question=T(
            $"把「{site}」的后台管理员恢复成：\n\n用户名：{user}\n密码：{password}\n\n该账号会被启用、设为超级管理员，并关闭它的两步验证；账号不存在时自动新建。同时解除输错密码造成的登录锁定。其他管理员不受影响。\n\n继续吗？",
            $"Restore the admin account of \"{site}\" to:\n\nUser: {user}\nPassword: {password}\n\nThe account is enabled, made a super administrator and its two-step verification is turned off; it is created if missing. Login lockouts from wrong passwords are cleared. Other administrators are not changed.\n\nContinue?",
            $"「{site}」の管理者を次の内容に戻します。\n\nユーザー：{user}\nパスワード：{password}\n\nこのアカウントを有効にしてスーパー管理者にし、2 段階認証を解除します（なければ作成）。パスワード誤入力によるログインロックも解除します。他の管理者は変更しません。\n\n続けますか？");
        if(MessageBox.Show(this,question,T("重置后台管理员","Reset admin account","管理者のリセット"),MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        string? action=null;
        _=Finish();
        async Task Finish()
        {
            await Work(async()=>{action=await runtime.ResetCmsAdminAsync(site);},T($"后台管理员已恢复：{user} / {password}",$"Admin restored: {user} / {password}",$"管理者を復元しました：{user} / {password}"));
            if(action==null||IsDisposed)return;
            var text=T(
                (action=="created"?"已新建后台管理员。":"后台管理员已恢复。")+$"\n\n用户名：{user}\n密码：{password}\n\n建议登录后在后台修改密码。",
                (action=="created"?"The admin account was created.":"The admin account was restored.")+$"\n\nUser: {user}\nPassword: {password}\n\nChange the password after signing in.",
                (action=="created"?"管理者アカウントを作成しました。":"管理者アカウントを復元しました。")+$"\n\nユーザー：{user}\nパスワード：{password}\n\nログイン後にパスワードを変更してください。");
            MessageBox.Show(this,text,T("重置后台管理员","Reset admin account","管理者のリセット"),MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
    }
}
