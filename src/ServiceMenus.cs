namespace YikaiLocal;

// 状态栏服务：点击后单独启动、停止、重启；Web 服务器、PHP 版本与默认 MySQL 的选择。
public sealed partial class MainForm
{
    string ServiceName(string service)=>service switch{"web"=>Runtime.WebServerName(settings.WebServer),"php"=>"PHP","php8.0" or "php8.2" or "php8.5"=>"PHP "+Runtime.PhpVersionOf(service),"mysql80"=>"MySQL 8.0","mysql57"=>"MySQL 5.7",_=>T("数据库页面","DB manager","DB 管理")};
    void AttachServiceMenu(Label label,string service)
    {
        var menu=new ContextMenuStrip{Font=Font};label.Cursor=Cursors.Hand;
        label.MouseUp+=(_,e)=>{if(!busy&&e.Button is MouseButtons.Left or MouseButtons.Right)menu.Show(label,new Point(0,0),ToolStripDropDownDirection.AboveRight);};
        menu.Opening+=(_,e)=>
        {
            if(busy){e.Cancel=true;return;}
            while(menu.Items.Count>0)menu.Items[0].Dispose();
            var (any,all)=runtime.ServiceState(service);
            menu.Items.Add(new ToolStripMenuItem(ServiceName(service)){Enabled=false});menu.Items.Add(new ToolStripSeparator());
            AddMenuItem(menu.Items,T("启动","Start","起動"),"play",()=>_ = Work(()=>runtime.StartServiceAsync(service)),!all);
            AddMenuItem(menu.Items,T("停止","Stop","停止"),"stop",()=>{if(ConfirmServiceStop(service))_ = Work(()=>runtime.StopServiceAsync(service));},any);
            AddMenuItem(menu.Items,T("重启","Restart","再起動"),"sync",()=>_ = Work(()=>runtime.RestartServiceAsync(service)),any);
            if(service=="web"){menu.Items.Add(new ToolStripSeparator());menu.Items.Add(WebServerMenu());AddMenuItem(menu.Items,T($"编辑 {Runtime.WebServerName(settings.WebServer)} 配置",$"Edit {Runtime.WebServerName(settings.WebServer)} config",$"{Runtime.WebServerName(settings.WebServer)} 設定を編集"),"settings",()=>ShowConfigEditor(settings.WebServer));}
            if(service=="dbpage"){menu.Items.Add(new ToolStripSeparator());AddMenuItem(menu.Items,T("编辑 php.ini","Edit php.ini","php.ini を編集"),"settings",()=>ShowConfigEditor("dbpage"));}
            if(service is "mysql80" or "mysql57"){menu.Items.Add(new ToolStripSeparator());menu.Items.Add(DefaultMysqlMenu());AddMenuItem(menu.Items,T("编辑 my.ini","Edit my.ini","my.ini を編集"),"settings",()=>ShowConfigEditor(service));}
            if(service=="php"){menu.Items.Add(new ToolStripSeparator());foreach(var version in Runtime.PhpVersions)menu.Items.Add(PhpVersionMenu(version));menu.Items.Add(new ToolStripSeparator());menu.Items.Add(DefaultPhpMenu());}
        };
        label.Disposed+=(_,_)=>menu.Dispose();
    }
    void AddMenuItem(ToolStripItemCollection items,string text,string icon,Action action,bool enabled=true,bool check=false)
    {
        var item=new ToolStripMenuItem(text,icon==""?null:UiIcons.Create(icon,Ink,16)){Enabled=enabled,Checked=check,ForeColor=Palette.Text};
        item.Click+=(_,_)=>action();item.Disposed+=(_,_)=>item.Image?.Dispose();items.Add(item);
    }
    bool ConfirmServiceStop(string service)
    {
        var sites=runtime.AffectedSites(service);if(sites.Count==0)return true;
        var names=string.Join("\n",sites.Take(8).Select(s=>"· "+s))+(sites.Count>8?"\n…":"");
        return MessageBox.Show(this,T($"停止 {ServiceName(service)}？以下项目将暂时无法访问：\n{names}",$"Stop {ServiceName(service)}? These projects become unavailable:\n{names}",$"{ServiceName(service)} を停止しますか？次のプロジェクトが利用できなくなります：\n{names}"),T("停止服务","Stop service","サービス停止"),MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)==DialogResult.OK;
    }
    ToolStripMenuItem WebServerMenu()
    {
        var root=new ToolStripMenuItem(T("Web 服务器","Web server","Web サーバー")){ForeColor=Palette.Text};
        foreach(var kind in new[]{"nginx","apache"})
            AddMenuItem(root.DropDownItems,Runtime.WebServerName(kind),"",()=>{if(kind!=settings.WebServer)_ = Work(()=>runtime.SwitchWebServerAsync(kind));},runtime.WebServerInstalled(kind),kind==settings.WebServer);
        return root;
    }
    // 单个 PHP 版本：只作用于使用该版本、已启用的项目。
    ToolStripMenuItem PhpVersionMenu(string version)
    {
        var key="php"+version;var (any,all)=runtime.ServiceState(key);var installed=runtime.PhpInstalled(version);
        var count=settings.Sites.Count(s=>s.Enabled&&s.Php==version);
        var state=!installed?T("未安装","Not installed","未インストール"):count==0?T("无启用项目","No enabled projects","有効なプロジェクトなし"):all?T($"运行 · {count} 个项目",$"On · {count} projects",$"起動 · {count} 件"):any?T($"部分运行 · {count} 个项目",$"Partly on · {count} projects",$"一部起動 · {count} 件"):T($"已停止 · {count} 个项目",$"Off · {count} projects",$"停止中 · {count} 件");
        var root=new ToolStripMenuItem($"PHP {version} · {state}",UiIcons.Create(all&&count>0?"dot":"stop",all&&count>0?Palette.Success:Muted,16)){Name="phpVersion"+version,Enabled=installed,ForeColor=Palette.Text};
        root.Disposed+=(_,_)=>root.Image?.Dispose();
        AddMenuItem(root.DropDownItems,T("启动","Start","起動"),"play",()=>_ = Work(()=>runtime.StartServiceAsync(key)),count>0&&!all);
        AddMenuItem(root.DropDownItems,T("停止","Stop","停止"),"stop",()=>{if(ConfirmServiceStop(key))_ = Work(()=>runtime.StopServiceAsync(key));},any);
        AddMenuItem(root.DropDownItems,T("重启","Restart","再起動"),"sync",()=>_ = Work(()=>runtime.RestartServiceAsync(key)),any);
        root.DropDownItems.Add(new ToolStripSeparator());
        AddMenuItem(root.DropDownItems,T("扩展…","Extensions…","拡張…"),"tool",()=>ShowPhpExtensions(version),installed);
        AddMenuItem(root.DropDownItems,T("编辑 php.ini","Edit php.ini","php.ini を編集"),"settings",()=>ShowConfigEditor(key),installed);
        return root;
    }
    ToolStripMenuItem DefaultPhpMenu()
    {
        var root=new ToolStripMenuItem(T("新项目默认 PHP 版本","Default PHP for new projects","新規プロジェクトの既定 PHP")){Name="defaultPhp",ForeColor=Palette.Text};
        foreach(var version in Runtime.PhpVersions)
            AddMenuItem(root.DropDownItems,"PHP "+version+(version=="8.0"?T("（YikaiCMS 需 8.2+）"," (YikaiCMS needs 8.2+)","（YikaiCMS は 8.2+）"):""),"",()=>{settings.PhpDefault=version;settings.Save();RefreshState();},runtime.PhpInstalled(version),version==settings.PhpDefault);
        return root;
    }
    ToolStripMenuItem DefaultMysqlMenu()
    {
        var root=new ToolStripMenuItem(T("默认启动的 MySQL","MySQL started by default","既定で起動する MySQL")){ForeColor=Palette.Text};
        foreach(var kind in new[]{"mysql80","mysql57"})
            AddMenuItem(root.DropDownItems,ServiceName(kind),"",()=>{settings.MysqlActive=kind;settings.Save();RefreshState();},runtime.MysqlInstalled(kind),kind==settings.MysqlActive);
        return root;
    }
    void RefreshPhpService()
    {
        var enabled=settings.Sites.Where(p=>p.Enabled).ToList();var (any,all)=runtime.ServiceState("php");
        var versions=enabled.Count==0?settings.PhpDefault:string.Join("/",enabled.Select(p=>p.Php).Distinct().Order());
        var alive=enabled.Count(p=>runtime.ServiceRunning("php-"+p.Id));
        var warning=!all&&enabled.Count>0&&runtime.AnyRunning&&!busy;
        var state=all?T("运行","On","起動"):any?T($"运行 {alive}/{enabled.Count}",$"On {alive}/{enabled.Count}",$"起動 {alive}/{enabled.Count}"):warning?T("未运行","Not running","未起動"):T("已停止","Off","停止中");
        phpState.Text=$"PHP {versions} · {state}";
        phpState.ForeColor=all?Palette.Success:warning?Palette.Warning:Muted;
        SetIcon(phpState,all?"dot":warning?"warning":"stop",phpState.ForeColor);
        serviceTips.SetToolTip(phpState,enabled.Count==0?T("没有启用的项目","No enabled projects","有効なプロジェクトなし"):string.Join("\n",enabled.Select(p=>$"{p} · PHP {p.Php} · 127.0.0.1:{p.FastCgiPort} · PID {runtime.ServicePid("php-"+p.Id)?.ToString()??"—"}")));
    }
}
