namespace YikaiLocal;

public sealed partial class MainForm
{
    void RewriteDialog(Site site)
    {
        if(busy)return;
        using var dialog=new Form{Name="rewriteDialog",Text=T("伪静态配置","URL rewrite","リライトルール")+" · "+site.Domain,Size=new Size(940,730),MinimumSize=new Size(800,620),StartPosition=FormStartPosition.CenterParent,Font=Font,Icon=Icon};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=4};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,78));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));dialog.Controls.Add(layout);
        var top=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(top,0,0);
        var templates=new Choice{Width=300};templates.Items.AddRange([T("项目默认","Project default","プロジェクト既定"),"YikaiCMS",T("通用 / WordPress / Laravel","Generic / WordPress / Laravel","汎用 / WordPress / Laravel"),"ThinkPHP",T("关闭伪静态","No rewrite","リライトなし")]);templates.SelectedIndex=0;
        var load=new ThemeButton{Text=T("载入模板","Load template","テンプレート読込"),Width=210,Height=36};top.Controls.Add(templates);top.Controls.Add(load);
        var hint=new Label{Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,Fs(9f)),Text=settings.WebServer=="apache"?T("当前 Web 服务器为 Apache：伪静态以项目目录中的 .htaccess 为准。\n这里编辑的是 Nginx 规则，会保存并在切换回 Nginx 后生效。","Apache is the current web server: rewrites come from the project .htaccess.\nThese Nginx rules are saved and apply after switching back to Nginx.","現在は Apache：リライトはプロジェクトの .htaccess を使用します。\nここの Nginx ルールは保存され、Nginx に戻すと適用されます。"):T("编辑 Nginx server 内的完整规则（不是 Apache .htaccess）。保留 PHP 处理段。\n{{PHP_PORT}} 和 {{FASTCGI_PARAMS}} 自动匹配当前项目。Laravel / ThinkPHP 请确保项目目录指向正确入口。","Edit the full rules inside an Nginx server (not Apache .htaccess). Keep the PHP handler.\n{{PHP_PORT}} and {{FASTCGI_PARAMS}} follow the project. Laravel / ThinkPHP need the correct document root.","Nginx server 内のルールを編集（.htaccess 非対応）。PHP 処理を残してください。\n{{PHP_PORT}} / {{FASTCGI_PARAMS}} は自動設定。公開ディレクトリを確認してください。"),AutoEllipsis=true};layout.Controls.Add(hint,0,1);
        var editor=new TextBox{Dock=DockStyle.Fill,Multiline=true,AcceptsTab=true,AcceptsReturn=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=CreateCodeFont(),MaxLength=131072,Text=(site.RewriteRules??runtime.RewriteTemplate(site,"default")).ReplaceLineEndings(Environment.NewLine)};layout.Controls.Add(editor,0,2);
        bool dirty=false,reset=false,working=false;editor.TextChanged+=(_,_)=>dirty=true;
        load.Click+=(_,_)=>{if(dirty&&MessageBox.Show(dialog,T("用模板替换当前编辑内容？","Replace edits with the template?","編集内容をテンプレートで置き換えますか？"),dialog.Text,MessageBoxButtons.OKCancel)!=DialogResult.OK)return;editor.Text=runtime.RewriteTemplate(site,new[]{"default","yikaicms","front","thinkphp","plain"}[templates.SelectedIndex]).ReplaceLineEndings(Environment.NewLine);reset=templates.SelectedIndex==0;dirty=true;};
        editor.TextChanged+=(_,_)=>reset=false;
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};layout.Controls.Add(bottom,0,3);
        var save=new ThemeButton{Text=T("保存并应用","Save and apply","保存して適用"),Width=250,Height=36};var check=new ThemeButton{Text=T("检查配置","Check configuration","設定を検査"),Width=250,Height=36};var cancel=new ThemeButton{Text=T("取消","Cancel","キャンセル"),Width=144,Height=36,DialogResult=DialogResult.Cancel};bottom.Controls.Add(save);bottom.Controls.Add(check);bottom.Controls.Add(cancel);dialog.CancelButton=cancel;
        async Task Apply(bool test)
        {
            if(working)return;working=true;top.Enabled=editor.Enabled=bottom.Enabled=false;busy=true;
            try{using var operation=Runtime.Lock(settings);runtime.Adopt();await runtime.SaveRewriteAsync(site,reset?null:editor.Text,test);MessageBox.Show(dialog,test?T("配置检查通过。","Configuration is valid.","設定の検査に成功しました。"):T("已保存。运行中的 Nginx 已重载；停止的项目将在下次启动时使用。","Saved. Running Nginx reloaded; stopped projects use this on next start.","保存しました。稼働中の Nginx に反映。停止中は次回起動時に適用。"));if(!test){dirty=false;working=false;dialog.DialogResult=DialogResult.OK;}}
            catch(Exception error){MessageBox.Show(dialog,error.Message,dialog.Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
            finally{working=false;busy=false;top.Enabled=editor.Enabled=bottom.Enabled=true;RefreshState();}
        }
        check.Click+=async(_,_)=>await Apply(true);save.Click+=async(_,_)=>await Apply(false);
        dialog.FormClosing+=(_,e)=>{if(working){e.Cancel=true;return;}if(dirty&&dialog.DialogResult!=DialogResult.OK&&MessageBox.Show(dialog,T("放弃未保存的规则？","Discard unsaved rules?","未保存のルールを破棄しますか？"),dialog.Text,MessageBoxButtons.OKCancel)!=DialogResult.OK)e.Cancel=true;};
        Palette.Show(dialog,this);
    }
}
