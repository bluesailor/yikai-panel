namespace YikaiLocal;

// 端口排查：列出面板使用的每个端口和当前占用情况。占用者不是面板进程时显示进程名和路径，
// 支持一键复制诊断文本，方便远程排障（启动失败多数是端口被其他程序占用）。
public sealed partial class MainForm
{
    void ShowPortDiagnostics()
    {
        if(busy)return;
        using var dialog=new Form{Name="portDiagnostics",Text=T("端口排查","Port check","ポート確認"),ClientSize=new Size(760,564),Font=Font,Icon=Icon,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,18,24,18),ColumnCount=1,RowCount=4};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,88));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
        // footer 行高要按“按钮 + 上下外边距 + 面板上内边距”算：40 + 8 + 10 = 58，取 64 留余量。
        // 行高不够时按钮会被父容器裁掉底部（100% 下差 6px，显示缩放下同比例）。
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
        dialog.Controls.Add(layout);

        var intro=L(T("面板使用的本地端口和当前占用情况。启动失败时先看这里：有端口被其他程序占用时，停掉占用它的程序再启动。\nMySQL 端口在首次初始化前会自动避开被占用的端口；之后端口固定，被占用时会明确提示占用者。",
            "Local ports the panel uses and who holds them. If startup fails, look here first: stop the program holding the port, then start again.\nMySQL ports avoid occupied ports before first initialization; afterwards the port is fixed and the occupier is named.",
            "パネルが使うローカルポートと占有状況です。起動失敗時はまずここを確認し、ポートを占有するプログラムを停止してください。\nMySQL ポートは初期化前のみ自動回避し、以降は固定です。占有時は対象を表示します。"),9);
        intro.ForeColor=Muted;intro.TextAlign=ContentAlignment.TopLeft;intro.AutoEllipsis=false;layout.Controls.Add(intro,0,0);

        var grid=new DataGridView{Name="portList",Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,Margin=new Padding(0,6,0,6)};
        grid.ColumnHeadersHeight=36;grid.RowTemplate.Height=34;
        grid.Columns.Add("portPurpose",T("用途","Purpose","用途"));
        grid.Columns.Add("portNumber",T("端口","Port","ポート"));
        grid.Columns.Add("portState",T("状态","State","状態"));
        grid.Columns[1].FillWeight=18;grid.Columns[2].FillWeight=52;
        layout.Controls.Add(grid,0,1);

        var status=L("",9);status.Name="portStatus";status.ForeColor=Muted;status.TextAlign=ContentAlignment.TopLeft;layout.Controls.Add(status,0,2);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,10,0,0)};layout.Controls.Add(footer,0,3);
        var refresh=new ThemeButton{Name="refreshPorts",Text=T("刷新","Refresh","更新"),Size=new Size(120,40),Margin=new Padding(0,0,10,0)};
        var copy=new ThemeButton{Name="copyPortReport",Text=T("复制诊断信息","Copy diagnostics","診断情報をコピー"),Size=new Size(200,40),Margin=new Padding(0,0,10,0)};
        var close=new ThemeButton{Text=T("关闭","Close","閉じる"),Size=new Size(120,40),Margin=Padding.Empty,DialogResult=DialogResult.Cancel};
        footer.Controls.AddRange([close,copy,refresh]);dialog.CancelButton=close;

        string PortUseLabel(Runtime.PortUse use)=>use.Key switch
        {
            "php-db"=>T("数据库页面 PHP","Database page PHP","DB ページ PHP"),
            "dbpage"=>T("数据库页面","Database page","DB ページ"),
            "mysql80"=>"MySQL 8.0",
            "mysql57"=>"MySQL 5.7",
            _=>settings.Sites.FirstOrDefault(s=>use.Key=="https-"+s.Id||use.Key=="http-"+s.Id||use.Key=="php-"+s.Id) is { } site
                ?use.Key.StartsWith("https-")?T("网站 HTTPS · ","Website HTTPS · ","サイト HTTPS · ")+site.Domain
                :use.Key.StartsWith("http-")?T("网站 · ","Website · ","サイト · ")+site.Domain
                :T("PHP · ","PHP · ","PHP · ")+site.Domain
                :use.Key
        };
        (string Text,Color Color,string Tip) PortState(Runtime.PortUse use)
        {
            if(runtime.ServiceRunning(use.Service))
            {
                var pid=runtime.ServicePid(use.Service);
                return (T($"运行中 · 面板 PID {pid}",$"Running · panel PID {pid}",$"実行中 · パネル PID {pid}"),Palette.Success,"");
            }
            if(PortDiagnostics.ListenerPid(use.Port) is { } holder)
            {
                var described=PortDiagnostics.Describe(holder).Split('\n');
                return (T("被占用","In use","使用中")+" · "+described[0],Palette.Brand,described.Length>1?described[1]:"");
            }
            return (T("空闲","Free","空き"),Palette.Secondary,"");
        }
        void RefreshPorts()
        {
            grid.Rows.Clear();
            foreach(var use in runtime.PortUses())
            {
                var (text,color,tip)=PortState(use);
                var row=grid.Rows.Add(PortUseLabel(use),use.Port.ToString(),text);
                grid.Rows[row].Cells[2].Style.ForeColor=color;
                if(tip.Length>0)grid.Rows[row].Cells[2].ToolTipText=tip;
            }
        }
        refresh.Click+=(_,_)=>{RefreshPorts();status.Text=T("已刷新。","Refreshed.","更新しました。");};
        copy.Click+=(_,_)=>{try{Clipboard.SetText(runtime.PortReport());status.Text=T("诊断信息已复制，可直接发给支持人员。","Diagnostics copied; send it to support.","診断情報をコピーしました。サポートに送れます。");}catch(System.Runtime.InteropServices.ExternalException){status.Text=T("复制失败，请稍后重试。","Copy failed; retry in a moment.","コピーに失敗しました。再試行してください。");}};
        RefreshPorts();
        Palette.Show(dialog,this);
    }
}
