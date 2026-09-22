using YikaiLocal;
using System.Reflection;
using System.Text.Json;
using System.Net;
using System.Diagnostics;
partial class Program
{
    static readonly string Output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../evidence"));
    static readonly string Root=Path.Combine(Path.GetTempPath(),"yikai project tools "+Guid.NewGuid().ToString("N"));
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static IEnumerable<Control> Desc(Control root){foreach(Control c in root.Controls){yield return c;foreach(var n in Desc(c))yield return n;}}
    static T Named<T>(Control root,string name) where T:Control=>Desc(root).OfType<T>().Single(c=>c.Name==name);
    static void Call(object instance,string method,params object?[] args)=>instance.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(instance,args);
    static void Write(string file,string text=""){var path=Path.Combine(Root,file);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text);}
    static void Pump(Task task){var until=DateTime.UtcNow.AddSeconds(15);while(!task.IsCompleted&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}if(!task.IsCompleted)throw new TimeoutException();task.GetAwaiter().GetResult();Application.DoEvents();}
    static void Capture(Form form,string name){Application.DoEvents();using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(Output,name+".png"));}
    [STAThread] static int Main(string[] args){try{Directory.CreateDirectory(Output);ApplicationConfiguration.Initialize();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Control.CheckForIllegalCrossThreadCalls=true;
        if(args.Contains("--live")){var settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText("D:\\yikai\\config\\panel.json"),Settings.Json)!;settings.Root="D:\\yikai";var runtime=new Runtime(settings);runtime.Adopt();using var live=new MainForm(settings,runtime,true);SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());live.Show();Pump((Task)typeof(MainForm).GetMethod("MeasureSelectedDirectory",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(live,[true])!);Capture(live,"live-panel");Console.WriteLine("Live directory: "+Named<Label>(live,"directoryDetail").Text);Console.WriteLine("Live admin scan: "+string.Join(", ",AdminEntryDetector.Scan(settings.Sites[0].Directory).Entries.Select(x=>x.Path+" / "+x.Source)));return 0;}
        Functional();UpdateChecks();using(var loop=new Form{ShowInTaskbar=false,WindowState=FormWindowState.Minimized}){loop.Shown+=(_,_)=>{Ui();loop.Close();};Application.Run(loop);}Console.WriteLine("Fixture: "+Root);Console.WriteLine("Evidence: "+Output);return 0;
    }catch(Exception e){Console.WriteLine(e);return 1;}}
    static void Functional()
    {
        Write("cms/config/version.php","CMS_VERSION");Write("cms/control/login.php");Write("cms/control/index.php");Write("cms/control/includes/header.php");
        var cms=AdminEntryDetector.Scan(Path.Combine(Root,"cms"));Check(cms.Entries.Count==1&&cms.Entries[0].Path=="/control/"&&cms.Entries[0].Confidence==100,"Detect renamed YikaiCMS admin directory");
        Write("wordpress/wp-admin/index.php");Write("wordpress/wp-login.php");Check(AdminEntryDetector.Scan(Path.Combine(Root,"wordpress")).Entries.Single().Path=="/wp-admin/","Detect WordPress admin");
        Write("ambiguous/admin/index.php");Write("ambiguous/manage/login.php");Check(AdminEntryDetector.Scan(Path.Combine(Root,"ambiguous")).Entries.Count==2,"Retain multiple admin candidates for choice");
        Write("configured/.env","BACKEND_PATH=/index.php?s=/console\nDB_PASSWORD=not-a-route");Check(AdminEntryDetector.Scan(Path.Combine(Root,"configured")).Entries.Single().Path=="/index.php?s=/console","Read explicit backend route from .env without PHP execution");
        Write("none/install/index.php");Check(AdminEntryDetector.Scan(Path.Combine(Root,"none")).Entries.Count==0,"Installer is never an admin candidate");
        Check(AdminEntryDetector.Url("http://127.0.0.1:8081/","admin/")=="http://127.0.0.1:8081/admin/","Saved relative admin follows the current domain and port");
        foreach(var value in new[]{"https://elsewhere.test/admin","//elsewhere.test/","../admin","/%2e%2e/x","/%5cevil"})Check(!AdminEntryDetector.TryNormalize(value,out _),"Reject non-local admin path: "+value);
        Write("size/a","12345");Write("size/nested/b","1234567");Write("size/.hidden","123");var sizeRoot=Path.Combine(Root,"size");
        var junction=Path.Combine(sizeRoot,"nested","loop");var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-Command");start.ArgumentList.Add("New-Item -ItemType Junction -Path '"+junction.Replace("'","''")+"' -Target '"+sizeRoot.Replace("'","''")+"' | Out-Null");using(var p=Process.Start(start)!){p.WaitForExit();Check(p.ExitCode==0,"Create isolated directory-link cycle");}
        var stats=DirectorySize.Measure(sizeRoot);Check(stats.Bytes==15&&stats.Files==3&&stats.Skipped==1&&!stats.Limited,"Folder size includes hidden files and skips link cycles");
        Check(DirectorySize.Measure(sizeRoot,maxEntries:1).Limited,"Bounded folder scan reports partial totals");
        using(var stop=new CancellationTokenSource()){stop.Cancel();try{DirectorySize.Measure(sizeRoot,stop.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Console.WriteLine("PASS Folder scan cancellation");}}
        var old="{\"root\":\"test\",\"sites\":[{\"domain\":\"old.yikai\"}]}";var settings=JsonSerializer.Deserialize<Settings>(old,Settings.Json)!;Check(settings.SidebarWidth==360&&settings.Sites[0].AdminPath=="","Older project configuration accepts new optional fields");
        using var offline=new HttpClient(new FakeHandler(_=>new HttpResponseMessage(HttpStatusCode.NotFound)));try{PanelUpdate.CheckAsync(offline,CancellationToken.None).GetAwaiter().GetResult();throw new Exception("404 was accepted");}catch(HttpRequestException){Console.WriteLine("PASS Unconfigured update endpoint is handled as unavailable");}
        using var latest=new HttpClient(new FakeHandler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"version\":\"0.6.0\"}")}));Check(PanelUpdate.CheckAsync(latest,CancellationToken.None).GetAwaiter().GetResult()==null,"Same version offers no upgrade");
        using var invalid=new HttpClient(new FakeHandler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"version\":\"0.7.0\",\"downloadUrl\":\"http://bad.test/a.exe\",\"sha256\":\"bad\"}")}));try{PanelUpdate.CheckAsync(invalid,CancellationToken.None).GetAwaiter().GetResult();throw new Exception("Invalid manifest accepted");}catch(InvalidDataException){Console.WriteLine("PASS Invalid update metadata rejected");}
    }
    static void Ui()
    {
        var settings=new Settings{Root=Root,Sites=[new Site{Id="a",Domain="demo.yikai",Directory=Path.Combine(Root,"cms"),Enabled=false,Template="import"},new Site{Id="b",Domain="files.yikai",Directory=Path.Combine(Root,"size"),Enabled=false,Template="php"}]};settings.Save();using var form=new MainForm(settings,new Runtime(settings),true);form.Show();Application.DoEvents();
        Console.WriteLine("UI DPI "+form.DeviceDpi);
        foreach(var language in new[]{0,1,2}){
            Named<Choice>(form,"language").SelectedIndex=language;Application.DoEvents();var split=Named<ProjectSplitView>(form,"projectSplit");
            foreach(var size in new[]{new Size(1320,820),form.MinimumSize,new Size(1600,1000)}){
                form.Size=size;Application.DoEvents();foreach(var distance in new[]{split.Panel1MinSize,split.Width-split.SplitterWidth-split.Panel2MinSize}){
                    split.SplitterDistance=distance;Application.DoEvents();
                    var buttons=Desc(Named<TableLayoutPanel>(form,"environmentToolbar")).OfType<Button>().Concat(Desc(Named<TableLayoutPanel>(form,"projectCard")).OfType<Button>());
                    foreach(var button in buttons){Check(button.Right<=button.Parent!.ClientSize.Width&&button.Bottom<=button.Parent.ClientSize.Height,$"{language} {size.Width}/{distance} fits: {button.Text}");using var g=button.CreateGraphics();var measured=TextRenderer.MeasureText(g,button.Text,button.Font,new Size(10000,button.Height),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;Check(measured<=button.Width-(button.Image==null?30:66),"Full text: "+button.Text);}
                }
            }
            form.Size=new Size(1320,820);split.SplitterDistance=360;Application.DoEvents();Capture(form,language+"-main");
        }
        var divider=Named<ProjectSplitView>(form,"projectSplit");var grip=Named<Control>(form,"sidebarDivider");Call(grip,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,4,20,0));Call(grip,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,64,20,0));Call(grip,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,4,20,0));Check(Settings.Load(Root).SidebarWidth==420,"Mouse drag events persist sidebar width");Call(grip,"OnKeyDown",new KeyEventArgs(Keys.Left));Check(Settings.Load(Root).SidebarWidth==404,"Keyboard resizes and persists sidebar");Call(grip,"OnKeyDown",new KeyEventArgs(Keys.Right));
        Named<Choice>(form,"language").SelectedIndex=0;Application.DoEvents();Check(Named<ProjectSplitView>(form,"projectSplit").SplitterDistance==420,"Language rebuild restores sidebar width");
        var site=settings.Sites[0];Call(form,"RecordBackend",site,"control/","manual");Check(Settings.Load(Root).Sites[0].AdminPath=="/control/","Admin path choice persists per project");
        var firstScan=(Task)typeof(MainForm).GetMethod("MeasureSelectedDirectory",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,[true])!;
        Named<ListBox>(form,"projects").SelectedIndex=1;var secondScan=(Task)typeof(MainForm).GetMethod("MeasureSelectedDirectory",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,[true])!;Pump(Task.WhenAll(firstScan,secondScan));Check(Named<Label>(form,"directoryDetail").Text.Contains("15 B"),"Rapid project switching cancels old scan and displays the correct folder size");
        Named<ListBox>(form,"projects").SelectedIndex=0;Application.DoEvents();Check(!Named<Label>(form,"directoryDetail").Text.Contains("15 B"),"Switching projects does not reuse another folder total");
        foreach(var dialogLanguage in new[]{0,1,2}){Named<Choice>(form,"language").SelectedIndex=dialogLanguage;Application.DoEvents();foreach(var method in new[]{"ShowAbout","ShowUpgrade","ChooseBackend","ProjectDialog"}){
            bool captured=false;using var timer=new System.Windows.Forms.Timer{Interval=100};timer.Tick+=(_,_)=>{var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(x=>x.Owner==form);if(dialog==null)return;timer.Stop();foreach(var button in Desc(dialog).OfType<Button>()){Check(button.Left>=0&&button.Right<=button.Parent!.ClientSize.Width&&button.Bottom<=button.Parent.ClientSize.Height,"Dialog button bounds: "+button.Text);using var g=button.CreateGraphics();Check(TextRenderer.MeasureText(g,button.Text,button.Font,new Size(10000,button.Height),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width<=button.Width-(button.Width<60?12:30),"Dialog text: "+button.Text);}Capture(dialog,dialogLanguage+"-"+method);captured=true;dialog.DialogResult=DialogResult.Cancel;dialog.Close();};timer.Start();if(method=="ChooseBackend")Call(form,method,site,AdminEntryDetector.Scan(Path.Combine(Root,"ambiguous")));else if(method=="ProjectDialog")Call(form,method,(object?)null);else Call(form,method);Check(captured,method+" opens and closes");
        }
        }
    }
    sealed class FakeHandler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(response(request));}
}
