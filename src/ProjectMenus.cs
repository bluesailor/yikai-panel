namespace YikaiLocal;

public sealed partial class MainForm
{
    void AttachProjectMenu()
    {
        var menu=new ContextMenuStrip{Font=Font};projects.ContextMenuStrip=menu;
        projects.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Right)projects.SelectedIndex=projects.IndexFromPoint(e.Location);};
        menu.Opening+=(_,e)=>
        {
            if(busy){e.Cancel=true;return;}
            while(menu.Items.Count>0)menu.Items[0].Dispose();
            void Add(string text,string icon,Action action,bool enabled=true){var item=new ToolStripMenuItem(text,UiIcons.Create(icon,Ink,16)){Enabled=enabled,ForeColor=Palette.Text};item.Click+=(_,_)=>action();item.Disposed+=(_,_)=>item.Image?.Dispose();menu.Items.Add(item);}
            if(Selected is {} site)
            {
                Add(site.Starred?T("取消星标","Unstar project","スター解除"):T("添加星标","Star project","スターを付ける"),"star",()=>ToggleProjectStar(site));
                Add(T("SSL 证书","SSL certificate","SSL 証明書"),"lock",()=>ShowSslDialog(site));
                Add(T("PHP 扩展","PHP extensions","PHP 拡張"),"tool",()=>ShowPhpExtensions(site.Php,site));
                Add(open.Text,"globe",()=>open.PerformClick(),open.Enabled);Add(admin.Text,"login",()=>admin.PerformClick(),admin.Enabled);Add(database.Text,"database",()=>database.PerformClick(),database.Enabled);Add(folder.Text,"folder-open",()=>folder.PerformClick());
                menu.Items.Add(new ToolStripSeparator());
                Add(start.Text,"play",()=>start.PerformClick(),start.Enabled);Add(stop.Text,"stop",()=>stop.PerformClick(),stop.Enabled);Add(edit.Text,"settings",()=>edit.PerformClick());
                Add(T("后台入口设置","Admin entry settings","管理入口設定"),"settings",()=>_ = ConfigureBackend(site));
                if(site.Template=="yikaicms")Add(T($"重置后台管理员（{settings.CmsAdminUser} / {settings.CmsAdminPassword}）…",$"Reset admin account ({settings.CmsAdminUser} / {settings.CmsAdminPassword})…",$"管理者をリセット（{settings.CmsAdminUser} / {settings.CmsAdminPassword}）…"),"login",()=>ResetCmsAdmin(site),CmsInstalled(site));
                Add(T("重新计算目录大小","Recalculate folder size","容量を再計算"),"disk",()=>_ = MeasureSelectedDirectory(true));
                Add(T("伪静态配置","URL rewrite","リライトルール"),"settings",()=>RewriteDialog(site));
                Add(T("复制网址","Copy URL","URL をコピー"),"globe",()=>Clipboard.SetText(runtime.SiteUrl(site)));
                Add(T("复制目录","Copy folder path","パスをコピー"),"folder",()=>Clipboard.SetText(site.Directory));
                menu.Items.Add(new ToolStripSeparator());Add(remove.Text,"remove",()=>remove.PerformClick());
                menu.Items.Add(new ToolStripSeparator());
            }
            Add(T("新建项目","New project","プロジェクトを追加"),"add",()=>ProjectDialog(null));
            Add(T("扫描目录添加项目","Scan a folder for projects","フォルダーをスキャンして追加"),"search",()=>ScanFolderForProjects());
            if(PhpStudyDetection.Detected())Add(T("从 PHPStudy 导入","Import PHPStudy","PHPStudy から取込"),"folder",()=>ShowPhpStudyImport());
        };
        // Menu lifetime belongs to the list, never to the Closed event.
        projects.Disposed+=(_,_)=>menu.Dispose();
    }
}
