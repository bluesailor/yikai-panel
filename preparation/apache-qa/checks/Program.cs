using System.Reflection;
using YikaiLocal;

// 状态栏服务菜单与 Web 服务器 / 默认 MySQL 选择的界面检查（不启动任何服务）。
static class Program
{
    static readonly List<string> Lines=[];
    static IEnumerable<Control> Desc(Control root){foreach(Control c in root.Controls){yield return c;foreach(var n in Desc(c))yield return n;}}
    static void Check(bool ok,string text){Lines.Add((ok?"PASS ":"FAIL ")+text);if(!ok)Environment.ExitCode=1;}
    static T Call<T>(object target,string name,params object[] args)=>(T)target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,args)!;
    [STAThread]
    static void Main(string[] args)
    {
        var root=args[0];var output=args[1];Directory.CreateDirectory(output);
        ApplicationConfiguration.Initialize();
        foreach(var lang in new[]{"zh","en","ja"})
        {
            var settings=Settings.Load(root);settings.Language=lang;settings.WebServer="apache";settings.MysqlActive="mysql80";
            var runtime=new Runtime(settings);
            using var form=new MainForm(settings,runtime,true){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
            form.Show();Application.DoEvents();
            // 项目列表：空闲时定时刷新不应重画；状态变化后才重画
            var projectList=Desc(form).OfType<ListBox>().Single(l=>l.Name=="projects");int draws=0;projectList.DrawItem+=(_,_)=>draws++;
            void Pump(double seconds){var until=DateTime.UtcNow.AddSeconds(seconds);while(DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(20);}}
            Pump(2);draws=0;Pump(4.5);
            Check(draws==0,$"{lang} project list not redrawn on idle refresh ticks (draws={draws})");
            settings.Sites[0].Starred=!settings.Sites[0].Starred;Pump(2);
            Check(draws>0,$"{lang} project list redrawn after state change (draws={draws})");
            settings.Sites[0].Starred=!settings.Sites[0].Starred;Pump(2);
            var labels=Desc(form).OfType<Label>().ToList();
            var web=labels.Single(l=>l.Text.StartsWith("Apache 2.4.39"));
            Check(web.Cursor==Cursors.Hand,$"{lang} web status is clickable");
            var php=labels.Single(l=>l.Text.StartsWith("PHP 8.2 · "));
            Check(php.Cursor==Cursors.Hand,$"{lang} php status shows enabled versions: {php.Text}");
            var webMenu=Call<ToolStripMenuItem>(form,"WebServerMenu");
            var items=webMenu.DropDownItems.OfType<ToolStripMenuItem>().ToList();
            Check(items.Count==2&&items[0].Text=="Nginx"&&!items[0].Checked&&items[1].Text=="Apache 2.4.39"&&items[1].Checked&&items.All(i=>i.Enabled),$"{lang} web server menu marks Apache and both installed");
            var mysql=Call<ToolStripMenuItem>(form,"DefaultMysqlMenu").DropDownItems.OfType<ToolStripMenuItem>().ToList();
            Check(mysql.Count==2&&mysql[0].Checked&&!mysql[1].Checked,$"{lang} default MySQL menu marks 8.0");
            // 真实弹出状态栏菜单并截屏
            form.TopMost=true;form.Activate();SetForegroundWindow(form.Handle);Application.DoEvents();
            // 真实鼠标点击状态栏上的 Web 服务器文字
            var target=web.PointToScreen(new Point(40,web.Height/2));SetCursorPos(target.X,target.Y);mouse_event(0x0002,0,0,0,IntPtr.Zero);mouse_event(0x0004,0,0,0,IntPtr.Zero);
            ToolStripDropDown? menu=null;
            for(var attempt=0;attempt<4&&menu==null;attempt++)
            {
                if(attempt>0){form.Activate();SetForegroundWindow(form.Handle);Application.DoEvents();SetCursorPos(target.X,target.Y);mouse_event(0x0002,0,0,0,IntPtr.Zero);mouse_event(0x0004,0,0,0,IntPtr.Zero);}
                for(var i=0;i<40&&menu==null;i++){Application.DoEvents();menu=ToolStripManager_OpenDropDown();Thread.Sleep(20);}
            }
            for(var i=0;i<10;i++){Application.DoEvents();Thread.Sleep(20);}
            Check(menu!=null,$"{lang} web status opens a menu");
            if(menu!=null)
            {
                var texts=menu.Items.OfType<ToolStripItem>().Where(i=>i is not ToolStripSeparator).Select(i=>i.Text).ToList();
                Check(texts.Count==6&&texts[0]=="Apache 2.4.39"&&texts[5].Contains("Apache"),$"{lang} web menu items: {string.Join(" | ",texts)}");
                var start=menu.Items.OfType<ToolStripMenuItem>().ElementAt(1);var stop=menu.Items.OfType<ToolStripMenuItem>().ElementAt(2);
                Check(start.Enabled&&!stop.Enabled,$"{lang} stopped service: start enabled, stop disabled");
                var bounds=Rectangle.Union(form.Bounds,menu.Bounds);
                using var shot=new Bitmap(bounds.Width,bounds.Height);using(var g=Graphics.FromImage(shot))g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
                shot.Save(Path.Combine(output,$"service-menu-{lang}.png"));
                menu.Close();
            }
            // PHP 状态栏菜单：按版本子菜单与“新项目默认 PHP 版本”
            var defaults=Call<ToolStripMenuItem>(form,"DefaultPhpMenu").DropDownItems.OfType<ToolStripMenuItem>().ToList();
            Check(defaults.Count==3&&defaults.Count(i=>i.Checked)==1&&defaults.Single(i=>i.Checked).Text.StartsWith("PHP "+settings.PhpDefault),$"{lang} default PHP menu marks {settings.PhpDefault}");
            form.Activate();SetForegroundWindow(form.Handle);Application.DoEvents();
            var phpTarget=php.PointToScreen(new Point(40,php.Height/2));ToolStripDropDown? phpMenu=null;
            for(var attempt=0;attempt<4&&phpMenu==null;attempt++)
            {
                form.Activate();SetForegroundWindow(form.Handle);Application.DoEvents();SetCursorPos(phpTarget.X,phpTarget.Y);mouse_event(0x0002,0,0,0,IntPtr.Zero);mouse_event(0x0004,0,0,0,IntPtr.Zero);
                for(var i=0;i<40&&phpMenu==null;i++){Application.DoEvents();phpMenu=ToolStripManager_OpenDropDown();Thread.Sleep(20);}
            }
            Check(phpMenu!=null,$"{lang} php status opens a menu");
            if(phpMenu!=null)
            {
                var names=phpMenu.Items.OfType<ToolStripMenuItem>().Select(i=>i.Text).ToList();
                Check(Runtime.PhpVersions.All(v=>names.Any(n=>n!.StartsWith("PHP "+v+" · ")))&&names.Last()==defaults[0].OwnerItem!.Text,$"{lang} php menu: {string.Join(" | ",names)}");
                var v82=phpMenu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Name=="phpVersion8.2");v82.ShowDropDown();
                for(var i=0;i<10;i++){Application.DoEvents();Thread.Sleep(20);}
                Check(v82.DropDownItems.OfType<ToolStripMenuItem>().Count()==4&&v82.DropDownItems[0].Enabled&&!v82.DropDownItems[1].Enabled&&v82.DropDownItems[4].Text!.Contains("php.ini"),$"{lang} php 8.2 submenu: start enabled, stop disabled while stopped");
                var bounds=Rectangle.Union(Rectangle.Union(form.Bounds,phpMenu.Bounds),v82.DropDown.Bounds);
                using var shot=new Bitmap(bounds.Width,bounds.Height);using(var g=Graphics.FromImage(shot))g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
                shot.Save(Path.Combine(output,$"php-menu-{lang}.png"));
                phpMenu.Close();
            }
            // 新建项目对话框：默认 PHP 8.0 时，YikaiCMS 回落 8.2，空白 PHP 使用 8.0
            settings.PhpDefault="8.0";bool dialogDone=false;
            using(var timer=new System.Windows.Forms.Timer{Interval=150})
            {
                timer.Tick+=(_,_)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form);if(dialog==null)return;timer.Stop();
                    var kind=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="projectKind");var version=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="projectPhp");
                    Check(version.Text=="8.2",$"{lang} new YikaiCMS project falls back to PHP 8.2 when default is 8.0");
                    kind.SelectedIndex=1;Check(version.Text=="8.0",$"{lang} new blank PHP project uses default PHP 8.0");
                    version.SelectedItem="8.5";kind.SelectedIndex=2;Check(version.Text=="8.5",$"{lang} manual PHP choice is kept when type changes");
                    // 对话框按钮：禁用 / 可用两种状态截图，检查文字居中
                    kind.SelectedIndex=1;var save=Desc(dialog).OfType<Button>().Single(b=>b.Name=="saveProject");var cancelButton=Desc(dialog).OfType<Button>().Single(b=>b.Name=="cancelProject");
                    Check(!save.Enabled,$"{lang} create disabled before domain");
                    void Snap(string name){Application.DoEvents();using var img=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,$"project-dialog-{name}-{lang}.png"));}
                    Snap("disabled");
                    Desc(dialog).OfType<TextBox>().Single(t=>t.Name=="projectDomain").Text="demo-button";Application.DoEvents();
                    Check(save.Enabled&&cancelButton.Enabled,$"{lang} create enabled after domain");Snap("enabled");
                    // 数据库名 / 用户 / 密码：默认随域名生成、可修改、非法值禁止创建、SQLite 时禁用
                    var dbNameBox=Desc(dialog).OfType<TextBox>().Single(t=>t.Name=="projectDatabaseName");var dbUserBox=Desc(dialog).OfType<TextBox>().Single(t=>t.Name=="projectDatabaseUser");var dbPassBox=Desc(dialog).OfType<TextBox>().Single(t=>t.Name=="projectDatabasePassword");
                    var engineChoice=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="projectDatabase");
                    Check(dbNameBox.Text=="demo_button_yikai"&&dbUserBox.Text=="demo_button_yikai"&&dbPassBox.Text.Length==16,$"{lang} database defaults follow domain ({dbNameBox.Text}/{dbUserBox.Text}/{dbPassBox.Text.Length})");
                    dbUserBox.Text="root";Application.DoEvents();
                    Check(!save.Enabled,$"{lang} root as database user blocks create");
                    dbUserBox.Text="demo_owner";Application.DoEvents();
                    Check(save.Enabled&&dbNameBox.Text=="demo_button_yikai",$"{lang} custom database user accepted, name kept");
                    var oldPass=dbPassBox.Text;Desc(dialog).OfType<Button>().Single(b=>b.Name=="regenerateDatabasePassword").PerformClick();
                    Check(dbPassBox.Text.Length==16&&dbPassBox.Text!=oldPass,$"{lang} regenerate password");
                    Snap("database");
                    engineChoice.SelectedIndex=2;Application.DoEvents();
                    Check(!dbNameBox.Enabled&&!dbUserBox.Enabled&&!dbPassBox.Enabled&&save.Enabled,$"{lang} SQLite disables database account fields");
                    engineChoice.SelectedIndex=0;Application.DoEvents();
                    // 目录按钮：新建时三种类型都可用；目标文件夹已存在时不能创建
                    var browseButton=Desc(dialog).OfType<Button>().Single(b=>b.Name=="browseDirectory");
                    foreach(var index in new[]{0,1,2}){kind.SelectedIndex=index;Application.DoEvents();Check(browseButton.Enabled&&browseButton.Visible,$"{lang} browse enabled for project type {index}");}
                    kind.SelectedIndex=1;var existing=Path.Combine(settings.Root,"wwwroot","exists-check.yikai");Directory.CreateDirectory(existing);
                    Desc(dialog).OfType<TextBox>().Single(t=>t.Name=="projectDomain").Text="exists-check";Application.DoEvents();
                    Check(!save.Enabled,$"{lang} create disabled when target folder exists");Snap("exists");Directory.Delete(existing);
                    dialog.DialogResult=DialogResult.Cancel;dialog.Close();dialogDone=true;
                };
                timer.Start();
                typeof(MainForm).GetMethod("ProjectDialog",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,[null]);
                var until=DateTime.UtcNow.AddSeconds(10);while(!dialogDone&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
            }
            Check(dialogDone,$"{lang} project dialog checked");
            // 编辑已有项目：不移动目录，隐藏“…”
            bool editDone=false;
            using(var timer=new System.Windows.Forms.Timer{Interval=150})
            {
                timer.Tick+=(_,_)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form);if(dialog==null)return;timer.Stop();
                    Check(!Desc(dialog).OfType<Button>().Single(b=>b.Name=="browseDirectory").Visible,$"{lang} browse hidden when editing project");
                    Check(Desc(dialog).OfType<TextBox>().Where(t=>t.Name.StartsWith("projectDatabase")).All(t=>t.ReadOnly)&&!Desc(dialog).OfType<Button>().Single(b=>b.Name=="regenerateDatabasePassword").Visible,$"{lang} database fields read-only when editing");
                    dialog.DialogResult=DialogResult.Cancel;dialog.Close();editDone=true;
                };
                timer.Start();
                typeof(MainForm).GetMethod("ProjectDialog",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,[settings.Sites[0]]);
                var until=DateTime.UtcNow.AddSeconds(10);while(!editDone&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
            }
            Check(editDone,$"{lang} edit dialog checked");
            // 配置文件编辑器：文件列表、载入内容、查找；只读检查，不保存
            bool configDone=false;
            using(var timer=new System.Windows.Forms.Timer{Interval=150})
            {
                timer.Tick+=(_,_)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form&&f.Name=="configEditor");if(dialog==null)return;timer.Stop();
                    var list=Desc(dialog).OfType<ListBox>().Single(l=>l.Name=="configFiles");var editor=Desc(dialog).OfType<TextBox>().Single(t=>t.Name=="configText");
                    var files=list.Items.Cast<ConfigFile>().ToList();
                    Check(files.Any(f=>f.Key=="php8.2")&&files.Any(f=>f.Key=="nginx")&&files.Any(f=>f.Key=="apache")&&files.Any(f=>f.Key=="mysql80")&&files.Any(f=>f.Key=="dbpage"),$"{lang} config list: {string.Join(" | ",files.Select(f=>f.Title))}");
                    var php=files.Single(f=>f.Key=="php8.2");
                    Check(((ConfigFile)list.SelectedItem!).Key=="php8.2"&&editor.Text.ReplaceLineEndings("\n")==File.ReadAllText(php.FilePath).ReplaceLineEndings("\n"),$"{lang} opens requested php.ini with file content");
                    var find=Desc(dialog).OfType<TextBox>().Single(t=>t.Name=="configFind");find.Text="memory_limit";Desc(dialog).OfType<Button>().Single(b=>b.Name=="configFindNext").PerformClick();Application.DoEvents();
                    Check(editor.SelectedText.Equals("memory_limit",StringComparison.OrdinalIgnoreCase),$"{lang} find selects match");
                    dialog.Size=new Size(1080,760);Application.DoEvents();using(var img=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,$"config-editor-{lang}.png"));}
                    list.SelectedItem=files.Single(f=>f.Key=="nginx");Application.DoEvents();
                    Check(editor.Text.Contains("client_max_body_size"),$"{lang} nginx custom template shown");
                    dialog.Close();configDone=true;
                };
                timer.Start();
                typeof(MainForm).GetMethod("ShowConfigEditor",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,["php8.2"]);
                var until=DateTime.UtcNow.AddSeconds(15);while(!configDone&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
            }
            Check(configDone,$"{lang} config editor closes without prompt when unchanged");
            // 配置菜单：设置类菜单项；工具栏在最小窗口宽度下放得下
            form.WindowState=FormWindowState.Normal;form.Size=form.MinimumSize;Application.DoEvents();
            var commandButtons=Desc(form).OfType<Button>().Where(b=>b.Name is "startEnvironment" or "stopEnvironment" or "syncDomains" or "environmentTools" or "environmentConfig" or "upgradePanel" or "aboutPanel").ToList();
            var language=Desc(form).OfType<Choice>().Single(c=>c.Name=="language");
            var lastRight=commandButtons.Max(b=>b.PointToScreen(new Point(b.Width,0)).X);
            Check(commandButtons.Count==7&&commandButtons.All(b=>b.Visible)&&lastRight<=language.PointToScreen(Point.Empty).X,$"{lang} toolbar fits at minimum width (last={lastRight}, language={language.PointToScreen(Point.Empty).X})");
            Check(lang=="zh"||commandButtons.Any(b=>b.Text==""),$"{lang} narrow toolbar collapses some buttons to icons: {string.Join(",",commandButtons.Where(b=>b.Text=="").Select(b=>b.Name))}");
            form.Size=new Size(form.MinimumSize.Width+700,form.MinimumSize.Height);Application.DoEvents();
            Check(commandButtons.All(b=>b.Text!=""),$"{lang} wide toolbar restores all labels");
            form.Size=form.MinimumSize;Application.DoEvents();
            var initial=typeof(MainForm).GetMethod("InitialBounds",BindingFlags.NonPublic|BindingFlags.Static)!;
            Rectangle Bounds(Rectangle work,float scale,int? w,int? h){var args=new object?[]{work,scale,w,h,null};return (Rectangle)initial.Invoke(null,args)!;}
            var fourK=Bounds(new Rectangle(0,0,3840,2088),1.5f,null,null);
            Check(fourK.Size==new Size(2400,1560)&&fourK.X==720&&fourK.Y==264,$"{lang} 4K 150% first window {fourK}");
            var small=Bounds(new Rectangle(0,0,1366,728),1f,null,null);
            Check(small.Width==1180&&small.Height==728&&small.Y==0,$"{lang} small screen window fits work area {small}");
            var saved=Bounds(new Rectangle(0,0,3840,2088),1.5f,1400,900);
            Check(saved.Size==new Size(2100,1350),$"{lang} saved window size restored {saved}");
            var configButton=commandButtons.Single(b=>b.Name=="environmentConfig");ToolStripDropDown? configMenu=null;
            for(var attempt=0;attempt<4&&configMenu==null;attempt++)
            {
                form.Activate();SetForegroundWindow(form.Handle);Application.DoEvents();var at=configButton.PointToScreen(new Point(configButton.Width/2,configButton.Height/2));SetCursorPos(at.X,at.Y);mouse_event(0x0002,0,0,0,IntPtr.Zero);mouse_event(0x0004,0,0,0,IntPtr.Zero);
                for(var i=0;i<40&&configMenu==null;i++){Application.DoEvents();configMenu=ToolStripManager_OpenDropDown();Thread.Sleep(20);}
            }
            Check(configMenu!=null,$"{lang} config menu opens");
            if(configMenu!=null)
            {
                for(var i=0;i<10;i++){Application.DoEvents();Thread.Sleep(20);}
                var configItems=configMenu.Items.OfType<ToolStripMenuItem>().ToList();
                Check(configItems.Count==6&&configItems[0].Name=="configFilesMenu"&&configItems[1].Name=="sslCertificatesMenu"&&configItems[1].Enabled&&configItems[5].Name=="rootPasswordMenu"&&configItems[2].DropDownItems.Count==2&&configItems[3].DropDownItems.Count==3&&configItems[4].DropDownItems.Count==2,$"{lang} config menu: {string.Join(" | ",configItems.Select(i=>i.Text))}");
                var bounds=Rectangle.Union(form.Bounds,configMenu.Bounds);
                using(var shot=new Bitmap(bounds.Width,bounds.Height)){using(var g=Graphics.FromImage(shot))g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);shot.Save(Path.Combine(output,$"config-menu-{lang}.png"));}
                configMenu.Close();
                // 再打开一次：动态子菜单不重复
                SetCursorPos(configButton.PointToScreen(new Point(configButton.Width/2,configButton.Height/2)).X,configButton.PointToScreen(new Point(configButton.Width/2,configButton.Height/2)).Y);mouse_event(0x0002,0,0,0,IntPtr.Zero);mouse_event(0x0004,0,0,0,IntPtr.Zero);
                ToolStripDropDown? again=null;for(var i=0;i<40&&again==null;i++){Application.DoEvents();again=ToolStripManager_OpenDropDown();Thread.Sleep(20);}
                Check(again!=null&&again.Items.OfType<ToolStripMenuItem>().Count()==6,$"{lang} config menu stable on reopen");
                again?.Close();
            }
            // SSL 证书对话框：只检查界面状态，不保存、不改系统证书库
            bool sslDone=false;
            using(var timer=new System.Windows.Forms.Timer{Interval=150})
            {
                timer.Tick+=(_,_)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form&&f.Name=="sslDialog");if(dialog==null)return;timer.Stop();
                    var enable=Desc(dialog).OfType<CheckBox>().Single(c=>c.Name=="enableHttps");var custom=Desc(dialog).OfType<RadioButton>().Single(r=>r.Name=="certificateCustom");var auto=Desc(dialog).OfType<RadioButton>().Single(r=>r.Name=="certificateAuto");
                    var certFile=Desc(dialog).OfType<TextBox>().Single(t=>t.Name=="certificateFile");var projectChoice=Desc(dialog).OfType<Choice>().Single(c=>c.Name=="sslProject");
                    Check(projectChoice.Items.Count==settings.Sites.Count&&Desc(dialog).OfType<Label>().Single(l=>l.Name=="caStatus").Text.Length>0,$"{lang} ssl dialog lists projects and root status");
                    enable.Checked=false;Application.DoEvents();
                    Check(!certFile.Enabled&&!auto.Enabled&&!custom.Enabled,$"{lang} ssl options disabled while https off");
                    enable.Checked=true;custom.Checked=true;Application.DoEvents();
                    Check(certFile.Enabled&&!Desc(dialog).OfType<Button>().Single(b=>b.Name=="renewCertificate").Enabled,$"{lang} custom certificate enables file fields, disables re-issue");
                    auto.Checked=true;Application.DoEvents();
                    Check(!certFile.Enabled,$"{lang} auto certificate hides file fields");
                    Application.DoEvents();using(var img=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,$"ssl-dialog-{lang}.png"));}
                    dialog.Close();sslDone=true;
                };
                timer.Start();
                typeof(MainForm).GetMethod("ShowSslDialog",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null);
                var until=DateTime.UtcNow.AddSeconds(10);while(!sslDone&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
            }
            Check(sslDone,$"{lang} ssl dialog opened and closed");
            settings.PhpDefault="8.2";settings.Save();
            // PHPStudy 导入：左栏不再有按钮，入口在“工具”菜单第一项，点击打开导入窗口
            Check(!Desc(form).OfType<Button>().Any(b=>b.Name=="importPhpStudy"),$"{lang} sidebar has no PHPStudy import button");
            var addButton=Desc(form).OfType<Button>().Single(b=>b.Name=="addProject");
            Check(addButton.Visible&&addButton.Bottom<=addButton.Parent!.ClientSize.Height,$"{lang} add project button fits sidebar");
            var tools=Desc(form).OfType<Button>().Single(b=>b.Name=="environmentTools");ToolStripDropDown? toolsMenu=null;
            for(var attempt=0;attempt<4&&toolsMenu==null;attempt++)
            {
                form.Activate();SetForegroundWindow(form.Handle);Application.DoEvents();var at=tools.PointToScreen(new Point(tools.Width/2,tools.Height/2));SetCursorPos(at.X,at.Y);mouse_event(0x0002,0,0,0,IntPtr.Zero);mouse_event(0x0004,0,0,0,IntPtr.Zero);
                for(var i=0;i<40&&toolsMenu==null;i++){Application.DoEvents();toolsMenu=ToolStripManager_OpenDropDown();Thread.Sleep(20);}
            }
            Check(toolsMenu!=null,$"{lang} tools menu opens");
            if(toolsMenu!=null)
            {
                var dbItem=toolsMenu.Items.OfType<ToolStripMenuItem>().SingleOrDefault(i=>i.Name=="databaseManagerMenu");
                var toolNames=toolsMenu.Items.OfType<ToolStripMenuItem>().Select(i=>i.Name).ToList();
                Check(dbItem!=null&&dbItem.Enabled&&toolNames.SequenceEqual(["importPhpStudy","databaseManagerMenu","runtimeLogsMenu"]),$"{lang} tools menu holds actions only: {string.Join(" | ",toolsMenu.Items.OfType<ToolStripMenuItem>().Select(i=>i.Text))}");
                var first=toolsMenu.Items[0];
                Check(first.Name=="importPhpStudy"&&first.Text!.Contains("PHPStudy"),$"{lang} tools menu first item: {first.Text} | {string.Join(" | ",toolsMenu.Items.OfType<ToolStripMenuItem>().Skip(1).Select(i=>i.Text))}");
                for(var i=0;i<10;i++){Application.DoEvents();Thread.Sleep(20);}
                var bounds=Rectangle.Union(form.Bounds,toolsMenu.Bounds);
                using(var shot=new Bitmap(bounds.Width,bounds.Height)){using(var g=Graphics.FromImage(shot))g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);shot.Save(Path.Combine(output,$"tools-menu-{lang}.png"));}
                bool importOpened=false;
                using var importTimer=new System.Windows.Forms.Timer{Interval=150};
                importTimer.Tick+=(_,_)=>{var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form);if(dialog==null)return;importTimer.Stop();importOpened=Desc(dialog).OfType<DataGridView>().Any();dialog.Close();};
                importTimer.Start();first.PerformClick();
                var until=DateTime.UtcNow.AddSeconds(15);while(!importOpened&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
                importTimer.Stop();
                Check(importOpened,$"{lang} tools menu opens PHPStudy import dialog");
            }
            form.Close();
        }
        File.WriteAllLines(Path.Combine(output,"result.txt"),Lines);
    }
    static ToolStripDropDown? ToolStripManager_OpenDropDown()
    {
        foreach(var handle in TopWindows())
            if(Control.FromHandle(handle) is ToolStripDropDown d&&d.Visible)return d;
        return null;
    }
    static List<IntPtr> TopWindows()
    {
        var list=new List<IntPtr>();var pid=Environment.ProcessId;
        EnumWindows((h,_)=>{GetWindowThreadProcessId(h,out var p);if(p==pid)list.Add(h);return true;},IntPtr.Zero);return list;
    }
    delegate bool EnumProc(IntPtr hwnd,IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,IntPtr extra);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out int pid);
}
