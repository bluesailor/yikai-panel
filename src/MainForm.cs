
using System.Diagnostics;

namespace YikaiLocal;

public sealed partial class MainForm : Form
{
    readonly Settings settings;
    readonly Runtime runtime;
    readonly bool renderOnly;
    readonly NotifyIcon tray=new();
    readonly System.Windows.Forms.Timer timer=new(){Interval=1500};
    readonly List<Button> actions=[];
    ListBox projects=null!;
    TextBox search=null!;
    Choice language=null!;
    ProjectHeadingLabel heading=null!;
    Label status=null!,address=null!,progress=null!,summary=null!;
    ProjectInfoLabel info=null!;
    ProjectChips chips=null!;
    TableLayoutPanel projectCard=null!;
    // 卡片内容宽度：按钮和右侧小按钮都排在这个宽度内，宽屏下不会拉散；标题列宽让几行信息对齐。
    const int CardContentWidth=960,CaptionColumn=78;
    Button rewrite=null!,start=null!,stop=null!,open=null!,admin=null!,database=null!,folder=null!,edit=null!,remove=null!;
    readonly ToolTip serviceTips=new();
    Label webState=null!,phpState=null!,mysql80State=null!,mysql57State=null!,managerState=null!;
    string? selectedId;
    string projectListState="";
    bool busy,exit,building;
    static Color Ink=>Palette.Text;
    static Color Muted=>Palette.Secondary;
    static Color Blue=>Palette.Link;
    static Color Canvas=>Palette.Canvas;
    // 界面字号：所有文字都按“标准 10 磅”的比例缩放。
    float Fs(float size)=>size*settings.FontSize/10f;
    // 固定尺寸（行高、按钮、列表行）按“标准字号、100% 缩放”写，这里换算成实际像素：
    // 跟着界面字号走，也跟着 Windows 显示缩放走。窗体没有开启 WinForms 自动缩放，文字按磅值会随缩放变大，
    // 固定像素却不会，150% 缩放下就会出现文字被行高切掉（比如卡片上的 PHP 标签底边）。
    int Px(int value)=>(int)Math.Round(value*settings.FontSize/10f*DeviceDpi/96f);
    Site? Selected=>projects?.SelectedItem as Site;
    string T(string zh,string en,string ja)=>settings.Language=="en"?en:settings.Language=="ja"?ja:zh;
    public MainForm(Settings settings,Runtime runtime,bool renderOnly=false,bool startEnvironment=true)
    {
        this.settings=settings;this.runtime=runtime;this.renderOnly=renderOnly;
        Palette.Use(settings.Theme);Palette.LayoutScale=settings.FontSize/10f;
        Text="易开面板 · "+PanelUpdate.CurrentVersion;Icon=Branding.LoadIcon();Size=new Size(Px(1320),Px(820));MinimumSize=new Size(Px(1120),Px(740));StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Microsoft YaHei UI",settings.FontSize);ForeColor=Ink;BackColor=Canvas;AutoScaleMode=AutoScaleMode.Dpi;
        Build();runtime.Progress+=OnProgress;
        timer.Tick+=(_,_)=>{if(!busy)runtime.Adopt();RefreshState();};timer.Start();
        tray.Icon=Icon;tray.Text="易开面板";tray.Visible=!renderOnly;tray.DoubleClick+=(_,_)=>ShowPanel();UpdateTray();
        if(!renderOnly){Load+=(_,_)=>RestoreWindowPlacement();Microsoft.Win32.SystemEvents.UserPreferenceChanged+=OnSystemPreference;}
        FormClosing+=(_,e)=>{if(!renderOnly)SaveWindowPlacement();if(!exit&&!renderOnly&&settings.MinimizeToTray&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
        Shown+=async(_,_)=>{if(!renderOnly&&startEnvironment)await Work(()=>runtime.StartAsync());};
    }
    // 主题设为“跟随系统”时，Windows 改了应用模式就跟着换。
    void OnSystemPreference(object? sender,Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if(settings.Theme!="system"||e.Category!=Microsoft.Win32.UserPreferenceCategory.General||IsDisposed||!IsHandleCreated)return;
        if(Palette.SystemDark()==Palette.IsDark)return;
        BeginInvoke(()=>{if(IsDisposed)return;Palette.Use("system");Build();UpdateTray();});
    }
    // 换字体：WinForms 的 Font 属性遇到“相等”的字体会直接忽略赋值（旧对象继续在用），
    // 这时再释放旧字体，控件手里就是一个已释放的字体，之后绘制或读 FontFamily 会报 “Parameter is not valid”。
    // 所以只在真的换上新字体后才释放旧的；没换上就释放刚建的新字体。只用于控件自己独占的字体。
    static void ReplaceFont(Control control,Font font)
    {
        var old=control.Font;control.Font=font;
        if(ReferenceEquals(control.Font,old))font.Dispose();else old.Dispose();
    }
    void OnProgress(string value){if(IsHandleCreated&&!IsDisposed)BeginInvoke(()=>{if(!progress.IsDisposed)progress.Text=value;});}
    Label L(string text,float size=10,bool bold=false)=>new IconLabel(){Text=text,Font=new Font(Font.FontFamily,Fs(size),bold?FontStyle.Bold:FontStyle.Regular),ForeColor=Ink,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
    Button B(string text,Action click,bool primary=false,string icon="")
    {
        var b=new ThemeButton{Text=text,Size=new Size(Px(224),Px(36)),FlatStyle=FlatStyle.Flat,BackColor=primary?Palette.Accent:Palette.ButtonFill,ForeColor=primary?Palette.OnAccent:Ink,Cursor=Cursors.Hand,Margin=new Padding(0,0,8,8)};
        if(icon!=""){b.Font=new Font(Font.FontFamily,Fs(10f));AttachButtonIcon(b,icon);}
        b.AccessibleName=text;serviceTips.SetToolTip(b,text);
        b.FlatAppearance.BorderColor=Palette.Border;b.Click+=(_,_)=>click();actions.Add(b);return b;
    }
    static void AttachButtonIcon(Button button,string icon)
    {
        void Refresh(){var old=button.Image;button.Image=UiIcons.Create(icon,button.Enabled?button.ForeColor:Palette.Secondary,(int)Math.Round(16*button.DeviceDpi/96f));old?.Dispose();}
        button.ImageAlign=ContentAlignment.MiddleLeft;button.TextImageRelation=TextImageRelation.ImageBeforeText;
        Refresh();button.EnabledChanged+=(_,_)=>Refresh();button.ForeColorChanged+=(_,_)=>Refresh();button.DpiChangedAfterParent+=(_,_)=>Refresh();button.Disposed+=(_,_)=>button.Image?.Dispose();
    }
    void PopulateProjects()
    {
        if(projects==null)return;
        var id=Selected?.Id??selectedId;projects.BeginUpdate();projects.Items.Clear();
        foreach(var s in settings.Sites.Where(s=>(!starredOnly||s.Starred)&&(s.ToString()+" "+s.Domain+" "+s.Directory).Contains(search.Text,StringComparison.OrdinalIgnoreCase)))projects.Items.Add(s);
        projects.SelectedItem=projects.Items.Cast<Site>().FirstOrDefault(s=>s.Id==id);if(projects.SelectedIndex<0&&projects.Items.Count>0)projects.SelectedIndex=0;projects.EndUpdate();RefreshState();
    }
    void DrawProject(object? sender,DrawItemEventArgs e)
    {
        if(e.Index<0)return;var s=(Site)projects.Items[e.Index];var selected=(e.State&DrawItemState.Selected)!=0;
        using(var sidebar=new SolidBrush(Palette.Surface))e.Graphics.FillRectangle(sidebar,e.Bounds);
        // 选中项：侧栏上的白色圆角卡片 + 细边框
        var rowBack=Palette.Surface;
        if(selected)
        {
            var card=Rectangle.Inflate(e.Bounds,-2,-3);card.Width-=1;card.Height-=1;rowBack=Palette.Selected;
            var smoothing=e.Graphics.SmoothingMode;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var shape=RoundedBox(card,8))using(var bg=new SolidBrush(Palette.Selected))using(var edge=new Pen(Palette.Divider)){e.Graphics.FillPath(bg,shape);e.Graphics.DrawPath(edge,shape);}
            e.Graphics.SmoothingMode=smoothing;
        }
        var size=Px(34);var avatar=new Rectangle(e.Bounds.X+Px(12),e.Bounds.Y+(e.Bounds.Height-size)/2,size,size);
        DrawProjectAvatar(e.Graphics,avatar,s,runtime.IsRunning(s),rowBack,Font.FontFamily);
        var textX=avatar.Right+Px(12);
        using var boldFont=new Font(Font,FontStyle.Bold);TextRenderer.DrawText(e.Graphics,s.ToString(),boldFont,new Rectangle(textX,e.Bounds.Y+Px(9),e.Bounds.Right-Px(44)-textX,Px(30)),Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);
        var line=new Rectangle(textX,e.Bounds.Y+Px(39),e.Bounds.Right-Px(8)-textX,Px(26));var right=line.Right;
        if(HttpsBadge(s) is {} badge)right=DrawRowBadge(e.Graphics,right,line,badge.Text,badge.Color,rowBack,Font.FontFamily,Fs(8f));
        TextRenderer.DrawText(e.Graphics,s.Domain+"  ·  PHP "+s.Php,Font,new Rectangle(line.X,line.Y,Math.Max(20,right-line.X),line.Height),Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);
        DrawProjectStar(e.Graphics,e.Bounds,s.Starred);
        if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
    }
    void RefreshState()
    {
        if(building||IsDisposed)return;
        var count=settings.Sites.Count(runtime.IsRunning);summary.Text=T($"项目 {settings.Sites.Count} · 运行 {count}",$"Projects {settings.Sites.Count} · Running {count}",$"プロジェクト {settings.Sites.Count} · 起動 {count}");
        status.Text=busy?T("正在处理…","Working…","処理中…"):T("环境运行中","Environment running","環境は稼働中");
        var s=Selected;heading.Text=s?.ToString()??(settings.Sites.Count==0?T("添加你的第一个项目","Add your first project","最初のプロジェクトを追加"):T("选择左侧项目","Select a project","プロジェクトを選択"));address.Text=s==null?"":runtime.SiteUrl(s);
        var cms=s==null?null:CmsVersion.Detect(s.Directory);heading.Badge=cms==null?"":"YikaiCMS "+cms;serviceTips.SetToolTip(heading,cms==null?"":T("YikaiCMS 版本：","YikaiCMS version: ","YikaiCMS バージョン：")+cms);
        UpdateProjectFacts(s);
        foreach(var b in new[]{open,admin,database,folder,edit,remove,rewrite,start,stop})b.Enabled=!busy&&s!=null;
        if(s!=null){start.Enabled=!busy&&!runtime.IsRunning(s);stop.Enabled=!busy&&runtime.IsRunning(s);admin.Enabled=!busy;database.Enabled=!busy&&s.Database!="none";}
        SetService(webState,Runtime.WebServerName(settings.WebServer),settings.WebServer,s?.HttpPort??settings.DbManagerPort,settings.Sites.Any(p=>p.Enabled));
        RefreshPhpService();
        SetService(mysql80State,"MySQL 8.0","mysql80",settings.Mysql80Port,settings.MysqlActive=="mysql80"||settings.Sites.Any(p=>p.Enabled&&p.Database=="mysql80"));
        SetService(mysql57State,"MySQL 5.7","mysql57",settings.Mysql57Port,settings.MysqlActive=="mysql57"||settings.Sites.Any(p=>p.Enabled&&p.Database=="mysql57"));
        SetService(managerState,T("数据库页面","DB manager","DB 管理"),"php-db",settings.DbManagerPort,true);
        if(!busy&&!runtime.AnyRunning){status.Text=T("环境已停止","Environment stopped","環境は停止中");status.ForeColor=Muted;}
        else if(!busy&&!runtime.Running){status.Text=T("部分服务未运行，请检查状态栏","Some services are stopped; check the status bar","一部のサービスが停止中です");status.ForeColor=Palette.Warning;}
        else status.ForeColor=Ink;
        SetIcon(status,busy?"sync":!runtime.AnyRunning?"stop":runtime.Running?"check":"warning",status.ForeColor);
        // 定时刷新只在列表内容或运行状态变化时重画，避免每 1.5 秒整表重绘造成闪烁。
        var listState=string.Join("|",projects.Items.Cast<Site>().Select(p=>p.Id+""+p+""+p.Domain+""+p.Php+""+p.Starred+""+runtime.IsRunning(p)+""+p.Https));
        if(listState!=projectListState){projectListState=listState;projects.Invalidate();}
        RefreshProjectDetails();
    }
    void SetIcon(Label label,string icon,Color color)
    {
        var key=icon+color.ToArgb();if(label.Tag as string==key)return;
        var target=(IconLabel)label;var old=target.StatusIcon;target.StatusIcon=UiIcons.Create(icon,color,16);old?.Dispose();label.Padding=new Padding(22,0,0,0);label.Tag=key;label.Invalidate();
    }
    void SetService(Label label,string name,string key,int port,bool expected)
    {
        var running=runtime.ServiceRunning(key)&&(key!="php-db"||runtime.ServiceRunning(runtime.WebKey));
        var warning=!running&&expected&&runtime.AnyRunning&&!busy;
        var state=running?T("运行","On","起動"):warning?T("未运行","Not running","未起動"):T("已停止","Off","停止中");
        label.Text=$"{name} · {state}"+(running?$" :{port}":"");
        label.ForeColor=running?Palette.Success:warning?Palette.Warning:Muted;
        SetIcon(label,running?"dot":warning?"warning":"stop",label.ForeColor);
        serviceTips.SetToolTip(label,$"{name}\n{state}\n127.0.0.1:{port}\nPID: {runtime.ServicePid(key)?.ToString()??"—"}");
    }
    async Task Work(Func<Task> action)
    {
        if(busy)return;busy=true;actions.ForEach(b=>b.Enabled=false);projects.Enabled=search.Enabled=language.Enabled=false;RefreshState();
        try{using var operation=Runtime.Lock(settings);runtime.Adopt();await action();progress.Text=T("已完成","Done","完了");}
        catch(Exception e){progress.Text=e.Message;MessageBox.Show(this,e.Message,"易开面板",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally{busy=false;actions.ForEach(b=>b.Enabled=true);projects.Enabled=search.Enabled=language.Enabled=true;RefreshState();}
    }
    Task WithSite(Func<Site,Task> action)=>Selected is {} s?Work(()=>action(s)):Task.CompletedTask;
    Task OpenSite(bool backend)=>backend?OpenBackend():WithSite(async s=>{await runtime.StartSiteAsync(s);Open(runtime.SiteUrl(s));});
    static void Open(string target)=>Process.Start(new ProcessStartInfo(target){UseShellExecute=true});
    void ShowPanel(){Show();ShowInTaskbar=true;WindowState=FormWindowState.Normal;Activate();BringToFront();
        // 由另一个进程（第二次启动面板）触发时，Windows 可能不给前台权限：短暂置顶一次把它带到前面
        TopMost=true;TopMost=false;}
    // 第二次启动面板时，新进程通过命名事件通知这里把窗口调出来，而不是弹“已在托盘运行”的对话框。
    // 后台线程等待信号，再切回 UI 线程显示窗口（窗口句柄尚未创建时先忽略）。
    public void AcceptShowSignal(EventWaitHandle signal)
    {
        var thread=new Thread(()=>{
            while(true)
            {
                if(!signal.WaitOne(500)){if(IsDisposed)return;continue;}
                try{if(IsDisposed)return;if(IsHandleCreated)BeginInvoke(ShowPanel);}
                catch(ObjectDisposedException){return;}
                catch(InvalidOperationException){return;}
            }
        }){IsBackground=true,Name="YikaiPanel-ShowSignal"};
        thread.Start();
    }
    void UpdateTray(){var old=tray.ContextMenuStrip;var menu=new ContextMenuStrip{ForeColor=Palette.Text};menu.Items.Add(T("打开面板","Show panel","パネルを開く"),null,(_,_)=>ShowPanel());menu.Items.Add(T("退出并停止环境","Exit and stop","停止して終了"),null,async(_,_)=>{await Work(()=>runtime.StopAsync());if(runtime.AnyRunning)return;exit=true;Close();});tray.ContextMenuStrip=menu;old?.Dispose();}
    Task SyncHosts()=>Work(async()=>{var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};info.ArgumentList.Add("--root");info.ArgumentList.Add(settings.Root);info.ArgumentList.Add("--sync-hosts");using var p=Process.Start(info)??throw new IOException("Cannot start domain helper.");await p.WaitForExitAsync();if(p.ExitCode!=0)throw new IOException(File.ReadAllText(Path.Combine(settings.Root,"logs","panel-last-error.txt")));});
    async Task RemoveProject()
    {
        if(Selected is not {} s)return;
        if(MessageBox.Show(this,T($"从列表移除 {s}？\n项目文件和数据库会保留。",$"Remove {s} from this list?\nFiles and databases will be kept.",$"{s} を一覧から外しますか？\nファイルとデータベースは残ります。"),T("移除项目","Remove project","プロジェクトを外す"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        await Work(async()=>{await runtime.StopSiteAsync(s);settings.Sites.Remove(s);settings.Save();if(runtime.WebServerRunning)await runtime.ReloadWebServer();selectedId=null;PopulateProjects();});
    }
    protected override void Dispose(bool disposing){if(disposing){CancelDirectoryMeasure();if(!renderOnly)Microsoft.Win32.SystemEvents.UserPreferenceChanged-=OnSystemPreference;runtime.Progress-=OnProgress;timer.Dispose();serviceTips.Dispose();tray.ContextMenuStrip?.Dispose();tray.Dispose();}base.Dispose(disposing);}
}
