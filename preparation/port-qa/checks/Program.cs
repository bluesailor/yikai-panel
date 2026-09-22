using System.Reflection;
using System.Net;
using System.Net.Sockets;
using YikaiLocal;

// 端口排查窗口的界面与内容检查。不启动任何服务；用检查进程自己的监听充当“外部占用者”。
static class Program
{
    static readonly List<string> Lines=[];
    static IEnumerable<Control> Desc(Control root){foreach(Control c in root.Controls){yield return c;foreach(var n in Desc(c))yield return n;}}
    static void Check(bool ok,string text){Lines.Add((ok?"PASS ":"FAIL ")+text);Console.WriteLine((ok?"PASS ":"FAIL ")+text);if(!ok)Environment.ExitCode=1;}
    static Type PaletteType=>typeof(Site).Assembly.GetType("YikaiLocal.Palette")!;

    // 窗口布局检查：可操作控件在窗口内、按钮文字放得下、标签不被行高截断（与 ui-project-badges 的规则一致）。
    static void CheckLayout(string name,Form dialog,string output)
    {
        var client=dialog.ClientSize;
        var outside=Desc(dialog).Where(c=>c.Visible&&c is Button or TextBox or CheckBox or ListBox or InputFrame or DataGridView)
            .Where(c=>{var r=dialog.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));return r.Left<-1||r.Top<-1||r.Right>client.Width+1||r.Bottom>client.Height+1;})
            .Select(c=>c.Name.Length>0?c.Name:c.Text).ToList();
        Check(outside.Count==0,$"{name}: controls inside the window {string.Join(" | ",outside)}");
        // 控件还必须在父容器内：footer 行高按“按钮+外边距+内边距”算错时，按钮底部会被 FlowLayoutPanel 裁掉，
        // 而“在窗口内”这条检查发现不了（父容器本身在窗口内）。
        var clipped=Desc(dialog).Where(c=>c.Visible&&c is Button or TextBox or Choice or CheckBox or ListBox or InputFrame or DataGridView&&c.Parent is not null&&c.Parent is not Form)
            .Where(c=>{var r=c.Parent!.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));return r.Bottom>c.Parent.ClientSize.Height+1||r.Top<-1||r.Right>c.Parent.ClientSize.Width+1||r.Left<-1;})
            .Select(c=>{var r=c.Parent!.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));return $"{c.Name}{(c.Name.Length>0?"":"("+c.Text+")")} bottom={r.Bottom}>{c.Parent.ClientSize.Height}";}).ToList();
        Check(clipped.Count==0,$"{name}: controls not clipped by their container {string.Join(" | ",clipped)}");
        var cramped=Desc(dialog).OfType<Button>().Where(b=>b.Visible&&b.Text.Length>1)
            .Where(b=>TextRenderer.MeasureText(b.Text,b.Font,new Size(4000,100),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Width+(int)Math.Round(((b.Image!=null?24:0)+16)*b.DeviceDpi/96f)>b.Width)
            .Select(b=>$"{b.Text}({b.Width})").ToList();
        Check(cramped.Count==0,$"{name}: button text fits {string.Join(" | ",cramped)}");
        var cut=Desc(dialog).OfType<Label>().Where(l=>l.Visible&&l.Text.Length>0)
            .Where(l=>{var wraps=!l.AutoEllipsis&&!l.AutoSize;var needed=wraps?TextRenderer.MeasureText(l.Text,l.Font,new Size(Math.Max(1,l.Width-l.Padding.Horizontal),int.MaxValue),TextFormatFlags.WordBreak).Height:TextRenderer.MeasureText(l.Text.Split('\n')[0],l.Font).Height;return l.Height+1<needed;})
            .Select(l=>l.Text.Split('\n')[0]).ToList();
        Check(cut.Count==0,$"{name}: labels not cut {string.Join(" | ",cut)}");
        using var img=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(img,new Rectangle(Point.Empty,dialog.Size));img.Save(Path.Combine(output,$"dialog-{name}.png"));
    }
    static void Inspect(MainForm form,string dialogName,string label,Action open,string output,Action<Form> extra)
    {
        bool done=false;
        using var timer=new System.Windows.Forms.Timer{Interval=150};
        timer.Tick+=(_,_)=>{
            var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Name==dialogName&&f.Visible);if(dialog==null)return;timer.Stop();
            for(var i=0;i<10;i++){Application.DoEvents();Thread.Sleep(15);}
            CheckLayout(label,dialog,output);extra(dialog);
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
        // 夹具端口故意避开面板常用区段，保证除了它以外的端口都是空闲的，断言才稳定。
        var settings0=Settings.Load(root);
        var fixture=settings0.Sites[0].HttpPort;
        var listener=new TcpListener(IPAddress.Loopback,fixture);
        listener.Start();
        try
        {
            foreach(var size in new[]{10f,12f})
            foreach(var lang in new[]{"zh","en","ja"})
            {
                var settings=Settings.Load(root);settings.Language=lang;settings.FontSize=size;settings.Save();
                var runtime=new Runtime(settings);
                using var form=new MainForm(settings,runtime,true,false){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
                form.Show();Application.DoEvents();
                var occupiedWord=lang=="en"?"In use":lang=="ja"?"使用中":"被占用";
                var freeWord=lang=="en"?"Free":lang=="ja"?"空き":"空闲";
                Inspect(form,"portDiagnostics",$"{lang}{size:0}-ports",()=>typeof(MainForm).GetMethod("ShowPortDiagnostics",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null),output,dialog=>{
                    var grid=Desc(dialog).OfType<DataGridView>().Single(g=>g.Name=="portList");
                    var expected=runtime.PortUses();
                    Check(grid.RowCount==expected.Count,$"{lang}{size:0}-ports: one row per panel port ({grid.RowCount} vs {expected.Count})");
                    var occupied=Enumerable.Range(0,grid.RowCount).Where(i=>grid.Rows[i].Cells[2].Value?.ToString()?.Contains(occupiedWord)==true).ToList();
                    Check(occupied.Count==1&&Convert.ToInt32(grid.Rows[occupied[0]].Cells[1].Value)==fixture,$"{lang}{size:0}-ports: exactly the fixture port is marked occupied (port {Convert.ToInt32(grid.Rows[occupied.Count==1?occupied[0]:0].Cells[1].Value)})");
                    if(occupied.Count==1)Check(!string.IsNullOrEmpty(grid.Rows[occupied[0]].Cells[2].ToolTipText),$"{lang}{size:0}-ports: occupied row shows the holder's path");
                    Check(Enumerable.Range(0,grid.RowCount).Any(i=>Convert.ToInt32(grid.Rows[i].Cells[1].Value)==settings.Mysql80Port&&grid.Rows[i].Cells[2].Value?.ToString()==freeWord),$"{lang}{size:0}-ports: idle MySQL port reads free");
                    // 刷新按钮可用；复制的内容与 CLI --ports 共用同一份报告
                    var refresh=Desc(dialog).OfType<Button>().Single(b=>b.Name=="refreshPorts");
                    Check(refresh.Enabled,$"{lang}{size:0}-ports: refresh enabled");
                    var report=runtime.PortReport();
                    Check(report.Contains(fixture.ToString())&&report.Contains("in use by")&&report.Contains(Environment.ProcessId.ToString()),$"{lang}{size:0}-ports: report names the fixture holder");
                });
                form.Close();Application.DoEvents();
            }
            {var reset=Settings.Load(root);reset.Language="zh";reset.FontSize=10f;reset.Save();}
            // P/Invoke 的监听表与 netstat 对账：IPv4 + IPv6 的 LISTEN 行数一致，端口集合一致。
            var ours=PortDiagnostics.Listeners().Select(l=>l.Port).ToHashSet();
            var netstat=new HashSet<int>();
            foreach(var line in System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd","/c netstat -an"){UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true})!.StandardOutput.ReadToEnd().Split('\n'))
            {
                var match=System.Text.RegularExpressions.Regex.Match(line.Trim(),@"^(?:TCP)\s+\S+:(\d+)\s+\S+\s+LISTENING");
                if(match.Success)netstat.Add(int.Parse(match.Groups[1].Value));
            }
            Check(ours.SetEquals(netstat),$"listener table matches netstat: only-panel {string.Join(",",ours.Except(netstat).Order())} only-netstat {string.Join(",",netstat.Except(ours).Order().Take(12))} ({ours.Count} vs {netstat.Count})");
        }
        finally{listener.Stop();}
        // 常用端口的 URL 规则与端口校验：runtime 逻辑，直接在检查里断言
        Check(Runtime.WebUrl("http","demo.yikai",80)=="http://demo.yikai/",$"URL omits :80 -> {Runtime.WebUrl("http","demo.yikai",80)}");
        Check(Runtime.WebUrl("https","demo.yikai",443)=="https://demo.yikai/",$"URL omits :443 -> {Runtime.WebUrl("https","demo.yikai",443)}");
        Check(Runtime.WebUrl("http","demo.yikai",8082)=="http://demo.yikai:8082/",$"URL keeps other ports -> {Runtime.WebUrl("http","demo.yikai",8082)}");
        {
            var s=Settings.Load(root);
            Check(s.PortProblem(0)=="range"&&s.PortProblem(70000)=="range","port range is validated");
            Check(s.PortProblem(s.DbManagerPort)=="panel","the panel's own port is rejected");
            Check(s.PortProblem(s.Mysql80Port)=="mysql","a panel MySQL port is rejected");
            var first=s.Sites.First();
            Check(s.PortProblem(first.HttpPort,first)==null,"a project's own port is allowed when editing it");
            Check(s.PortProblem(first.HttpPort)=="site","another project's port is rejected");
            Check(s.PortProblem(fixture)=="site","the fixture port (another project) is rejected");
        }
        File.WriteAllLines(Path.Combine(output,"verification.txt"),Lines);
        Console.WriteLine($"{Lines.Count(l=>l.StartsWith("PASS"))}/{Lines.Count} passed");
    }
}
