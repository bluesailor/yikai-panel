using System.Reflection;
using YikaiLocal;

// 项目列表徽标（HTTPS / 到期）、项目卡片状态行、工作区去重与到期提醒输入的界面检查。不启动任何服务。
static class Program
{
    static readonly List<string> Lines=[];
    static IEnumerable<Control> Desc(Control root){foreach(Control c in root.Controls){yield return c;foreach(var n in Desc(c))yield return n;}}
    static void Check(bool ok,string text){Lines.Add((ok?"PASS ":"FAIL ")+text);Console.WriteLine((ok?"PASS ":"FAIL ")+text);if(!ok)Environment.ExitCode=1;}
    static Type PaletteType=>typeof(Site).Assembly.GetType("YikaiLocal.Palette")!;
    static void UseTheme(string mode)=>PaletteType.GetMethod("Use",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,[mode]);
    static bool DarkTheme=>(bool)PaletteType.GetProperty("IsDark",BindingFlags.Public|BindingFlags.Static)!.GetValue(null)!;
    static double Luminance(Color c){static double F(double v)=>v<=0.03928?v/12.92:Math.Pow((v+0.055)/1.055,2.4);return 0.2126*F(c.R/255.0)+0.7152*F(c.G/255.0)+0.0722*F(c.B/255.0);}
    static double Contrast(Color a,Color b){var x=Luminance(a);var y=Luminance(b);return Math.Round((Math.Max(x,y)+0.05)/(Math.Min(x,y)+0.05),2);}
    static Color Hue(string name)=>PaletteType.GetProperty(name,BindingFlags.Public|BindingFlags.Static) is {} property?(Color)property.GetValue(null)!:(Color)PaletteType.GetField(name,BindingFlags.Public|BindingFlags.Static)!.GetValue(null)!;
    static (string Text,Color Color)? Badge(MainForm form,string name,Site site)
        =>(ValueTuple<string,Color>?)typeof(MainForm).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,[site]);
    delegate bool EnumProc(IntPtr hwnd,IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,IntPtr extra);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out int pid);
    static ToolStripDropDown? OpenDropDown()
    {
        var list=new List<IntPtr>();var pid=Environment.ProcessId;
        EnumWindows((h,_)=>{GetWindowThreadProcessId(h,out var p);if(p==pid)list.Add(h);return true;},IntPtr.Zero);
        foreach(var handle in list)if(Control.FromHandle(handle) is ToolStripDropDown d&&d.Visible)return d;
        return null;
    }
    static bool Near(Color a,Color b)=>Math.Abs(a.R-b.R)<=6&&Math.Abs(a.G-b.G)<=6&&Math.Abs(a.B-b.B)<=6;

    // 窗口布局检查：可操作控件都在窗口内；按钮文字（加图标和内边距）放得下；标签不被行高截断（会换行的按实际宽度算）。
    static void CheckLayout(string name,Form dialog,string output)
    {
        var client=dialog.ClientSize;
        var outside=Desc(dialog).Where(c=>c.Visible&&c is Button or TextBox or Choice or CheckBox or ListBox or InputFrame or DataGridView)
            .Where(c=>{var r=dialog.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));return r.Left<-1||r.Top<-1||r.Right>client.Width+1||r.Bottom>client.Height+1;})
            .Select(c=>c.Name.Length>0?c.Name:c.Text).ToList();
        Check(outside.Count==0,$"{name}: controls inside the window {string.Join(" | ",outside)}");
        // 控件还必须在父容器内：footer 行高按“按钮+外边距+内边距”算错时，按钮底部会被父面板裁掉，
        // “在窗口内”这条检查发现不了（父面板本身在窗口内）。
        var clipped=Desc(dialog).Where(c=>c.Visible&&c is Button or TextBox or Choice or CheckBox or ListBox or InputFrame or DataGridView&&c.Parent is not null&&c.Parent is not Form)
            .Where(c=>{var r=c.Parent!.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));return r.Bottom>c.Parent.ClientSize.Height+1||r.Top<-1||r.Right>c.Parent.ClientSize.Width+1||r.Left<-1;})
            .Select(c=>{var r=c.Parent!.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));return $"{c.Name}{(c.Name.Length>0?"":"("+c.Text+")")} bottom={r.Bottom}>{c.Parent.ClientSize.Height}";}).ToList();
        Check(clipped.Count==0,$"{name}: controls not clipped by their container {string.Join(" | ",clipped)}");
        var cramped=Desc(dialog).OfType<Button>().Where(b=>b.Visible&&b.Text.Length>1)
            .Where(b=>TextRenderer.MeasureText(b.Text,b.Font,new Size(4000,100),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Width+(int)Math.Round(((b.Image!=null?24:0)+16)*b.DeviceDpi/96f)>b.Width)
            .Select(b=>$"{b.Text}({b.Width})").ToList();
        Check(cramped.Count==0,$"{name}: button text fits {string.Join(" | ",cramped)}");
        var cut=Desc(dialog).OfType<Label>().Where(l=>l.Visible&&l.Text.Length>0&&l is not ProjectInfoLabel).Where(l=>
        {
            var wraps=!l.AutoEllipsis&&!l.AutoSize;
            var needed=wraps?TextRenderer.MeasureText(l.Text,l.Font,new Size(Math.Max(1,l.Width-l.Padding.Horizontal),int.MaxValue),TextFormatFlags.WordBreak).Height:TextRenderer.MeasureText(l.Text.Split('\n')[0],l.Font).Height;
            return l.Height+1<needed;
        }).Select(l=>$"{l.Text.Split('\n')[0]}({l.Height})").ToList();
        Check(cut.Count==0,$"{name}: labels not cut {string.Join(" | ",cut)}");
        using var img=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,$"dialog-{name}.png"));
    }
    // 打开一个对话框、做布局检查、关闭。open 负责调用 MainForm 上的方法。
    static void Inspect(MainForm form,string dialogName,string label,Action open,string output,Action<Form>? extra=null)
    {
        bool done=false;
        using var timer=new System.Windows.Forms.Timer{Interval=150};
        timer.Tick+=(_,_)=>{
            var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Name==dialogName&&f.Visible);if(dialog==null)return;timer.Stop();
            for(var i=0;i<10;i++){Application.DoEvents();Thread.Sleep(15);}
            CheckLayout(label,dialog,output);extra?.Invoke(dialog);
            dialog.DialogResult=DialogResult.Cancel;dialog.Close();done=true;
        };
        timer.Start();open();
        var until=DateTime.UtcNow.AddSeconds(15);while(!done&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
        Check(done,$"{label}: opened and closed");
    }

    [STAThread]
    static void Main(string[] args)
    {
        var root=args[0];var output=args[1];Directory.CreateDirectory(output);
        ApplicationConfiguration.Initialize();
        // 夹具：0 号项目 HTTPS 正常，1 号项目不开 HTTPS，2 号项目启用 HTTPS 但证书文件缺失
        var seed=Settings.Load(root);var seedRuntime=new Runtime(seed);
        // 真实配置里的外观设置（字号、主题）会随使用变化，检查从标准字号、浅色开始
        seed.FontSize=10f;seed.Theme="light";seed.CodeFont="Consolas";seed.CodeFontSize=10f;
        seed.Sites[0].Https=true;seed.Sites[0].HttpsPort=8443;seed.Sites[0].CertificateSource="auto";
        seedRuntime.EnsureSiteCertificate(seed.Sites[0],true);
        seed.Sites[1].Https=false;
        seed.Sites[2].Https=true;seed.Sites[2].HttpsPort=8444;seed.Sites[2].CertificateSource="auto";
        var missing=seedRuntime.CertificatePaths(seed.Sites[2]);
        foreach(var file in new[]{missing.Certificate,missing.Key})if(File.Exists(file))File.Delete(file);
        seed.Save();
        var issued=seedRuntime.SiteCertificateInfo(seed.Sites[0])!;

        foreach(var lang in new[]{"zh","en","ja"})
        {
            var settings=Settings.Load(root);settings.Language=lang;
            var runtime=new Runtime(settings);
            using var form=new MainForm(settings,runtime,true,false){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
            form.Show();Application.DoEvents();
            var labels=Desc(form).OfType<Label>().ToList();
            Check(!labels.Any(l=>l.Text is "本地项目" or "Local projects" or "ローカルプロジェクト"),$"{lang} workspace no longer repeats the sidebar heading");
            var status=labels.Single(l=>l.Name=="projectRunStatus");
            Check(!status.Text.Any(char.IsDigit),$"{lang} workspace status has no project count: {status.Text}");

            var https=Badge(form,"HttpsBadge",settings.Sites[0]);
            Check(https is {Text:"HTTPS"} ok1&&ok1.Color==Hue("Success"),$"{lang} valid certificate shows a green HTTPS badge");
            var broken=Badge(form,"HttpsBadge",settings.Sites[2]);
            Check(broken is {Text:"HTTPS"} ok2&&ok2.Color==Hue("Brand"),$"{lang} missing certificate marks the HTTPS badge");
            Check(Badge(form,"HttpsBadge",settings.Sites[1])==null,$"{lang} project without HTTPS has no badge");

            var list=Desc(form).OfType<ListBox>().Single(l=>l.Name=="projects");
            var info=Desc(form).OfType<ProjectInfoLabel>().Single(l=>l.Name=="projectInfo");
            var chips=Desc(form).OfType<ProjectChips>().Single(c=>c.Name=="projectChips");
            string LastLine()=>info.Text.Split('\n')[^1];
            bool Warned()=>info.WarnLines.Contains(info.Text.Split('\n').Length-1);
            list.SelectedIndex=0;Application.DoEvents();
            Check(info.Text.Contains("HTTPS\t")&&info.Text.Contains(issued.NotAfter.ToString("yyyy-MM-dd"))&&info.WarnLines.Length==0,$"{lang} card shows the certificate date: {info.Text.Replace('\n','|').Replace('\t','=')}");
            list.SelectedIndex=1;Application.DoEvents();
            Check(!info.Text.Contains("HTTPS\t")&&info.WarnLines.Length==0,$"{lang} project without HTTPS has no certificate row");
            list.SelectedIndex=2;Application.DoEvents();
            Check(Warned()&&LastLine().Length>0,$"{lang} card warns about the missing certificate: {LastLine()}");
            list.SelectedIndex=0;Application.DoEvents();
            using(var paint=new Bitmap(Math.Max(1,chips.Width),Math.Max(1,chips.Height))){chips.DrawToBitmap(paint,new Rectangle(Point.Empty,chips.Size));}
            Check(chips.Count==3&&!chips.Truncated&&chips.Height>=TextRenderer.MeasureText("Ag",chips.Font).Height+(int)Math.Round(10*chips.DeviceDpi/96f)+2,$"{lang} card chips fit: {chips.Describe()} in {chips.Width}x{chips.Height}");

            // 目录和域名可以点开：悬停时变成手型光标，其他行不变
            using(var paint=new Bitmap(Math.Max(1,info.Width),Math.Max(1,info.Height))){info.DrawToBitmap(paint,new Rectangle(Point.Empty,info.Size));}
            var move=typeof(ProjectInfoLabel).GetMethod("OnMouseMove",BindingFlags.NonPublic|BindingFlags.Instance)!;
            var rows=info.Text.Split('\n').ToList();
            void Hover(int line,int x)=>move.Invoke(info,[new MouseEventArgs(MouseButtons.None,0,x,Math.Max(0,(info.Height-rows.Count*info.LineHeight)/2)+line*info.LineHeight+info.LineHeight/2,0)]);
            var folderLine=rows.FindIndex(l=>l.Contains('\t')&&l.Split('\t')[1].Contains(":\\"));
            var domainLine=folderLine+1;   // 顺序固定：数据库、目录、域名、HTTPS、到期
            var databaseLine=folderLine-1;
            Hover(folderLine,140);Check(info.Cursor==Cursors.Hand,$"{lang} folder row is clickable (line {folderLine})");
            Hover(domainLine,140);Check(info.Cursor==Cursors.Hand,$"{lang} domain row is clickable (line {domainLine})");
            if(databaseLine>=0){Hover(databaseLine,140);Check(info.Cursor==Cursors.Default,$"{lang} plain rows are not clickable (line {databaseLine})");}
            Hover(folderLine,info.Width-4);Check(info.Cursor==Cursors.Default,$"{lang} empty space after the text is not clickable");
            var addressLabel=Desc(form).OfType<Label>().Single(l=>l.Name=="projectAddress");
            Check(addressLabel.Cursor==Cursors.Hand&&addressLabel.Text.StartsWith("http"),$"{lang} address is a link: {addressLabel.Text}");

            // 项目设置：每个项目自己的 SSL 证书和 PHP 扩展入口
            bool editorDone=false;
            using(var timer=new System.Windows.Forms.Timer{Interval=150})
            {
                timer.Tick+=(_,_)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form&&f.Name=="projectEditor");if(dialog==null)return;timer.Stop();
                    var buttons=Desc(dialog).OfType<Button>().Select(b=>b.Name).ToList();
                    Check(buttons.Contains("projectSsl")&&buttons.Contains("projectExtensions"),$"{lang} project settings offer per-project SSL and extensions: {string.Join(",",buttons.Where(n=>n.Length>0))}");
                    Check(!Desc(dialog).OfType<TextBox>().Any(t2=>t2.Name=="projectExpires"),$"{lang} expiry reminder removed from project settings");
                    // 底部四个按钮都在窗口内、文字放得下；左侧标签不被截断
                    var footerButtons=Desc(dialog).OfType<Button>().Where(b=>b.Name is "projectSsl" or "projectExtensions" or "saveProject" or "cancelProject").ToList();
                    var outside=footerButtons.Where(b=>{var r=dialog.RectangleToClient(b.RectangleToScreen(b.ClientRectangle));return r.Left<0||r.Right>dialog.ClientSize.Width;}).Select(b=>b.Name).ToList();
                    Check(footerButtons.Count==4&&outside.Count==0,$"{lang} project settings buttons inside the window: {string.Join(",",outside)}");
                    var cramped=footerButtons.Where(b=>TextRenderer.MeasureText(b.Text,b.Font).Width+(int)Math.Round(16*b.DeviceDpi/96f)>b.Width).Select(b=>b.Text).ToList();
                    Check(cramped.Count==0,$"{lang} project settings button text fits: {string.Join(" | ",cramped)}");
                    var cut=Desc(dialog).OfType<Label>().Where(l=>l.Visible&&l.Text.Length>0&&l.Height<TextRenderer.MeasureText(l.Text,l.Font).Height).Select(l=>l.Text).ToList();
                    Check(cut.Count==0,$"{lang} project settings labels not cut: {string.Join(" | ",cut)}");
                    if(lang=="zh"){using var img=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,"project-editor.png"));}
                    dialog.DialogResult=DialogResult.Cancel;dialog.Close();editorDone=true;
                };
                timer.Start();
                typeof(MainForm).GetMethod("ProjectDialog",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,[settings.Sites[0]]);
                var until=DateTime.UtcNow.AddSeconds(10);while(!editorDone&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
            }
            Check(editorDone,$"{lang} project settings checked");
            using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,new Rectangle(Point.Empty,form.Size));shot.Save(Path.Combine(output,$"panel-{lang}.png"));}
            // 宽屏：卡片内的按钮区不跟着窗口拉伸，卡片高度随内容收缩
            form.Size=new Size(2400,940);Application.DoEvents();
            var card=Desc(form).OfType<TableLayoutPanel>().Single(t=>t.Name=="projectCard");
            var blocks=card.Controls.OfType<TableLayoutPanel>().ToList();
            Check(blocks.Count==4&&blocks.All(b=>b.Width<=(int)Math.Round(960*form.DeviceDpi/96f)),$"{lang} card blocks stay compact on a wide window: {string.Join(",",blocks.Select(b=>b.Width))}");
            Check(card.Height<card.Parent!.Height&&card.Height<(int)Math.Round(640*form.DeviceDpi/96f),$"{lang} card height follows its content: {card.Height}");
            if(lang=="zh"){using var wide=new Bitmap(form.Width,form.Height);form.DrawToBitmap(wide,new Rectangle(Point.Empty,form.Size));wide.Save(Path.Combine(output,"panel-wide.png"));}
            form.Size=new Size(1120,740);Application.DoEvents();
            Check(blocks.All(b=>b.Width>0&&b.Right<=card.ClientSize.Width),$"{lang} card blocks fit the minimum window: {string.Join(",",blocks.Select(b=>b.Width))}");
            form.Size=new Size(1320,820);Application.DoEvents();
            form.Close();Application.DoEvents();
        }
        // 所有对话框：当前显示缩放下，标准和特大两种界面字号、中英日都不越界、不截断
        foreach(var textSize in new[]{10f,11f,12f})
        foreach(var lang in new[]{"zh","en","ja"})
        {
            var settings=Settings.Load(root);settings.Language=lang;settings.FontSize=textSize;settings.Save();
            var runtime=new Runtime(settings);
            using var form=new MainForm(settings,runtime,true,false){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
            form.Show();Application.DoEvents();
            object? Call(string method,params object?[] values)=>typeof(MainForm).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,values);
            var site=settings.Sites[0];
            var empty=Path.Combine(Path.GetTempPath(),"yikai-empty-scan");Directory.CreateDirectory(empty);
            Inspect(form,"projectEditor",$"{lang}{textSize:0}-project-new",()=>Call("ProjectDialog",[null]),output);
            Inspect(form,"projectEditor",$"{lang}{textSize:0}-project-edit",()=>Call("ProjectDialog",[site]),output);
            Inspect(form,"sslDialog",$"{lang}{textSize:0}-ssl",()=>Call("ShowSslDialog",[null]),output,dialog=>{
                // 用不到的选项收起：关掉 HTTPS 时不显示证书来源，选“使用自己的证书”才显示证书和私钥
                void Pump(){for(var i=0;i<15;i++){Application.DoEvents();Thread.Sleep(15);}}
                var enable=Desc(dialog).OfType<CheckBox>().Single(c=>c.Name=="enableHttps");
                var autoRadio=Desc(dialog).OfType<RadioButton>().Single(r=>r.Name=="certificateAuto");
                var customRadio=Desc(dialog).OfType<RadioButton>().Single(r=>r.Name=="certificateCustom");
                var certBox=Desc(dialog).OfType<TextBox>().Single(b=>b.Name=="certificateFile");
                var height=dialog.ClientSize.Height;
                enable.Checked=false;Pump();
                Check(!autoRadio.Visible&&!certBox.Visible&&dialog.ClientSize.Height<height,$"{lang}{textSize:0}-ssl: HTTPS off hides certificate options ({height} -> {dialog.ClientSize.Height})");
                CheckLayout($"{lang}{textSize:0}-ssl-off",dialog,output);
                enable.Checked=true;customRadio.Checked=true;Pump();
                Check(autoRadio.Visible&&certBox.Visible,$"{lang}{textSize:0}-ssl: own certificate shows file rows");
                CheckLayout($"{lang}{textSize:0}-ssl-custom",dialog,output);
            });
            Inspect(form,"configEditor",$"{lang}{textSize:0}-config",()=>Call("ShowConfigEditor",[null]),output);
            Inspect(form,"rootPasswordDialog",$"{lang}{textSize:0}-root-password",()=>Call("ShowRootPasswordReset"),output);
            Inspect(form,"databasePortDialog",$"{lang}{textSize:0}-db-port",()=>Call("ShowDatabasePort"),output);
            Inspect(form,"rewriteDialog",$"{lang}{textSize:0}-rewrite",()=>Call("RewriteDialog",[site]),output);
            // PHPStudy 导入窗口是 async void 打开的，自动关闭时会卡住嵌套的模态循环（上一轮卡死的原因），这里不自动打开。
            Inspect(form,"aboutDialog",$"{lang}{textSize:0}-about",()=>Call("ShowAbout"),output);
            Inspect(form,"upgradeDialog",$"{lang}{textSize:0}-upgrade",()=>Call("ShowUpgrade"),output);
            Inspect(form,"backendEditor",$"{lang}{textSize:0}-backend",()=>Call("ChooseBackend",[site,AdminEntryDetector.Scan(empty)]),output);
            Inspect(form,"lanAccess",$"{lang}{textSize:0}-lan",()=>Call("ShowLanAccess"),output);
            Inspect(form,"appearance",$"{lang}{textSize:0}-appearance",()=>Call("ShowAppearance"),output);
            form.Close();Application.DoEvents();
        }
        {var reset=Settings.Load(root);reset.Language="zh";reset.FontSize=10f;reset.Save();}

        // 外观：深浅两套配色的对比度、外观窗口里切换主题与字号
        foreach(var mode in new[]{"light","dark"})
        {
            UseTheme(mode);
            Check(DarkTheme==(mode=="dark"),$"{mode} theme selected");
            Check(Contrast(Hue("Text"),Hue("Canvas"))>=7,$"{mode} body text contrast {Contrast(Hue("Text"),Hue("Canvas"))}");
            Check(Contrast(Hue("Secondary"),Hue("Canvas"))>=4.5,$"{mode} secondary text contrast {Contrast(Hue("Secondary"),Hue("Canvas"))}");
            Check(Contrast(Hue("Secondary"),Hue("Card"))>=4.5,$"{mode} secondary on card {Contrast(Hue("Secondary"),Hue("Card"))}");
            Check(Contrast(Hue("Link"),Hue("Card"))>=4.5,$"{mode} link contrast {Contrast(Hue("Link"),Hue("Card"))}");
            Check(Contrast(Hue("OnAccent"),Hue("Accent"))>=4.5,$"{mode} primary button contrast {Contrast(Hue("OnAccent"),Hue("Accent"))}");
            Check(Contrast(Hue("Success"),Hue("Surface"))>=3&&Contrast(Hue("Warning"),Hue("Surface"))>=3,$"{mode} status colors readable");
            Check(Contrast(Hue("Text"),Hue("Selection"))>=4.5,$"{mode} menu hover text contrast {Contrast(Hue("Text"),Hue("Selection"))}");
            var avatars=(Color[])PaletteType.GetField("Avatars",BindingFlags.Public|BindingFlags.Static)!.GetValue(null)!;
            Check(avatars.All(a=>Contrast(Color.White,a)>=4.5),$"{mode} avatar text contrast {avatars.Min(a=>Contrast(Color.White,a))}");
        }
        UseTheme("light");
        {
            var settings=Settings.Load(root);settings.Language="zh";settings.Theme="light";settings.FontSize=10f;settings.Save();
            var runtime=new Runtime(settings);
            using var form=new MainForm(settings,runtime,true,false){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
            form.Show();Application.DoEvents();
            var baseFont=form.Font.Size;
            bool done=false;
            using(var timer=new System.Windows.Forms.Timer{Interval=150})
            {
                timer.Tick+=(_,_)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form&&f.Name=="appearance");if(dialog==null)return;timer.Stop();
                    var theme=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="appearanceTheme");
                    var size=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="appearanceFontSize");
                    Check(theme.Items.Count==3&&size.Items.Count==3,"appearance offers three themes and three text sizes");
                    var codeFont=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="appearanceCodeFont");
                    var codeSize=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="appearanceCodeSize");
                    var previewBox=Desc(dialog).OfType<TextBox>().Single(b=>b.Name=="appearancePreview");
                    Check(codeFont.Items.Count>=2&&codeFont.Text.Length>0,$"programming fonts offered: {string.Join(",",codeFont.Items.Select(i=>i.ToString()))}");
                    Check(codeSize.Items.Count>=4,$"code sizes offered: {codeSize.Items.Count}");
                    Check(previewBox.Font.Name==codeFont.Text,$"preview uses the chosen font: {previewBox.Font.Name}");
                    var clipped=Desc(dialog).OfType<Label>().Where(l=>l.Text.Length>0&&(l.Height<l.PreferredSize.Height||l.Width<l.PreferredSize.Width)).Select(l=>l.Text).ToList();
                    Check(clipped.Count==0,$"no clipped text in the appearance dialog: {string.Join(" | ",clipped)}");
                    codeSize.SelectedIndex=codeSize.Items.Count-1;for(var i=0;i<30;i++){Application.DoEvents();Thread.Sleep(20);}
                    Check(settings.CodeFontSize>10f&&previewBox.Font.Size==settings.CodeFontSize,$"code size applied: {previewBox.Font.Size}");
                    codeSize.SelectedIndex=1;for(var i=0;i<30;i++){Application.DoEvents();Thread.Sleep(20);}
                    void Pump(){for(var i=0;i<30;i++){Application.DoEvents();Thread.Sleep(20);}}
                    theme.SelectedIndex=2;Pump();
                    Check(DarkTheme&&settings.Theme=="dark"&&form.BackColor==Hue("Canvas"),$"dark theme applied to the window: {form.BackColor}");
                    using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,new Rectangle(Point.Empty,form.Size));shot.Save(Path.Combine(output,"panel-dark.png"));}
                    using(var img=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,"appearance-dark.png"));}
                    size.SelectedIndex=2;Pump();
                    Check(settings.FontSize==12f&&form.Font.Size>baseFont,$"large text applied: {form.Font.Size}");
                    var list=Desc(form).OfType<ListBox>().Single(l=>l.Name=="projects");
                    Check(list.ItemHeight>(int)Math.Round(76*form.DeviceDpi/96f),$"list rows grow with the text size: {list.ItemHeight}");
                    Check(form.MinimumSize.Width>(int)Math.Round(1120*form.DeviceDpi/96f),$"minimum window size grows with the text size: {form.MinimumSize}");
                    using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,new Rectangle(Point.Empty,form.Size));shot.Save(Path.Combine(output,"panel-large.png"));}
                    // 悬停的菜单项：底色必须是面板配色，不是系统高亮蓝
                    form.TopMost=true;form.Activate();SetForegroundWindow(form.Handle);Application.DoEvents();
                    var configButton=Desc(form).OfType<Button>().Single(b=>b.Name=="environmentConfig");
                    ToolStripDropDown? menu=null;
                    for(var attempt=0;attempt<3&&menu==null;attempt++)
                    {
                        configButton.PerformClick();
                        for(var i=0;i<40&&menu==null;i++){Application.DoEvents();menu=OpenDropDown();Thread.Sleep(20);}
                    }
                    Check(menu!=null,"config menu opens for the hover check");
                    if(menu!=null)
                    {
                        var first=menu.Items.OfType<ToolStripMenuItem>().First();
                        first.Select();for(var i=0;i<15;i++){Application.DoEvents();Thread.Sleep(20);}
                        using var shot=new Bitmap(menu.Width,menu.Height);
                        using(var g=Graphics.FromImage(shot))g.CopyFromScreen(menu.Bounds.Location,Point.Empty,menu.Bounds.Size);
                        shot.Save(Path.Combine(output,"menu-hover-dark.png"));
                        var sample=shot.GetPixel(Math.Min(shot.Width-4,first.Bounds.Right-8),Math.Min(shot.Height-2,first.Bounds.Top+first.Bounds.Height/2));
                        Check(Near(sample,Hue("Selection")),$"hovered menu item uses the panel colour: {sample} vs {Hue("Selection")}");
                        menu.Close();Application.DoEvents();
                    }
                    form.TopMost=false;
                    theme.SelectedIndex=1;size.SelectedIndex=0;Pump();
                    Check(!DarkTheme&&settings.Theme=="light"&&settings.FontSize==10f,"back to light and standard");
                    dialog.DialogResult=DialogResult.Cancel;dialog.Close();done=true;
                };
                timer.Start();
                typeof(MainForm).GetMethod("ShowAppearance",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null);
                var until=DateTime.UtcNow.AddSeconds(20);while(!done&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
            }
            Check(done,"appearance dialog checked");
            // 用户实际路径：字号不变，只改编程字体和主题。旧实现会把仍在使用的主窗口字体释放掉，之后点“升级”报 Parameter is not valid。
            Inspect(form,"appearance","appearance-font-only",()=>typeof(MainForm).GetMethod("ShowAppearance",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null),output,dialog=>{
                var codeFontChoice=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="appearanceCodeFont");
                var themeChoice=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="appearanceTheme");
                var preview=Desc(dialog).OfType<TextBox>().Single(b=>b.Name=="appearancePreview");
                void Pump(){for(var i=0;i<30;i++){Application.DoEvents();Thread.Sleep(20);}}
                codeFontChoice.SelectedIndex=codeFontChoice.Items.Count>1?1:0;Pump();
                themeChoice.SelectedIndex=2;Pump();themeChoice.SelectedIndex=1;Pump();
                bool Alive(Font font){try{_=font.FontFamily.Name;_=font.ToHfont();return true;}catch(ArgumentException){return false;}}
                Check(Alive(form.Font),"main window font still valid after changing only theme and code font");
                Check(Alive(preview.Font)&&Alive(dialog.Font),"appearance window fonts still valid");
                codeFontChoice.SelectedIndex=0;Pump();
            });
            // 改过外观之后，同一个窗口里再打开其他对话框（用户实际遇到：改外观后点“升级”报 Parameter is not valid）
            foreach(var (dialogName,method) in new[]{("upgradeDialog","ShowUpgrade"),("aboutDialog","ShowAbout"),("lanAccess","ShowLanAccess"),("appearance","ShowAppearance")})
            {
                try{Inspect(form,dialogName,"after-appearance-"+dialogName,()=>typeof(MainForm).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null),output);}
                catch(Exception error){Check(false,$"after-appearance-{dialogName}: {error.GetBaseException().GetType().Name} {error.GetBaseException().Message} {error.GetBaseException().StackTrace?.Split('\n').FirstOrDefault(l=>l.Contains("YikaiLocal"))}");}
            }
            try{using var shot=new Bitmap(form.Width,form.Height);form.DrawToBitmap(shot,new Rectangle(Point.Empty,form.Size));Check(true,"main window still paints after appearance changes");}
            catch(Exception error){Check(false,"main window paint after appearance: "+error.GetBaseException().Message);}
            form.Close();Application.DoEvents();
        }
        UseTheme("light");

        // 局域网访问窗口：地址列表、开关状态、防火墙说明（不改防火墙，不启动服务）
        {
            var settings=Settings.Load(root);settings.Language="zh";settings.LanAccess=true;
            var runtime=new Runtime(settings);
            using var form=new MainForm(settings,runtime,true,false){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
            form.Show();Application.DoEvents();
            bool done=false;
            using(var timer=new System.Windows.Forms.Timer{Interval=150})
            {
                timer.Tick+=(_,_)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form&&f.Name=="lanAccess");if(dialog==null)return;timer.Stop();
                    var toggle=Desc(dialog).OfType<CheckBox>().Single(c=>c.Name=="enableLanAccess");
                    var box=Desc(dialog).OfType<TextBox>().Single(t2=>t2.Name=="lanAddresses");
                    var firewall=Desc(dialog).OfType<Label>().Single(l=>l.Name=="firewallState");
                    var ips=Runtime.LocalAddresses();
                    Check(toggle.Checked,"lan dialog reflects the setting");
                    Check(ips.Count==0||box.Lines.Count(l=>l.Length>0)==settings.Sites.Count*ips.Count,$"one address per project and interface: {box.Lines.Count(l=>l.Length>0)} lines, {settings.Sites.Count} projects, {ips.Count} addresses");
                    Check(ips.Count==0||box.Text.Contains("http"),"addresses are shown as URLs");
                    Check(!box.Text.Contains(settings.DbManagerPort.ToString()),"database manager port is not offered to the network");
                    Check(firewall.Text.Length>0,"firewall state explained");
                    using(var img=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,"lan-access-zh.png"));}
                    dialog.DialogResult=DialogResult.Cancel;dialog.Close();done=true;
                };
                timer.Start();
                typeof(MainForm).GetMethod("ShowLanAccess",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null);
                var until=DateTime.UtcNow.AddSeconds(12);while(!done&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
            }
            Check(done,"lan access dialog checked");
            form.Close();Application.DoEvents();
        }
        // PHP 扩展窗口：需要一个带真实 soft\php\8.2 的根目录（第三个参数）
        if(args.Length>2&&Directory.Exists(args[2]))
        {
            var settings=Settings.Load(args[2]);settings.Language="zh";
            var runtime=new Runtime(settings);
            using var form=new MainForm(settings,runtime,true,false){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
            form.Show();Application.DoEvents();
            var enabledInIni=File.ReadAllText(runtime.PhpIniPath("8.2")).Split('\n').Where(l=>l.TrimStart().StartsWith("extension=")||l.TrimStart().StartsWith("zend_extension=")).Select(l=>l.Split('=')[1].Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool done=false;
            using(var timer=new System.Windows.Forms.Timer{Interval=150})
            {
                timer.Tick+=(_,_)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form&&f.Name=="phpExtensions");if(dialog==null)return;timer.Stop();
                    var list=Desc(dialog).OfType<CheckedListBox>().Single(l=>l.Name=="extensionList");
                    var status=Desc(dialog).OfType<Label>().Single(l=>l.Name=="extensionStatus");
                    var labels=list.Items.Cast<object>().Select(i=>i.ToString()!).ToList();
                    Check(labels.Count>=30,$"extensions listed: {labels.Count}");
                    var checkedNames=Enumerable.Range(0,list.Items.Count).Where(list.GetItemChecked).Select(i=>labels[i].Split('　')[0]).ToList();
                    Check(checkedNames.All(n=>enabledInIni.Contains(n))&&enabledInIni.Where(n=>labels.Any(l=>l.StartsWith(n+"　")||l==n)).All(n=>checkedNames.Contains(n,StringComparer.OrdinalIgnoreCase)),$"ticked extensions match php.ini: {string.Join(",",checkedNames)}");
                    Check(labels.Any(l=>l.StartsWith("opcache")&&l.Contains("zend_extension")),"opcache marked as zend_extension");
                    var required=labels.FindIndex(l=>l.StartsWith("mbstring"));
                    Check(required>=0&&list.GetItemChecked(required),"required extension is ticked");
                    list.SetItemChecked(required,false);Application.DoEvents();
                    Check(list.GetItemChecked(required)&&status.Text.Contains("mbstring"),$"required extension cannot be unticked: {status.Text}");
                    var optional=labels.FindIndex(l=>l.StartsWith("bz2"));
                    if(optional>=0){var before=list.GetItemChecked(optional);list.SetItemChecked(optional,!before);Application.DoEvents();Check(list.GetItemChecked(optional)!=before,"optional extension can be ticked");list.SetItemChecked(optional,before);}
                    using(var img=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,"php-extensions-zh.png"));}
                    dialog.DialogResult=DialogResult.Cancel;dialog.Close();done=true;
                };
                timer.Start();
                typeof(MainForm).GetMethod("ShowPhpExtensions",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,["8.2",null]);
                var until=DateTime.UtcNow.AddSeconds(12);while(!done&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
            }
            Check(done,"php extensions dialog checked");
            // 项目模式：只改这个项目，带“跟随版本设置”，并标出与版本不同的项
            var project=settings.Sites[0];
            project.ExtensionsOn=["gmp"];project.ExtensionsOff=null;settings.Save();
            bool projectDone=false;
            using(var timer=new System.Windows.Forms.Timer{Interval=150})
            {
                timer.Tick+=(_,_)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form&&f.Name=="phpExtensions");if(dialog==null)return;timer.Stop();
                    var list=Desc(dialog).OfType<CheckedListBox>().Single(l=>l.Name=="extensionList");
                    var choice=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="extensionPhp");
                    var followButton=Desc(dialog).OfType<Button>().Single(b=>b.Name=="followVersion");
                    var status=Desc(dialog).OfType<Label>().Single(l=>l.Name=="extensionStatus");
                    var labels=list.Items.Cast<object>().Select(i=>i.ToString()!).ToList();
                    var gmp=labels.FindIndex(l=>l.StartsWith("gmp"));
                    Check(!choice.Enabled&&choice.Text.Contains(project.ToString()),$"project mode is fixed to this project: {choice.Text}");
                    Check(followButton.Visible,"project mode offers following the version settings");
                    Check(gmp>=0&&list.GetItemChecked(gmp)&&labels[gmp].Contains("本项目"),$"project-only extension is ticked and marked: {labels[gmp]}");
                    Check(status.Text.Contains("1"),$"status counts the project changes: {status.Text}");
                    var versionOnly=runtime.PhpExtensionList("8.2").Single(e=>e.Name=="gmp");
                    Check(!versionOnly.Enabled,"the PHP version itself still has it off");
                    using(var img=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,"php-extensions-project.png"));}
                    dialog.DialogResult=DialogResult.Cancel;dialog.Close();projectDone=true;
                };
                timer.Start();
                typeof(MainForm).GetMethod("ShowPhpExtensions",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,["8.2",project]);
                var until=DateTime.UtcNow.AddSeconds(12);while(!projectDone&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
            }
            Check(projectDone,"project extensions dialog checked");
            project.ExtensionsOn=null;settings.Save();
            Check(File.ReadAllText(runtime.PhpIniPath("8.2")).Contains("extension=mbstring"),"php.ini untouched by the dialog check");
            form.Close();Application.DoEvents();
        }
        File.WriteAllLines(Path.Combine(output,"verification.txt"),Lines);
        Console.WriteLine($"{Lines.Count(l=>l.StartsWith("PASS"))}/{Lines.Count} passed");
    }
}
