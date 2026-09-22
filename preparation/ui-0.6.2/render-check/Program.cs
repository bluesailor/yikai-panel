using YikaiLocal;
using System.Reflection;
using System.Text.Json;

class Program
{
    static readonly string Output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../evidence"));
    static IEnumerable<Control> Desc(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Desc(c))yield return child;}}
    static T Named<T>(Control root,string name) where T:Control=>Desc(root).OfType<T>().Single(c=>c.Name==name);
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Capture(Form form,string name){Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(Output,name+".png"));}
    static int TextHeight(Control c){using var g=c.CreateGraphics();return TextRenderer.MeasureText(g,c.Text,c.Font,new Size(10000,1000),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Height;}
    static void ButtonBounds(Button button)
    {
        Require(button.Left>=0&&button.Top>=0&&button.Right<=button.Parent!.ClientSize.Width&&button.Bottom<=button.Parent.ClientSize.Height,"Button bounds: "+button.Text);
        using var g=button.CreateGraphics();int reserved=(int)Math.Round((button.Image==null?20:44)*button.DeviceDpi/96f);
        Require(TextRenderer.MeasureText(g,button.Text,button.Font,new Size(10000,1000),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width<=button.Width-reserved,"Button text: "+button.Text);
    }
    static void StatusBounds(MainForm form)
    {
        foreach(var name in new[]{"projectRunStatus","projectRunProgress"}){
            var label=Named<Label>(form,name);
            Require(label.ClientSize.Height>=TextHeight(label)+2,name+" text clips");
            Require(label.Bottom<=label.Parent!.ClientSize.Height,name+" exceeds its parent");
            Console.WriteLine(name+" label="+label.Height+" text="+TextHeight(label)+" / "+label.Text);
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try {
            Directory.CreateDirectory(Output);ApplicationConfiguration.Initialize();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Control.CheckForIllegalCrossThreadCalls=true;SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            if(args.Contains("--live")){
                var settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(@"D:\yikai\config\panel.json"),Settings.Json)!;settings.Root=@"D:\yikai";
                var runtime=new Runtime(settings);runtime.Adopt();using var form=new MainForm(settings,runtime,true);form.Show();Application.DoEvents();
                StatusBounds(form);Capture(form,"live-panel");Console.WriteLine("Live panel DPI "+form.DeviceDpi);return 0;
            }
            var root=Path.Combine(Path.GetTempPath(),"yikai-icons-feedback-"+Guid.NewGuid().ToString("N"));
            var fixture=new Settings{Root=root,Sites=[new Site{Id="preview",Domain="demo.yikai",Directory=Path.Combine(root,"site"),Enabled=false,Template="php"}]};fixture.Save();
            using var preview=new MainForm(fixture,new Runtime(fixture),true);preview.Show();Application.DoEvents();
            Console.WriteLine("DPI "+preview.DeviceDpi);
            foreach(var code in new[]{0,1,2}){
                Named<Choice>(preview,"language").SelectedIndex=code;Application.DoEvents();
                foreach(var size in new[]{new Size(1320,820),preview.MinimumSize,new Size(1600,1000)}){
                    preview.Size=size;Application.DoEvents();
                    StatusBounds(preview);
                    foreach(var button in Desc(Named<TableLayoutPanel>(preview,"environmentToolbar")).OfType<Button>())ButtonBounds(button);
                    var workspace=Named<TableLayoutPanel>(preview,"projectWorkspace");
                    foreach(Control c in workspace.Controls)Require(c.Bottom<=workspace.ClientSize.Height-workspace.Padding.Bottom,"Workspace bottom clips: "+c.Name);
                    Console.WriteLine("PASS toolbar and status "+code+" "+size);
                }
                preview.Size=new Size(1320,820);Application.DoEvents();
                Named<Label>(preview,"projectRunStatus").Text=code==0?"2 个项目正在运行":code==1?"2 projects running":"2 件のプロジェクトが起動中";
                StatusBounds(preview);Capture(preview,code+"-main");
                foreach(var method in new[]{"ShowAbout","ShowUpgrade"}){
                    bool captured=false;using var timer=new System.Windows.Forms.Timer{Interval=100};
                    timer.Tick+=(_,_)=>{
                        var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==preview);if(dialog==null)return;timer.Stop();
                        foreach(var button in Desc(dialog).OfType<Button>()){ButtonBounds(button);Require(button.Image!=null,"Missing icon: "+button.Text);}
                        if(method=="ShowAbout")Require(Named<LinkLabel>(dialog,"feedbackEmail").Text=="support@yikay.com","Feedback email differs");
                        Capture(dialog,code+"-"+method);captured=true;dialog.DialogResult=DialogResult.Cancel;dialog.Close();
                    };
                    timer.Start();typeof(MainForm).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(preview,null);
                    Require(captured,"Dialog did not render: "+method);Console.WriteLine("PASS "+code+" "+method);
                }
            }
            Console.WriteLine("PASS native UI render and geometry checks");return 0;
        }catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
