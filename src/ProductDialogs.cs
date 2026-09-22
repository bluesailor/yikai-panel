using System.Diagnostics;

namespace YikaiLocal;

public sealed partial class MainForm
{
    void ShowAbout()
    {
        using var dialog=new Form{Name="aboutDialog",Text=T("关于易开面板","About Yikai Panel","Yikai Panel について"),ClientSize=new Size(640,432),Font=Font,Icon=Icon,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=7};foreach(var height in new[]{78,44,58,44,30,60,70})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));dialog.Controls.Add(layout);
        layout.Controls.Add(Branding.CreateHeader(),0,0);layout.Controls.Add(L(T("版本 ","Version ","バージョン ")+PanelUpdate.CurrentVersion+" · Windows x64",11,true),0,1);
        layout.Controls.Add(L(T("本地 PHP 项目管理与运行环境","Local PHP project manager and runtime","ローカル PHP プロジェクト管理環境"),10),0,2);
        var website=new LinkLabel{Text=PanelUpdate.Website,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};website.LinkClicked+=(_,_)=>Open(PanelUpdate.Website);layout.Controls.Add(website,0,3);
        var feedback=L(T("意见与建议反馈","Feedback and suggestions","ご意見・ご要望"),10);layout.Controls.Add(feedback,0,4);
        var email=new LinkLabel{Name="feedbackEmail",Text="support@yikay.com",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
        email.LinkClicked+=(_,_)=>{try{Open("mailto:support@yikay.com?subject="+Uri.EscapeDataString("Yikai Panel feedback"));}catch(Exception e) when(e is System.ComponentModel.Win32Exception or InvalidOperationException){MessageBox.Show(dialog,T("请发送邮件至：","Please email: ","メール送信先：")+"support@yikay.com",feedback.Text,MessageBoxButtons.OK,MessageBoxIcon.Information);}};layout.Controls.Add(email,0,5);
        var close=new ThemeButton{Name="closeAbout",Text=T("关闭","Close","閉じる"),Width=144,Height=36,DialogResult=DialogResult.Cancel,CenterContent=true};AttachButtonIcon(close,"close");var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};footer.Controls.Add(close);layout.Controls.Add(footer,0,6);dialog.CancelButton=close;Palette.Show(dialog,this);
    }
    void ShowUpgrade()
    {
        using var dialog=new Form{Name="upgradeDialog",Text=T("升级易开面板","Update Yikai Panel","Yikai Panel の更新"),ClientSize=new Size(730,460),Font=Font,Icon=Icon,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=5};foreach(var h in new[]{46,58,208,44,56})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));dialog.Controls.Add(layout);
        layout.Controls.Add(L(T("当前版本：","Current version: ","現在のバージョン：")+PanelUpdate.CurrentVersion,11,true),0,0);
        var status=L(T("点击检查更新，获取可用的新版本。","Check for a newer version.","新しいバージョンを確認できます。"),10);layout.Controls.Add(status,0,1);
        var notes=new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical};layout.Controls.Add(notes,0,2);
        var website=new LinkLabel{Text=PanelUpdate.Website,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};website.LinkClicked+=(_,_)=>Open(PanelUpdate.Website);layout.Controls.Add(website,0,3);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};layout.Controls.Add(footer,0,4);
        var install=new ThemeButton{Name="installUpdate",Text=T("下载并升级","Download & update","ダウンロード更新"),Width=266,Height=36,Enabled=false,BackColor=Palette.Accent};var check=new ThemeButton{Name="checkUpdate",Text=T("检查更新","Check updates","更新を確認"),Width=214,Height=36};
        // 关闭按钮带图标时内容整体居中，与其他对话框（关闭按钮无图标、文字居中）保持一致
        var cancel=new ThemeButton{Name="closeUpgrade",Text=T("关闭","Close","閉じる"),Width=144,Height=36,DialogResult=DialogResult.Cancel,CenterContent=true};AttachButtonIcon(install,"download");AttachButtonIcon(check,"sync");AttachButtonIcon(cancel,"close");footer.Controls.AddRange([install,check,cancel]);dialog.CancelButton=cancel;
        using var cancellation=new CancellationTokenSource();using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(5)};PanelRelease? release=null;bool working=false,applying=false;
        check.Click+=async(_,_)=>{
            if(working)return;working=true;check.Enabled=install.Enabled=false;status.Text=T("正在检查…","Checking…","確認中…");
            try{using var request=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);request.CancelAfter(TimeSpan.FromSeconds(12));release=await PanelUpdate.CheckAsync(client,request.Token);if(dialog.IsDisposed)return;status.Text=release==null?T("当前已是最新版本。","You are up to date.","最新のバージョンです。"):T("发现新版本：","New version: ","新しいバージョン：")+release.Version;notes.Text=release?.Notes??"";install.Enabled=release!=null;}
            catch(Exception e) when(e is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException or ArgumentException){if(!dialog.IsDisposed){release=null;status.Text=T("更新服务尚未就绪，请稍后再试。","The update service is not ready. Try again later.","更新サービスは準備中です。後でもう一度お試しください。");}}
            finally{working=false;if(!dialog.IsDisposed)check.Enabled=true;}
        };
        install.Click+=async(_,_)=>{
            if(working||release==null)return;working=true;check.Enabled=install.Enabled=false;
            try{
                var target=Path.GetFullPath(Path.Combine(settings.Root,"soft","panel","YikaiLocal.exe"));if(!string.Equals(Environment.ProcessPath,target,StringComparison.OrdinalIgnoreCase))throw new IOException("Run the installed panel to apply an update.");
                var file=await PanelUpdate.DownloadAsync(settings,release,client,new Progress<int>(percent=>{if(!dialog.IsDisposed)status.Text=T($"正在下载：{percent}%",$"Downloading: {percent}%",$"ダウンロード中：{percent}%");}),cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();using var current=Process.GetCurrentProcess();var start=PanelUpdate.PrepareApply(settings,file,release.Sha256,current.Id,current.StartTime.ToUniversalTime().Ticks,T("升级未完成，已保留原面板。请查看运行日志或稍后重试。","The update could not complete. Your previous panel is preserved.","更新できませんでした。元のパネルは保持されています。"));
                if(busy)throw new IOException("Wait for the current operation to finish.");using var operation=Runtime.Lock(settings);
                using var helper=Process.Start(start)??throw new IOException("Cannot start the update helper.");applying=true;exit=true;dialog.DialogResult=DialogResult.OK;Close();
            }catch(OperationCanceledException){}
            catch(Exception e) when(e is IOException or HttpRequestException or System.ComponentModel.Win32Exception or ArgumentException){if(!dialog.IsDisposed){status.Text=T("升级未完成，当前版本仍可使用。","Update not completed. The current version is still available.","更新できませんでした。現在のバージョンは使用できます。");notes.Text=e.Message;}}
            finally{working=false;if(!dialog.IsDisposed){check.Enabled=true;install.Enabled=release!=null;}}
        };
        dialog.FormClosing+=(_,_)=>{if(!applying)cancellation.Cancel();};Palette.Show(dialog,this);
    }
}
