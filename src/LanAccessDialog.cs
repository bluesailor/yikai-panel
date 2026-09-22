using System.Diagnostics;

namespace YikaiLocal;

// 局域网访问：一个开关 + 同事要用的地址 + Windows 防火墙入站规则。
public sealed partial class MainForm
{
    void ShowLanAccess()
    {
        if(busy)return;
        using var dialog=new Form{Name="lanAccess",Text=T("局域网访问","LAN access","LAN アクセス"),ClientSize=new Size(720,560),Font=Font,Icon=Icon,AutoScaleMode=AutoScaleMode.Dpi,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,18,24,18),ColumnCount=1,RowCount=7};
        foreach(var h in new[]{40,76,44,48})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        // footer 行高 ≥ 按钮 40 + 外边距 8 + 面板上内边距 10；56 会裁掉按钮底部 2px
        foreach(var h in new[]{44,64})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        dialog.Controls.Add(layout);

        var enable=new CheckBox{Name="enableLanAccess",Text=T("允许同一局域网内的其他电脑访问本机项目","Let other computers on this network open my projects","同じ LAN の他の PC からプロジェクトを開けるようにする"),Checked=settings.LanAccess,Dock=DockStyle.Fill,Margin=Padding.Empty};
        layout.Controls.Add(enable,0,0);
        var note=L(T("只放开项目的网站端口。数据库管理页面、MySQL 和 PHP 仍然只在本机可用。\n同事用下面的 http 地址访问；.yikai 域名和 HTTPS 证书只对本机有效，局域网内请用 http 地址。","Only the project web ports are opened. The database manager, MySQL and PHP stay on this computer.\nColleagues use the http addresses below; the .yikai domain and the local certificate only work on this computer.","公開するのはプロジェクトの Web ポートだけです。DB 管理、MySQL、PHP はこの PC のままです。\n同僚は下の http アドレスを使います。.yikai ドメインと証明書はこの PC 専用です。"),9);
        note.ForeColor=Muted;note.TextAlign=ContentAlignment.TopLeft;note.AutoEllipsis=false;layout.Controls.Add(note,0,1);

        var firewallRow=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};layout.Controls.Add(firewallRow,0,2);
        var allow=new ThemeButton{Name="allowFirewall",Text=T("允许通过 Windows 防火墙","Allow through Windows Firewall","Windows ファイアウォールで許可"),Size=new Size(290,38),Margin=new Padding(0,0,10,0)};
        var block=new ThemeButton{Name="removeFirewall",Text=T("移除防火墙规则","Remove firewall rules","規則を削除"),Size=new Size(200,38),Margin=Padding.Empty};
        firewallRow.Controls.AddRange([allow,block]);
        var firewallState=L("",9);firewallState.Name="firewallState";firewallState.ForeColor=Muted;firewallState.TextAlign=ContentAlignment.TopLeft;firewallState.AutoEllipsis=false;layout.Controls.Add(firewallState,0,3);

        var addresses=new TextBox{Name="lanAddresses",Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,WordWrap=false,BorderStyle=BorderStyle.FixedSingle,Font=CreateCodeFont(),Margin=new Padding(0,6,0,6)};
        layout.Controls.Add(addresses,0,4);
        var status=L("",9);status.Name="lanStatus";status.ForeColor=Muted;status.TextAlign=ContentAlignment.TopLeft;status.AutoEllipsis=false;layout.Controls.Add(status,0,5);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,10,0,0)};layout.Controls.Add(footer,0,6);
        var copy=new ThemeButton{Name="copyLanAddresses",Text=T("复制地址","Copy addresses","アドレスをコピー"),Size=new Size(160,40),Margin=new Padding(0,0,10,0)};
        var close=new ThemeButton{Text=T("关闭","Close","閉じる"),Size=new Size(120,40),Margin=Padding.Empty,DialogResult=DialogResult.Cancel};
        footer.Controls.AddRange([close,copy]);dialog.CancelButton=close;

        bool working=false;
        void Refresh()
        {
            var ips=Runtime.LocalAddresses();
            addresses.Text=settings.LanAccess
                ?(ips.Count==0?T("没有找到局域网 IP 地址。","No network address found.","LAN の IP アドレスが見つかりません。")
                    :string.Join(Environment.NewLine,settings.Sites.SelectMany(site=>ips.Select(ip=>$"{site,-22}{Runtime.WebUrl("http",ip,site.HttpPort)}"))))
                :T("开启后这里显示同事可以访问的地址。","Turn it on to see the addresses to share.","有効にすると共有できるアドレスが表示されます。");
            var allowed=runtime.FirewallAllowed();
            firewallState.Text=allowed
                ?T("Windows 防火墙已放行面板的 Web 服务器（专用网络、域网络）。","Windows Firewall allows the panel's web server on private and domain networks.","Windows ファイアウォールで許可済み（プライベート／ドメイン）。")
                :T("Windows 防火墙尚未放行，别的电脑可能连不上；点上面的按钮添加规则（会弹出管理员确认）。","Windows Firewall has no rule yet, so other computers may not connect. Add one with the button above (asks for administrator).","ファイアウォール規則がありません。上のボタンで追加してください（管理者確認あり）。");
            allow.Enabled=!working&&!busy&&!allowed;block.Enabled=!working&&!busy&&allowed;
            copy.Enabled=settings.LanAccess&&Runtime.LocalAddresses().Count>0;enable.Enabled=!working&&!busy;
        }
        enable.CheckedChanged+=async(_,_)=>{
            if(working)return;
            working=true;var wanted=enable.Checked;
            status.Text=T("正在应用…","Applying…","適用しています…");
            try{await Work(()=>runtime.SetLanAccessAsync(wanted));}
            finally
            {
                working=false;
                if(!dialog.IsDisposed)
                {
                    enable.Checked=settings.LanAccess;
                    status.Text=settings.LanAccess
                        ?T("已开启。项目的网站端口现在监听所有网卡。","On. Project web ports now listen on all interfaces.","有効です。プロジェクトの Web ポートは全インターフェースで待ち受けます。")
                        :T("已关闭，只有本机可以访问。","Off. Only this computer can open the projects.","無効です。この PC からのみアクセスできます。");
                    Refresh();
                }
            }
        };
        void Firewall(bool value)
        {
            if(working||busy)return;
            working=true;Refresh();
            try
            {
                var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
                info.ArgumentList.Add("--root");info.ArgumentList.Add(settings.Root);info.ArgumentList.Add("--firewall");info.ArgumentList.Add(value?"on":"off");
                using var process=Process.Start(info)??throw new IOException("Cannot start the firewall helper.");
                process.WaitForExit();
                status.Text=process.ExitCode==0
                    ?(value?T("防火墙规则已添加。","Firewall rule added.","ファイアウォール規則を追加しました。"):T("防火墙规则已移除。","Firewall rules removed.","ファイアウォール規則を削除しました。"))
                    :File.ReadAllText(Path.Combine(settings.Root,"logs","panel-last-error.txt")).Split('\n')[0];
            }
            catch(System.ComponentModel.Win32Exception){status.Text=T("已取消，没有修改防火墙。","Cancelled; the firewall was not changed.","キャンセルしました。ファイアウォールは変更していません。");}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException){status.Text=error.Message;}
            finally{working=false;Refresh();}
        }
        allow.Click+=(_,_)=>Firewall(true);block.Click+=(_,_)=>Firewall(false);
        copy.Click+=(_,_)=>{if(addresses.Text.Length>0){Clipboard.SetText(addresses.Text);status.Text=T("地址已复制。","Addresses copied.","アドレスをコピーしました。");}};
        Refresh();
        dialog.FormClosing+=(_,e)=>{if(working)e.Cancel=true;};
        Palette.Show(dialog,this);
    }
}
