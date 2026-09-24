namespace YikaiLocal;

// 项目的 HTTPS 状态：列表里的小徽标和卡片上的“标题 + 内容”信息行共用同一套判断。
public sealed partial class MainForm
{
    const int SoonDays=30;
    static int DaysLeft(DateTime date)=>(int)(date.Date-DateTime.Now.Date).TotalDays;
    // 启用 HTTPS 的项目显示 HTTPS 徽标：正常为绿色，30 天内到期为警示色，已过期或证书缺失为赤陶色。
    (string Text,Color Color)? HttpsBadge(Site site)
    {
        if(!site.Https)return null;
        var certificate=runtime.SiteCertificateInfo(site);
        if(certificate==null)return ("HTTPS",Palette.Brand);
        var days=DaysLeft(certificate.NotAfter);
        return ("HTTPS",days<0?Palette.Brand:days<=SoonDays?Palette.Warning:Palette.Success);
    }
    // 项目卡片：一排标签（PHP / 数据库 / 运行状态）+ 若干“标题 + 内容”信息行，行数变化时卡片高度跟着收缩。
    void UpdateProjectFacts(Site? site)
    {
        if(site==null)
        {
            chips.Set();info.WarnLines=[];info.CaptionWidth=0;info.ClearActions();
            info.Text=T("新建 YikaiCMS、空白 PHP，或接入已有目录。","Create YikaiCMS, a blank PHP project, or connect a folder.","YikaiCMS、PHP、既存フォルダーを追加できます。");
            projectCard.RowStyles[3].Height=info.LineHeight+8;serviceTips.SetToolTip(info,"");return;
        }
        var running=runtime.IsRunning(site);
        var engine=site.Database=="sqlite"?"SQLite":site.Database=="none"?T("无数据库","No database","DB なし"):"MySQL "+settings.DatabaseVersion(site.Database);
        chips.Set([("PHP "+site.Php,Palette.Secondary),(engine,Palette.Secondary),(running?T("运行中","Running","起動中"):T("已停止","Stopped","停止中"),running?Palette.Success:Palette.Stopped)]);
        var facts=new List<(string Caption,string Value,bool Warning)>();int directoryLine,domainLine,databaseLine=-1;
        if(site.Database is "mysql80" or "mysql57")
            {facts.Add((T("数据库","Database","データベース"),T("库 ","DB ","DB ")+site.DatabaseName+T(" · 用户 "," · user "," · ユーザー ")+(string.IsNullOrEmpty(site.DatabaseUser)?settings.MysqlUser+T("（共用）"," (shared)","（共有）"):site.DatabaseUser)+(HeidiSqlAvailable?T("，点击用 HeidiSQL 客户端打开"," — click to open in HeidiSQL","（クリックで HeidiSQL を開く）"):""),false));databaseLine=facts.Count-1;}
        else if(site.Database=="sqlite"){facts.Add((T("数据库","Database","データベース"),"SQLite · storage/database.sqlite"+(HeidiSqlAvailable?T("，点击用 HeidiSQL 客户端打开"," — click to open in HeidiSQL","（クリックで HeidiSQL を開く）"):""),false));databaseLine=facts.Count-1;}
        facts.Add((T("目录","Folder","フォルダー"),site.Directory,false));directoryLine=facts.Count-1;
        var hostsSynced=runtime.HasHosts(site);
        facts.Add((T("域名","Domain","ドメイン"),hostsSynced?T($"{site.Domain} 已连接，点击打开网站",$"{site.Domain} connected — click to open",$"{site.Domain} 接続済み（クリックで開く）"):T("未同步，点击同步域名到 hosts","Not synced — click to sync domain to hosts","未同期（クリックして hosts に登録）"),false));domainLine=facts.Count-1;
        if(HttpsFact(site) is {} https)facts.Add(https);
        info.CaptionWidth=CaptionColumn;
        info.Text=string.Join("\n",facts.Select(f=>f.Caption+"\t"+f.Value));
        info.WarnLines=facts.Select((f,i)=>(f,i)).Where(x=>x.f.Warning).Select(x=>x.i).ToArray();
        projectCard.RowStyles[3].Height=info.LineHeight*facts.Count+8;
        // 目录和域名可以直接点；域名未同步时复用顶部“同步域名”的提权流程。
        info.ClearActions();
        if(databaseLine>=0&&HeidiSqlAvailable)info.SetAction(databaseLine,()=>{if(!busy)_ = OpenInHeidiSql(site);});
        info.SetAction(directoryLine,()=>{if(!busy&&Directory.Exists(site.Directory))Open(site.Directory);});
        info.SetAction(domainLine,()=>{if(!busy)_ = hostsSynced?OpenSite(false):SyncHosts();});
        serviceTips.SetToolTip(info,string.Join("\n",facts.Select(f=>f.Caption+"："+f.Value))+"\n"+(hostsSynced?T("点击目录打开文件夹，点击域名打开网站。","Click the folder to open it, click the domain to open the website.","フォルダー行でフォルダーを、ドメイン行でサイトを開けます。"):T("点击目录打开文件夹，点击域名同步到 hosts。","Click the folder to open it, click the domain to sync hosts.","フォルダー行でフォルダーを開き、ドメイン行で hosts に登録できます。")));
    }
    // 卡片上的 HTTPS 行：证书来源与有效期；证书缺失或快到期时用警示色。
    (string Caption,string Value,bool Warning)? HttpsFact(Site site)
    {
        if(!site.Https)return null;
        var caption="HTTPS";
        var certificate=runtime.SiteCertificateInfo(site);
        if(certificate==null)return (caption,T("证书缺失，请在配置 → SSL 证书中重新签发","Certificate missing; re-issue it in Settings → SSL certificates","証明書がありません（設定 → SSL 証明書で再発行）"),true);
        var days=DaysLeft(certificate.NotAfter);
        var source=site.CertificateSource=="custom"?T("导入的证书","Imported certificate","インポートした証明書"):T("自动签发","Auto-issued","自動発行");
        var value=source+T($" · 有效期至 {certificate.NotAfter:yyyy-MM-dd}",$" · valid until {certificate.NotAfter:yyyy-MM-dd}",$" · {certificate.NotAfter:yyyy-MM-dd} まで")
            +(days<0?T("（已过期）"," (expired)","（期限切れ）"):days<=SoonDays?T($"（{days} 天后过期）",$" ({days} days left)",$"（残り {days} 日）"):"");
        return (caption,value,days<=SoonDays);
    }
}
