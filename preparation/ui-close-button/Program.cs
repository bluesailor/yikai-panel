using System.Reflection;
using YikaiLocal;

// 渲染“关于”“升级”两个对话框，用于核对关闭按钮的文字位置。不启动任何服务，只读夹具根目录。
static class Program
{
    static IEnumerable<Control> Desc(Control root){foreach(Control c in root.Controls){yield return c;foreach(var n in Desc(c))yield return n;}}
    static void Inspect(MainForm form,string dialogName,string method,string output,string file)
    {
        bool done=false;
        using var timer=new System.Windows.Forms.Timer{Interval=150};
        timer.Tick+=(_,_)=>{
            var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Name==dialogName&&f.Visible);if(dialog==null)return;timer.Stop();
            for(var i=0;i<10;i++){Application.DoEvents();Thread.Sleep(15);}
            using(var img=new Bitmap(dialog.Width,dialog.Height))
            {
                dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));
                img.Save(Path.Combine(output,file));
            }
            var buttons=Desc(dialog).OfType<ThemeButton>().Where(b=>b.Text.Length>0).ToList();
            foreach(var b in buttons)
            {
                var icon=b.Image==null?0:(int)Math.Round(16*b.DeviceDpi/96f);
                var gap=b.Image==null?0:(int)Math.Round(8*b.DeviceDpi/96f);
                var inset=(int)Math.Round((b.Width<60?4:10)*b.DeviceDpi/96f);
                var textWidth=TextRenderer.MeasureText(b.Text,b.Font,new Size(10000,b.Height),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Width;
                var contentWidth=textWidth+icon+gap;
                var left=Math.Max(inset,(b.Width-contentWidth)/2);
                var centerOffset=Math.Abs(left-inset);
                Console.WriteLine($"{file} {b.Name,-14} {b.Width,4}x{b.Height} 居中偏移={centerOffset,3}px 内容宽={contentWidth,3}px 左内边距={inset}px");
            }
            dialog.DialogResult=DialogResult.Cancel;dialog.Close();done=true;
        };
        timer.Start();
        typeof(MainForm).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null);
        var until=DateTime.UtcNow.AddSeconds(20);while(!done&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
    }
    [STAThread]
    static void Main(string[] args)
    {
        var root=args[0];var output=args[1];Directory.CreateDirectory(output);
        ApplicationConfiguration.Initialize();
        foreach(var lang in new[]{"zh","ja"})
        {
            var settings=Settings.Load(root);settings.Language=lang;settings.FontSize=10f;settings.Theme="light";
            var runtime=new Runtime(settings);
            using var form=new MainForm(settings,runtime,true,false){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
            form.Show();Application.DoEvents();
            Inspect(form,"aboutDialog","ShowAbout",output,$"about-{lang}.png");
            Inspect(form,"upgradeDialog","ShowUpgrade",output,$"upgrade-{lang}.png");
            form.Close();Application.DoEvents();
        }
    }
}
