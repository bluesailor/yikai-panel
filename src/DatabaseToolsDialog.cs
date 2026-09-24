namespace YikaiLocal;

public sealed partial class MainForm
{
    // 顶部“工具 / 配置”下拉与按钮之间留一点空隙，贴着按钮底边弹出时两者边框连在一起，看着像一块。
    int MenuGap=>Px(6);
    void AttachEnvironmentTools(Button button)
    {
        // 工具：一次性操作（导入、打开数据库页面、看日志）。
        var menu=new ContextMenuStrip{Font=Font};
        AddMenuItem(menu.Items,T("从 PHPStudy 导入","Import from PHPStudy","PHPStudy から取込"),"folder",()=>ShowPhpStudyImport());var importItem=menu.Items[0];importItem.Name="importPhpStudy";
        AddMenuItem(menu.Items,T("扫描目录添加项目","Scan a folder for projects","フォルダーをスキャンして追加"),"search",()=>ScanFolderForProjects());menu.Items[^1].Name="scanSitesMenu";
        // 两条数据库入口按“在哪管理”分开命名：一条是浏览器里的网页管理，一条是桌面客户端（HeidiSQL）。
        AddMenuItem(menu.Items,T("网页管理数据库","Database manager (web)","データベース管理（Web）"),"database",()=>_ = OpenDatabaseManager());var databaseItem=menu.Items[^1];databaseItem.Name="databaseManagerMenu";
        AddMenuItem(menu.Items,T("客户端管理（HeidiSQL）","Database client (HeidiSQL)","データベース管理（HeidiSQL）"),"database",()=>_ = OpenInHeidiSql());menu.Items[^1].Name="heidiSqlMenu";
        menu.Items.Add(new ToolStripSeparator());
        AddMenuItem(menu.Items,T("运行日志","Runtime logs","実行ログ"),"logs",()=>Open(Path.Combine(settings.Root,"logs")));menu.Items[^1].Name="runtimeLogsMenu";
        AddMenuItem(menu.Items,T("端口排查","Port check","ポート確認"),"search",()=>ShowPortDiagnostics());menu.Items[^1].Name="portDiagnosticsMenu";
        menu.Opening+=(_,_) => {databaseItem.Enabled=settings.Sites.Count>0;importItem.Visible=PhpStudyDetection.Detected();};
        button.Click+=(_,_)=>menu.Show(button,new Point(0,button.Height+MenuGap));button.Disposed+=(_,_)=>menu.Dispose();
    }
    // 配置：会改变环境行为的设置（配置文件、Web 服务器、默认版本、root 密码）。
    void AttachEnvironmentConfig(Button button)
    {
        var menu=new ContextMenuStrip{Font=Font};
        AddMenuItem(menu.Items,T("外观","Appearance","外観"),"theme",()=>ShowAppearance());menu.Items[0].Name="appearanceMenu";
        AddMenuItem(menu.Items,T("配置文件（php.ini 等）","Configuration files (php.ini…)","設定ファイル（php.ini など）"),"settings",()=>ShowConfigEditor());menu.Items[^1].Name="configFilesMenu";
        AddMenuItem(menu.Items,T("PHP 扩展","PHP extensions","PHP 拡張"),"tool",()=>ShowPhpExtensions());menu.Items[^1].Name="phpExtensionsMenu";
        AddMenuItem(menu.Items,T("局域网访问","LAN access","LAN アクセス"),"globe",()=>ShowLanAccess());menu.Items[^1].Name="lanAccessMenu";
        AddMenuItem(menu.Items,T("SSL 证书","SSL certificates","SSL 証明書"),"lock",()=>ShowSslDialog());var sslItem=menu.Items[^1];sslItem.Name="sslCertificatesMenu";
        menu.Opening+=(_,_)=>sslItem.Enabled=settings.Sites.Count>0;
        menu.Items.Add(new ToolStripSeparator());
        var choicesEnd=new ToolStripSeparator{Name="configChoicesEnd"};menu.Items.Add(choicesEnd);
        AddMenuItem(menu.Items,T("MySQL root 密码","MySQL root password","MySQL root パスワード"),"database",()=>ShowRootPasswordReset());menu.Items[^1].Name="rootPasswordMenu";
        AddMenuItem(menu.Items,T("数据库端口","Database port","データベースのポート"),"database",()=>ShowDatabasePort());menu.Items[^1].Name="databasePortMenu";
        // 子菜单的勾选状态随当前设置变化，每次打开时重新生成。
        menu.Opening+=(_,_)=>{
            foreach(var old in menu.Items.OfType<ToolStripMenuItem>().Where(i=>i.Tag as string=="runtime-choice").ToArray())old.Dispose();
            var at=menu.Items.IndexOf(choicesEnd);
            foreach(var choice in new[]{WebServerMenu(),DefaultPhpMenu(),DefaultMysqlMenu()}){choice.Tag="runtime-choice";menu.Items.Insert(at++,choice);}
        };
        button.Click+=(_,_)=>menu.Show(button,new Point(0,button.Height+MenuGap));button.Disposed+=(_,_)=>menu.Dispose();
    }
    // 数据库管理页面（备份、恢复、查看数据、Adminer）：只启动它需要的服务，不启动整套环境。
    Task OpenDatabaseManager()=>Work(async()=>{
        var site=Selected??settings.Sites.FirstOrDefault();if(site==null)return;
        if(!runtime.WebServerRunning)await runtime.StartServiceAsync("web");
        if(!runtime.ServiceRunning("php-db"))await runtime.StartServiceAsync("dbpage");
        if(site.Database is "mysql80" or "mysql57")await runtime.EnsureDatabaseAsync(site.Database);
        Open(runtime.DatabaseUrl(site));
    });
    void ShowRootPasswordReset()
    {
        using var dialog=new Form{Name="rootPasswordDialog",Text=T("MySQL root 密码","MySQL root password","MySQL root パスワード"),ClientSize=new Size(700,370),Font=Font,Icon=Icon,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=5};foreach(var height in new[]{82,48,50,86,56})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));dialog.Controls.Add(layout);
        layout.Controls.Add(L(T("设置 root 新密码，默认 123456。所选 MySQL 会短暂重启，完成后恢复原运行状态。","Set a new root password. The default is 123456. MySQL briefly restarts, then returns to its previous state.","root の新しいパスワードを設定します。初期値は 123456 です。MySQL を一時再起動します。"),9),0,0);
        var engine=new Choice{Name="rootResetEngine",Dock=DockStyle.Top,Height=36,Margin=Padding.Empty};engine.Items.AddRange([$"MySQL 8.0 · 127.0.0.1:{settings.Mysql80Port}",$"MySQL 5.7 · 127.0.0.1:{settings.Mysql57Port}"]);engine.SelectedIndex=Selected?.Database=="mysql57"?1:0;layout.Controls.Add(engine,0,1);
        var passwordRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};passwordRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));passwordRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        passwordRow.Controls.Add(L(T("新密码","New password","新パスワード"),10),0,0);var password=new TextBox{Name="newRootPassword",Text="123456",MaxLength=128};passwordRow.Controls.Add(new InputFrame(password){Dock=DockStyle.Top,Height=36,Margin=Padding.Empty},1,0);layout.Controls.Add(passwordRow,0,2);
        var status=L(T("适用于易开管理的本地 MySQL。无需提供旧密码。","For MySQL managed by Yikai Panel. No old password is required.","Yikai Panel が管理する MySQL が対象です。旧パスワードは不要です。"),9);layout.Controls.Add(status,0,3);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};layout.Controls.Add(footer,0,4);
        var reset=new ThemeButton{Name="resetRootPassword",Text=T("保存新密码","Save new password","新パスワードを保存"),Width=270,Height=36,BackColor=Palette.Accent};var close=new ThemeButton{Text=T("关闭","Close","閉じる"),Width=140,Height=36,DialogResult=DialogResult.Cancel};footer.Controls.AddRange([reset,close]);dialog.CancelButton=close;bool resetting=false;
        reset.Click+=async(_,_)=>{
            if(resetting||busy)return;if(password.Text.Length==0||password.Text.Any(char.IsControl)){status.Text=T("请填写有效的新密码。","Enter a valid new password.","有効な新パスワードを入力してください。");return;}resetting=true;reset.Enabled=close.Enabled=engine.Enabled=password.Enabled=false;var kind=engine.SelectedIndex==1?"mysql57":"mysql80";RootPasswordResetResult? result=null;
            status.Text=T("正在处理，请稍候…","Resetting, please wait…","処理中です。しばらくお待ちください…");
            try{await Work(async()=>{result=await runtime.ResetRootPasswordAsync(kind,password.Text);});if(!dialog.IsDisposed)status.Text=result==null?T("未完成，请查看错误提示或运行日志。","Not completed. See the error message or runtime logs.","完了できませんでした。エラーまたはログを確認してください。"):result.Manual.Count>0?T("密码已修改。以下项目请手动检查连接：","Password changed. Review these project connections: ","変更済みです。次の接続を確認してください：")+string.Join(", ",result.Manual):T($"密码已修改，连接验证成功；同步 {result.Updated.Count} 个项目。",$"Password changed and verified; {result.Updated.Count} project configurations updated.",$"変更と接続確認が完了しました。{result.Updated.Count} 件の設定を更新しました。");}
            finally{resetting=false;if(!dialog.IsDisposed)reset.Enabled=close.Enabled=engine.Enabled=password.Enabled=true;}
        };
        dialog.FormClosing+=(_,e)=>{if(resetting)e.Cancel=true;};Palette.Show(dialog,this);
    }
    // 数据库端口：改成常用端口（例如 3306）。已初始化的实例会重启并验证连接，然后同步已装站点的连接配置。
    void ShowDatabasePort()
    {
        using var dialog=new Form{Name="databasePortDialog",Text=T("数据库端口","Database port","データベースのポート"),ClientSize=new Size(720,380),Font=Font,Icon=Icon,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=5};foreach(var height in new[]{92,48,50,80,64})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));dialog.Controls.Add(layout);
        layout.Controls.Add(L(T("把面板的 MySQL 改到常用端口，例如 3306。已初始化的实例会短暂重启并验证连接，已装站点（YikaiCMS / WordPress）的数据库配置会一起更新；端口被其他程序占用时会直接提示占用者，不会自动换端口。\n未初始化的实例只保存端口，首次启动时生效。",
            "Move the panel's MySQL to a standard port such as 3306. An initialized instance restarts briefly and the connection is verified; installed sites (YikaiCMS / WordPress) are updated too. If the port is taken by another program, the holder is reported instead of silently moving on.\nA database that has not been initialized yet just stores the port for its first start.",
            "パネルの MySQL を 3306 などの常用ポートに変更します。初期化済みの場合は短時間再起動して接続を検証し、既存サイト（YikaiCMS / WordPress）の設定も更新します。他プログラムが使用中の場合はその名前を表示し、自動で別ポートにはしません。\n未初期化の場合はポートを保存し、初回起動時に適用します。"),9),0,0);
        var engine=new Choice{Name="databasePortEngine",Dock=DockStyle.Top,Height=36,Margin=Padding.Empty};
        engine.Items.AddRange([$"MySQL 8.0 · 127.0.0.1:{settings.Mysql80Port}",$"MySQL 5.7 · 127.0.0.1:{settings.Mysql57Port}"]);
        engine.SelectedIndex=Selected?.Database=="mysql57"?1:0;layout.Controls.Add(engine,0,1);
        var portRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var portLabel=L(T("新端口（常用 3306）","New port (3306 is standard)","新しいポート（常用 3306）"),10);portRow.Controls.Add(portLabel,0,0);
        var port=new TextBox{Name="databasePortValue",MaxLength=5};portRow.Controls.Add(new InputFrame(port){Dock=DockStyle.Top,Height=36,Margin=Padding.Empty},1,0);layout.Controls.Add(portRow,0,2);
        var status=L("",9);status.Name="databasePortStatus";status.ForeColor=Muted;status.TextAlign=ContentAlignment.TopLeft;layout.Controls.Add(status,0,3);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,10,0,0)};layout.Controls.Add(footer,0,4);
        var apply=new ThemeButton{Name="applyDatabasePort",Text=T("应用新端口","Apply port","ポートを適用"),Width=220,Height=36,BackColor=Palette.Accent};
        var close=new ThemeButton{Text=T("关闭","Close","閉じる"),Width=120,Height=36,DialogResult=DialogResult.Cancel};footer.Controls.AddRange([apply,close]);dialog.CancelButton=close;
        void RefreshPort(){var kind=engine.SelectedIndex==1?"mysql57":"mysql80";port.Text=settings.DatabasePort(kind).ToString();status.Text=T($"当前端口：{settings.DatabasePort(kind)}；数据库页面 {settings.DbManagerPort}、另一个 MySQL 版本 {settings.DatabasePort(kind=="mysql80"?"mysql57":"mysql80")} 不能占用。",
            $"Current port: {settings.DatabasePort(kind)}; the database page ({settings.DbManagerPort}) and the other MySQL version ({settings.DatabasePort(kind=="mysql80"?"mysql57":"mysql80")}) are reserved.",
            $"現在のポート：{settings.DatabasePort(kind)}。データベースページ（{settings.DbManagerPort}）ともう一方の MySQL（{settings.DatabasePort(kind=="mysql80"?"mysql57":"mysql80")}）は使用できません。");}
        bool applying=false;
        engine.SelectedIndexChanged+=(_,_)=>{if(!applying&&!busy)RefreshPort();};
        apply.Click+=async(_,_)=>{
            if(applying||busy)return;
            if(!int.TryParse(port.Text.Trim(),out var wanted)){status.Text=T("端口请填 1–65535 的数字。","Enter a port number (1–65535).","ポートは 1～65535 の数字で入力してください。");return;}
            applying=true;apply.Enabled=close.Enabled=engine.Enabled=port.Enabled=false;
            var kind=engine.SelectedIndex==1?"mysql57":"mysql80";DatabasePortResult? result=null;string? failure=null;
            status.Text=T("正在应用，请稍候…","Applying, please wait…","適用しています。しばらくお待ちください…");
            try
            {
                await Work(async()=>{
                    using var operation=Runtime.Lock(settings);runtime.Adopt();
                    try{result=await runtime.ChangeDatabasePortAsync(kind,wanted);}
                    catch(Exception error){failure=error.Message;}
                });
            }
            finally
            {
                applying=false;
                if(!dialog.IsDisposed)
                {
                    apply.Enabled=close.Enabled=engine.Enabled=port.Enabled=true;
                    status.Text=failure!=null?T("未应用：","Not applied: ","適用できませんでした：")+failure
                        :result==null?T("未完成，请查看运行日志。","Not completed; see the runtime log.","完了できませんでした。ログを確認してください。")
                        :!result.Changed?T("端口没有变化。","The port was unchanged.","ポートは変更されていません。")
                        :result.Manual.Count>0?T($"端口已改为 {result.Port}。以下项目请手动检查连接：",$"Port changed to {result.Port}. Review these project connections: ",$"ポートを {result.Port} に変更しました。次の接続を確認してください：")+string.Join(", ",result.Manual)
                        :T($"端口已改为 {result.Port}，连接验证通过；同步 {result.Updated.Count} 个项目。",$"Port changed to {result.Port} and verified; {result.Updated.Count} project configuration(s) updated.",$"ポートを {result.Port} に変更し、接続確認と {result.Updated.Count} 件の更新が完了しました。");
                    engine.Items.Clear();engine.Items.AddRange([$"MySQL 8.0 · 127.0.0.1:{settings.Mysql80Port}",$"MySQL 5.7 · 127.0.0.1:{settings.Mysql57Port}"]);engine.SelectedIndex=kind=="mysql57"?1:0;
                    RefreshPort();RefreshState();
                }
            }
        };
        RefreshPort();
        dialog.FormClosing+=(_,e)=>{if(applying)e.Cancel=true;};
        Palette.Show(dialog,this);
    }
}
