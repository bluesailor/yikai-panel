namespace YikaiLocal;

public sealed partial class MainForm
{
    void Build()
    {
        building=true;CancelDirectoryMeasure();SuspendLayout();
        if(Selected!=null)selectedId=Selected.Id;
        var query=search?.Text??"";
        foreach(Control old in Controls.Cast<Control>().ToArray())old.Dispose();actions.Clear();
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(56)));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(34)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(layout);

        var toolbar=new TableLayoutPanel{Name="environmentToolbar",Dock=DockStyle.Fill,BackColor=Palette.Surface,ColumnCount=2,RowCount=1,Padding=new Padding(16,10,18,10),Margin=Padding.Empty};toolbar.RowStyles.Add(new RowStyle(SizeType.Percent,100));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Px(154)));layout.Controls.Add(toolbar,0,0);
        var commands=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};toolbar.Controls.Add(commands,0,0);
        int CommandWidth(Button button)=>ToolbarButtonWidth(button);
        Button Command(string name,string text,string icon,Action action){var button=B(text,action,icon:icon);button.Name=name;button.Width=CommandWidth(button);button.Margin=new Padding(0,0,4,0);button.BackColor=Palette.Surface;button.FlatAppearance.BorderSize=0;commands.Controls.Add(button);return button;}
        Command("startEnvironment",T("全部启动","Start all","すべて起動"),"play",()=>_ = Work(async()=>{foreach(var s in settings.Sites)s.Enabled=true;settings.Save();await runtime.StartAsync();}));
        Command("stopEnvironment",T("全部停止","Stop all","すべて停止"),"stop",()=>_ = Work(()=>runtime.StopAsync()));
        var separator=new Panel{Width=1,Height=24,BackColor=Palette.Divider,Margin=new Padding(8,6,12,0)};commands.Controls.Add(separator);
        Command("syncDomains",T("同步域名","Sync domains","ドメイン同期"),"sync",()=>_ = SyncHosts());
        var tools=Command("environmentTools",T("工具","Tools","ツール"),"tool",()=>{});AttachEnvironmentTools(tools);
        var configuration=Command("environmentConfig",T("配置","Settings","設定"),"settings",()=>{});AttachEnvironmentConfig(configuration);
        void Auxiliary(string name,string text,string icon,Action action){var b=B(text,action,icon:icon);b.Name=name;b.Font=new Font(Font.FontFamily,Fs(9f));b.Width=CommandWidth(b);b.FlatAppearance.BorderSize=0;b.Margin=new Padding(0,0,4,0);commands.Controls.Add(b);}
        Auxiliary("upgradePanel",T("升级","Update","更新"),"upgrade",ShowUpgrade);Auxiliary("aboutPanel",T("关于","About","情報"),"info",ShowAbout);
        toolbarButtons.Clear();foreach(var button in commands.Controls.OfType<Button>())toolbarButtons.Add((button,button.Text));
        commands.SizeChanged+=(_,_)=>FitToolbar(commands);
        language=new Choice{Name="language",Dock=DockStyle.Fill,Margin=Padding.Empty};language.Items.AddRange(["简体中文","English","日本語"]);language.SelectedIndex=settings.Language=="en"?1:settings.Language=="ja"?2:0;toolbar.Controls.Add(language,1,0);
        language.SelectedIndexChanged+=(_,_)=>{if(building)return;var code=language.SelectedIndex==1?"en":language.SelectedIndex==2?"ja":"zh";BeginInvoke(()=>{settings.Language=code;settings.Save();Build();UpdateTray();});};

        var split=CreateProjectSplit();layout.Controls.Add(split,0,1);
        var left=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Palette.Surface,Padding=new Padding(18,16,18,16),ColumnCount=1,RowCount=5,Margin=Padding.Empty};
        foreach(var h in new[]{64,34,76})left.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(h)));left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(58)));split.Panel1.Controls.Add(left);
        left.Controls.Add(Branding.CreateHeader(),0,0);summary=L("",10,true);left.Controls.Add(summary,0,1);
        search=new TextBox{Name="projectSearch",Text=query,PlaceholderText=T("搜索项目或域名","Search projects or domains","プロジェクトを検索")};
        var searchArea=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=Padding.Empty};searchArea.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(42)));searchArea.RowStyles.Add(new RowStyle(SizeType.Percent,100));searchArea.Controls.Add(new InputFrame(search){Dock=DockStyle.Top,Height=Px(36),Margin=Padding.Empty},0,0);
        var filter=new CheckBox{Name="starFilter",Text=T("仅看星标","Starred only","スターのみ"),Dock=DockStyle.Fill,Checked=starredOnly,Font=new Font(Font.FontFamily,Fs(10f)),Margin=Padding.Empty};filter.CheckedChanged+=(_,_)=>{starredOnly=filter.Checked;PopulateProjects();};searchArea.Controls.Add(filter,0,1);left.Controls.Add(searchArea,0,2);search.TextChanged+=(_,_)=>PopulateProjects();
        projects=new ListBox{Name="projects",Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=Px(76),IntegralHeight=false,BackColor=Palette.Surface,Margin=new Padding(0,8,0,12)};
        projects.DrawItem+=DrawProject;projects.SelectedIndexChanged+=(_,_)=>{if(!building){selectedId=Selected?.Id;RefreshState();}};left.Controls.Add(projects,0,3);AttachProjectMenu();AttachProjectStars();
        // 左栏底部是唯一的添加入口：点开菜单里既有新建/接入，也有“扫描目录快速导入”批量添加（见 AttachAddProjectMenu）。
        // 末尾的 ▾ 是提示“这里点开是菜单”，不是直接进新建窗口。
        var add=(ThemeButton)B(T("新建 / 接入项目 ▾","Add project ▾","新規 / 接続 ▾"),()=>{ },primary:true,icon:"add");add.Name="addProject";add.Dock=DockStyle.Bottom;add.Height=Px(48);add.Margin=Padding.Empty;add.CenterContent=true;add.CornerRadius=8f;add.Font=new Font(Font.FontFamily,Fs(10.5f),FontStyle.Bold);left.Controls.Add(add,0,4);AttachAddProjectMenu(add);

        // 顶部不再放“本地项目”标题和项目数量：左栏已有名称和“项目 N · 运行 N”，这里只留一行环境状态。
        var right=new TableLayoutPanel{Name="projectWorkspace",AutoScroll=true,Dock=DockStyle.Fill,BackColor=Palette.Workspace,Padding=new Padding(24,16,24,12),ColumnCount=1,RowCount=4,Margin=Padding.Empty};
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(52)));right.RowStyles.Add(new RowStyle(SizeType.AutoSize));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(28)));split.Panel2.Controls.Add(right);
        var banner=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=new Padding(0,0,0,10)};banner.RowStyles.Add(new RowStyle(SizeType.Absolute));banner.RowStyles.Add(new RowStyle(SizeType.Absolute));
        status=L("",10);status.Name="projectRunStatus";status.Margin=Padding.Empty;
        progress=L(T("各项目独立启停，共享数据库服务","Projects share database services.","プロジェクト間で DB サービスを共有。"),9);progress.Name="projectRunProgress";progress.Margin=Padding.Empty;progress.ForeColor=Muted;
        void FitStatusRows(){int HeightFor(Label label)=>TextRenderer.MeasureText("Ag 项目起動",label.Font,new Size(10000,1000),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Height+4;banner.RowStyles[0].Height=HeightFor(status);banner.RowStyles[1].Height=HeightFor(progress);right.RowStyles[0].Height=banner.RowStyles[0].Height+banner.RowStyles[1].Height+banner.Margin.Vertical;}
        banner.Controls.Add(status,0,0);banner.Controls.Add(progress,0,1);right.Controls.Add(banner,0,0);FitStatusRows();status.FontChanged+=(_,_)=>FitStatusRows();progress.FontChanged+=(_,_)=>FitStatusRows();banner.DpiChangedAfterParent+=(_,_)=>FitStatusRows();
        // 卡片高度随内容变化；信息行改成“标题 + 内容”两列，按钮区限制在固定宽度内，宽屏下不会散开。
        var card=new TableLayoutPanel{Name="projectCard",Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MaximumSize=new Size(Px(1040),0),BackColor=Palette.Card,Padding=new Padding(24,20,24,20),RowCount=8,ColumnCount=1,Margin=Padding.Empty};card.Paint+=(_,e)=>{using var edge=new Pen(Palette.Divider);e.Graphics.DrawRectangle(edge,0,0,card.Width-1,card.Height-1);};card.Resize+=(_,_)=>card.Invalidate();
        foreach(var h in new[]{36,32,44,104,84,48,48,48})card.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(h)));projectCard=card;right.Controls.Add(card,0,1);
        heading=new ProjectHeadingLabel{Name="projectHeading",Font=new Font(Font.FontFamily,Fs(13f),FontStyle.Bold),ForeColor=Ink,Dock=DockStyle.Fill};address=L("",10.5f);address.Name="projectAddress";address.ForeColor=Blue;address.Cursor=Cursors.Hand;
        // 网址看着像链接就应该能点：点它直接打开网站（悬停时加下划线）。
        address.Click+=(_,_)=>{if(!busy&&Selected!=null)_ = OpenSite(false);};
        address.MouseEnter+=(_,_)=>ReplaceFont(address,new Font(address.Font,FontStyle.Underline));
        address.MouseLeave+=(_,_)=>ReplaceFont(address,new Font(address.Font,FontStyle.Regular));
        serviceTips.SetToolTip(address,T("点击打开网站","Click to open the website","クリックでサイトを開く"));
        chips=new ProjectChips{Name="projectChips",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,Fs(9f)),BackColor=Palette.Card,ForeColor=Ink,Margin=new Padding(0,2,0,8)};
        info=new ProjectInfoLabel{Name="projectInfo",Font=new Font(Font.FontFamily,Fs(9.5f)),Dock=DockStyle.Fill,ForeColor=Ink,CaptionWidth=CaptionColumn};
        card.Controls.Add(heading,0,0);card.Controls.Add(address,0,1);card.Controls.Add(chips,0,2);card.Controls.Add(info,0,3);
        AddProjectDetails(card,4);
        TableLayoutPanel Row(int index){var row=new TableLayoutPanel{Dock=DockStyle.Fill,MaximumSize=new Size(Px(CardContentWidth),0),ColumnCount=3,RowCount=1,Margin=Padding.Empty};for(int i=0;i<3;i++)row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));row.RowStyles.Add(new RowStyle(SizeType.Percent,100));row.ControlAdded+=(_,e)=>{e.Control!.Dock=DockStyle.Fill;e.Control.Margin=new Padding(0,0,16,10);e.Control.MaximumSize=new Size(Px(300),Px(40));};card.Controls.Add(row,0,index);return row;}
        var first=Row(5);open=B(T("打开网站","Open website","サイトを開く"),()=>_ = OpenSite(false),icon:"globe");admin=B(T("打开后台","Open admin","管理画面"),()=>_ = OpenSite(true),icon:"login");database=B(T("管理数据库","Database","データベース"),()=>_ = WithSite(async s=>{await runtime.StartSiteAsync(s);Open(runtime.DatabaseUrl(s));}),icon:"database");first.Controls.AddRange([open,admin,database]);
        var second=Row(6);start=B(T("启动此项目","Start project","サイトを起動"),()=>_ = WithSite(runtime.StartSiteAsync),icon:"play");stop=B(T("停止此项目","Stop project","サイトを停止"),()=>_ = WithSite(runtime.StopSiteAsync),icon:"stop");folder=B(T("打开目录","Open folder","フォルダー"),()=>{if(Selected is {} s)Open(s.Directory);},icon:"folder-open");second.Controls.AddRange([start,stop,folder]);
        var third=Row(7);edit=B(T("项目设置","Project settings","サイト設定"),()=>{if(Selected is {} s)ProjectDialog(s);},icon:"settings");rewrite=B(T("伪静态配置","URL rewrite","リライトルール"),()=>{if(Selected is {} s)RewriteDialog(s);},icon:"settings");remove=B(T("从列表移除","Remove","一覧から外す"),()=>_ = RemoveProject(),icon:"remove");third.Controls.AddRange([edit,rewrite,remove]);
        var foot=L(T(PanelUpdate.CurrentVersion+" · 本机运行 · 文件与数据库保留在本机",PanelUpdate.CurrentVersion+" · Local runtime · Your data stays here",PanelUpdate.CurrentVersion+" · ローカル実行 · データはこの PC に保存"),9);foot.ForeColor=Muted;right.Controls.Add(foot,0,3);
        var strip=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Palette.Surface,ColumnCount=5,RowCount=1,Margin=Padding.Empty,Padding=new Padding(6,0,6,0)};strip.RowStyles.Add(new RowStyle(SizeType.Percent,100));for(var i=0;i<5;i++)strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));
        Label Item(){var label=L("",8.5f);label.Margin=new Padding(6,0,6,0);strip.Controls.Add(label);return label;}webState=Item();phpState=Item();mysql80State=Item();mysql57State=Item();managerState=Item();layout.Controls.Add(strip,0,2);
        AttachServiceMenu(webState,"web");AttachServiceMenu(phpState,"php");AttachServiceMenu(mysql80State,"mysql80");AttachServiceMenu(mysql57State,"mysql57");AttachServiceMenu(managerState,"dbpage");
        serviceTips.SetToolTip(stop,T("只停止当前项目，其他项目保持运行","Stop this project; keep others running","選択したプロジェクトのみ停止"));serviceTips.SetToolTip(remove,T("仅从列表移除，保留文件与数据库","Keep project files and databases","ファイルと DB は保持"));
        PopulateProjects();building=false;RefreshState();Palette.Apply(this);backendSettings.BackColor=directoryRefresh.BackColor=Palette.Card;ResumeLayout(true);RestoreSidebarWidth();
    }
}
