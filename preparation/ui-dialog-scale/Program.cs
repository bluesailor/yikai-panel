using System.Reflection;
using YikaiLocal;

// 诊断：对话框在显示缩放（本机 150%）下的实际几何。
// 背景：窗口没有开启 WinForms 自动缩放，Palette.Show 用 form.Scale(factor) 统一放大。
// 若 TableLayoutPanel 的固定行高没被一起放大，行内容（按钮等）就会被裁掉。
static class Program
{
    static IEnumerable<Control> Desc(Control root){foreach(Control c in root.Controls){yield return c;foreach(var n in Desc(c))yield return n;}}
    static void Report(Form dialog,string label)
    {
        Console.WriteLine($"--- {label}: ClientSize={dialog.ClientSize} DeviceDpi={dialog.DeviceDpi} 缩放={dialog.DeviceDpi/96f:0.##}");
        foreach(var panel in Desc(dialog).OfType<TableLayoutPanel>())
        {
            var rows=string.Join(", ", panel.RowStyles.Cast<RowStyle>().Select(r=>$"{r.SizeType}:{r.Height:0}"));
            var cols=string.Join(", ", panel.ColumnStyles.Cast<ColumnStyle>().Select(c=>$"{c.SizeType}:{c.Width:0}"));
            Console.WriteLine($"    TableLayoutPanel {panel.Name} bounds={panel.Bounds} rows=[{rows}] cols=[{cols}]");
        }
        foreach(var button in Desc(dialog).OfType<Button>())
        {
            var box=dialog.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
            var parent=button.Parent!;
            var parentBox=dialog.RectangleToClient(parent.RectangleToScreen(parent.ClientRectangle));
            var clippedBottom = box.Bottom > parentBox.Bottom;
            Console.WriteLine($"    Button {button.Text,-12} 尺寸={button.Width}x{button.Height} 在窗口内=({box.Left},{box.Top})-({box.Right},{box.Bottom}) 父容器底={parentBox.Bottom} 溢出={clippedBottom} 距窗口底={dialog.ClientSize.Height-box.Bottom}px");
        }
        // 父容器（footer 流式面板）自身几何：确认它到底拿了多高
        foreach(var panel in Desc(dialog).OfType<FlowLayoutPanel>())
        {
            var box=dialog.RectangleToClient(panel.RectangleToScreen(panel.ClientRectangle));
            Console.WriteLine($"    FlowLayoutPanel {panel.Name} 在窗口内=({box.Left},{box.Top})-({box.Right},{box.Bottom}) 高={box.Height} Padding={panel.Padding} Margin={panel.Margin} 子控件数={panel.Controls.Count}");
        }
        foreach(var cell in Desc(dialog).OfType<TableLayoutPanel>().SelectMany(p=>p.Controls.Cast<Control>()))
        {
            var cellBox=dialog.RectangleToClient(cell.RectangleToScreen(cell.ClientRectangle));
            Console.WriteLine($"    单元格子控件 {cell.GetType().Name} {cell.Name} 高={cellBox.Height} Dock={cell.Dock} Margin={cell.Margin}");
        }
    }
    static void Inspect(MainForm form,string dialogName,string method,string output,string file,object[]? arguments=null)
    {
        bool done=false;
        using var timer=new System.Windows.Forms.Timer{Interval=150};
        timer.Tick+=(_,_)=>{
            var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Name==dialogName&&f.Visible);if(dialog==null)return;timer.Stop();
            for(var i=0;i<10;i++){Application.DoEvents();Thread.Sleep(15);}
            Report(dialog,file);
            using(var img=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,file+".png"));}
            dialog.DialogResult=DialogResult.Cancel;dialog.Close();done=true;
        };
        timer.Start();
        typeof(MainForm).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,arguments);
        var until=DateTime.UtcNow.AddSeconds(20);while(!done&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
    }
    [STAThread]
    static void Main(string[] args)
    {
        var root=args[0];var output=args[1];Directory.CreateDirectory(output);
        var language=args.Length>2?args[2]:"zh";   // 三语检查：第三个参数给 en / ja，截图文件名也跟着变
        ApplicationConfiguration.Initialize();
        var settings=Settings.Load(root);settings.Language=language;settings.FontSize=10f;
        var runtime=new Runtime(settings);
        using var form=new MainForm(settings,runtime,true,false){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
        form.Show();Application.DoEvents();
        for(var i=0;i<10;i++){Application.DoEvents();Thread.Sleep(30);}
        // 主窗口整窗截图：官网首页用（mainwindow-<语言>.png）
        using(var window=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(window,new Rectangle(Point.Empty,form.Size));window.Save(Path.Combine(output,"mainwindow-"+language+".png"),System.Drawing.Imaging.ImageFormat.Png);}
        Console.WriteLine($"mainwindow-{language}.png {form.Width}x{form.Height}");
        // 扫描对话框要传扫描的目录：给根目录的 wwwroot 就行，里面放几个子目录才能看到候选列表
        Inspect(form,"scanFolderProjects","ShowScanFolderDialog",output,"scan-folder-"+language,[Path.Combine(root,"wwwroot")]);
        Inspect(form,"portDiagnostics","ShowPortDiagnostics",output,"ports-"+language);
        Inspect(form,"aboutDialog","ShowAbout",output,"about-"+language);
        Inspect(form,"upgradeDialog","ShowUpgrade",output,"upgrade-"+language);
        form.Close();Application.DoEvents();
    }
}
