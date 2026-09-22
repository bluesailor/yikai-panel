namespace YikaiLocal;

// SSL 证书：本地根证书（创建 / 信任 / 移除信任）与按项目开启 HTTPS（自动签发或导入自己的证书）。
public sealed partial class MainForm
{
    void ShowSslDialog(Site? initial=null)
    {
        if(busy||settings.Sites.Count==0)return;
        using var dialog=new Form{Name="sslDialog",Text=T("SSL 证书","SSL certificates","SSL 証明書"),ClientSize=new Size(780,600),Font=Font,Icon=Icon,AutoScaleMode=AutoScaleMode.Dpi,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,18,24,18),ColumnCount=1,RowCount=11};
        int[] rowHeights=[34,58,52,40,48,40,40,48,48,70,62];const int clientHeight=600;
        foreach(var h in rowHeights)layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));dialog.Controls.Add(layout);

        layout.Controls.Add(L(T("本地根证书","Local root certificate","ローカルルート証明書"),11,true),0,0);
        var caStatus=L("",9);caStatus.Name="caStatus";caStatus.ForeColor=Muted;caStatus.TextAlign=ContentAlignment.TopLeft;layout.Controls.Add(caStatus,0,1);
        var caButtons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};layout.Controls.Add(caButtons,0,2);
        ThemeButton Button(string name,string text,int width)=>new(){Name=name,Text=text,Size=new Size(width,40),Margin=new Padding(0,0,10,0)};
        var trust=Button("trustLocalCa",T("信任本地根证书","Trust local root","ルートを信頼"),190);var untrust=Button("untrustLocalCa",T("移除信任","Remove trust","信頼を解除"),130);var folder=Button("openSslFolder",T("打开证书目录","Open folder","フォルダーを開く"),160);
        caButtons.Controls.AddRange([trust,untrust,folder]);

        layout.Controls.Add(L(T("项目 HTTPS","Project HTTPS","プロジェクトの HTTPS"),11,true),0,3);
        var project=new Choice{Name="sslProject",Dock=DockStyle.Top,Height=36,Margin=new Padding(0,0,0,12)};
        foreach(var site in settings.Sites)project.Items.Add(site.ToString()==site.Domain?site.Domain:$"{site} · {site.Domain}");
        project.SelectedIndex=Math.Max(0,settings.Sites.IndexOf(initial??Selected!));layout.Controls.Add(project,0,4);
        var enable=new CheckBox{Name="enableHttps",Text=T("为此项目启用 HTTPS","Enable HTTPS for this project","このプロジェクトで HTTPS を有効化"),Dock=DockStyle.Fill,Margin=Padding.Empty};
        // HTTPS 端口与开关同一行：留空自动（8443 起），填 443 就是常用端口；填了会固定该项目端口，不再自动避让。
        var httpsPort=new TextBox{Name="sslHttpsPort",MaxLength=5};
        var httpsPortLabel=L(T("HTTPS 端口","HTTPS port","HTTPS ポート"),10);httpsPortLabel.Name="sslHttpsPortLabel";
        var enableRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        enableRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));enableRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));enableRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,96));
        enableRow.Controls.Add(enable,0,0);enableRow.Controls.Add(httpsPortLabel,1,0);enableRow.Controls.Add(new InputFrame(httpsPort){Dock=DockStyle.Fill,Margin=Padding.Empty},2,0);
        layout.Controls.Add(enableRow,0,5);
        var sources=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};
        var auto=new RadioButton{Name="certificateAuto",Text=T("自动签发（本地根证书）","Issue automatically (local root)","自動発行（ローカルルート）"),AutoSize=true,Margin=new Padding(0,6,24,0)};
        var custom=new RadioButton{Name="certificateCustom",Text=T("使用自己的证书","Use my own certificate","独自の証明書を使用"),AutoSize=true,Margin=new Padding(0,6,0,0)};
        sources.Controls.AddRange([auto,custom]);layout.Controls.Add(sources,0,6);
        TableLayoutPanel FileRow(string caption,TextBox box,ThemeButton browse)
        {
            var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,8)};
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,52));
            var label=L(caption,10);row.Controls.Add(label,0,0);row.Controls.Add(new InputFrame(box){Dock=DockStyle.Fill,Margin=Padding.Empty},1,0);browse.Dock=DockStyle.Fill;browse.Margin=new Padding(8,0,0,0);row.Controls.Add(browse,2,0);
            return row;
        }
        var certFile=new TextBox{Name="certificateFile",ReadOnly=true,PlaceholderText=T("选择证书文件（.crt / .pem）","Choose a certificate (.crt / .pem)","証明書（.crt / .pem）を選択")};var certBrowse=new ThemeButton{Name="browseCertificate",Text="…"};
        var keyFile=new TextBox{Name="keyFile",ReadOnly=true,PlaceholderText=T("选择私钥文件（.key / .pem，未加密）","Choose the private key (.key / .pem, unencrypted)","秘密鍵（.key / .pem、暗号化なし）を選択")};var keyBrowse=new ThemeButton{Name="browseKey",Text="…"};
        var certRow=FileRow(T("证书文件","Certificate","証明書"),certFile,certBrowse);var keyRow=FileRow(T("私钥文件","Private key","秘密鍵"),keyFile,keyBrowse);
        layout.Controls.Add(certRow,0,7);layout.Controls.Add(keyRow,0,8);
        var certInfo=L("",9);certInfo.Name="certificateInfo";certInfo.ForeColor=Muted;certInfo.TextAlign=ContentAlignment.TopLeft;layout.Controls.Add(certInfo,0,9);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,12,0,0)};layout.Controls.Add(footer,0,10);
        var save=new ThemeButton{Name="saveHttps",Text=T("保存并应用","Save and apply","保存して適用"),Size=new Size(170,40),Margin=Padding.Empty,BackColor=Palette.Accent};
        var renew=new ThemeButton{Name="renewCertificate",Text=T("重新签发","Re-issue","再発行"),Size=new Size(130,40),Margin=new Padding(0,0,10,0)};
        var close=new ThemeButton{Text=T("关闭","Close","閉じる"),Size=new Size(110,40),Margin=new Padding(0,0,10,0),DialogResult=DialogResult.Cancel};
        footer.Controls.AddRange([save,renew,close]);dialog.CancelButton=close;

        bool working=false,shown=false;
        Site Current()=>settings.Sites[project.SelectedIndex];
        // 用不到的选项直接收起，而不是灰掉：深色主题下系统画的灰色单选文字几乎看不清，灰掉的输入框看起来又像能填。
        // 没开 HTTPS 时不显示证书来源；选了“使用自己的证书”才显示证书和私钥两行；窗口高度跟着收放。
        void Relayout()
        {
            bool[] visible=[true,true,true,true,true,true,enable.Checked,enable.Checked&&custom.Checked,enable.Checked&&custom.Checked,true,true];
            // 打开前按 100% 书写（Palette.Show 统一放大），打开后要自己乘上缩放。
            var factor=shown?dialog.DeviceDpi/96f*Palette.LayoutScale:1f;var hidden=0;
            for(var i=0;i<rowHeights.Length;i++){layout.RowStyles[i].Height=visible[i]?(float)Math.Round(rowHeights[i]*factor):0;if(!visible[i])hidden+=rowHeights[i];}
            sources.Visible=visible[6];certRow.Visible=visible[7];keyRow.Visible=visible[8];
            httpsPort.Visible=httpsPortLabel.Visible=enable.Checked;
            dialog.ClientSize=new Size(dialog.ClientSize.Width,(int)Math.Round((clientHeight-hidden)*factor));
        }
        dialog.Shown+=(_,_)=>shown=true;
        void RefreshCa()
        {
            using var ca=runtime.LocalCa();var trusted=ca!=null&&runtime.LocalCaTrusted();
            caStatus.Text=ca==null
                ?T("尚未创建。为项目启用自动签发的 HTTPS 时自动创建；点击“信任”也会立即创建。","Not created yet. It is created when you enable automatic HTTPS, or when you click Trust.","未作成です。自動 HTTPS を有効にするか「信頼」を押すと作成されます。")
                :T($"已创建，有效期至 {ca.NotAfter:yyyy-MM-dd}。{(trusted?"已信任：浏览器不会提示不安全。":"未信任：浏览器会提示不安全，点击“信任本地根证书”后由 Windows 确认。")}\nFirefox 使用自己的证书库，需要在其设置中导入根证书。",$"Created, valid until {ca.NotAfter:yyyy-MM-dd}. {(trusted?"Trusted: browsers will not warn.":"Not trusted: browsers will warn until you click Trust and confirm in Windows.")}\nFirefox uses its own store; import the root there.",$"作成済み（{ca.NotAfter:yyyy-MM-dd} まで）。{(trusted?"信頼済み：ブラウザーは警告しません。":"未信頼：「ルートを信頼」を押して Windows で確認してください。")}\nFirefox は独自のストアを使用します。");
            trust.Enabled=!working&&!trusted;untrust.Enabled=!working&&trusted;
        }
        void RefreshSite()
        {
            var site=Current();var info=runtime.SiteCertificateInfo(site);
            var customMode=custom.Checked;certFile.Enabled=keyFile.Enabled=certBrowse.Enabled=keyBrowse.Enabled=!working&&enable.Checked&&customMode;
            auto.Enabled=custom.Enabled=!working&&enable.Checked;renew.Enabled=!working&&enable.Checked&&!customMode&&site.Https;save.Enabled=!working;project.Enabled=!working;enable.Enabled=!working;httpsPort.Enabled=httpsPortLabel.Enabled=!working&&enable.Checked;
            renew.Visible=enable.Checked&&!customMode&&site.Https;Relayout();
            var shown=customMode==(site.CertificateSource=="custom")?info:null;
            certInfo.Text=(site.Https&&site.HttpsPort>0?T($"当前地址：{Runtime.WebUrl("https",site.Domain,site.HttpsPort)}",$"Address: {Runtime.WebUrl("https",site.Domain,site.HttpsPort)}",$"アドレス：{Runtime.WebUrl("https",site.Domain,site.HttpsPort)}"):T("当前未启用 HTTPS。","HTTPS is off.","HTTPS は無効です。"))+"\n"+
                (shown==null?(customMode?T("尚未导入证书。选择证书文件和私钥文件后保存。","No certificate imported yet. Choose both files and save.","証明書が未登録です。両方のファイルを選択して保存してください。"):T("证书会在保存时自动签发。","A certificate is issued when you save.","保存時に証明書を発行します。"))
                :T($"证书：{shown.Subject}，有效期至 {shown.NotAfter:yyyy-MM-dd}，{(shown.MatchesDomain?"包含项目域名":"不包含项目域名，浏览器会提示不安全")}。",$"Certificate: {shown.Subject}, valid until {shown.NotAfter:yyyy-MM-dd}, {(shown.MatchesDomain?"covers the domain":"does NOT cover the domain")}.",$"証明書：{shown.Subject}（{shown.NotAfter:yyyy-MM-dd} まで）、{(shown.MatchesDomain?"ドメイン一致":"ドメイン不一致")}。"));
        }
        // 端口只在“指定过”（PortPinned）时回填；自动分配的端口留空，避免看起来像用户填过
        void LoadSite(){var site=Current();enable.Checked=site.Https;(site.CertificateSource=="custom"?custom:auto).Checked=true;certFile.Text=keyFile.Text="";httpsPort.Text=site.Https&&site.PortPinned&&site.HttpsPort>0?site.HttpsPort.ToString():"";RefreshSite();}
        project.SelectedIndexChanged+=(_,_)=>LoadSite();enable.CheckedChanged+=(_,_)=>RefreshSite();auto.CheckedChanged+=(_,_)=>RefreshSite();custom.CheckedChanged+=(_,_)=>RefreshSite();
        void Pick(TextBox box,string filter){using var picker=new OpenFileDialog{Filter=filter,CheckFileExists=true};if(picker.ShowDialog(dialog)==DialogResult.OK)box.Text=picker.FileName;}
        certBrowse.Click+=(_,_)=>Pick(certFile,T("证书","Certificate","証明書")+" (*.crt;*.pem;*.cer)|*.crt;*.pem;*.cer|*.*|*.*");
        keyBrowse.Click+=(_,_)=>Pick(keyFile,T("私钥","Private key","秘密鍵")+" (*.key;*.pem)|*.key;*.pem|*.*|*.*");
        folder.Click+=(_,_)=>{var path=Path.Combine(settings.Root,"config","ssl");Directory.CreateDirectory(path);Open(path);};
        trust.Click+=(_,_)=>{
            try{runtime.TrustLocalCa();}
            catch(System.Security.Cryptography.CryptographicException){MessageBox.Show(dialog,T("未加入信任列表（已在 Windows 提示中取消）。","The root was not trusted (cancelled in Windows).","信頼に追加されませんでした（キャンセル）。"),dialog.Text);}
            RefreshCa();
        };
        untrust.Click+=(_,_)=>{try{runtime.UntrustLocalCa();}catch(System.Security.Cryptography.CryptographicException){}RefreshCa();};
        async Task Apply(bool renewNow)
        {
            var site=Current();var source=custom.Checked?"custom":"auto";
            if(enable.Checked&&source=="custom"&&(certFile.Text.Length==0)!=(keyFile.Text.Length==0)){MessageBox.Show(dialog,T("请同时选择证书文件和私钥文件。","Choose both the certificate and the private key.","証明書と秘密鍵の両方を選択してください。"),dialog.Text);return;}
            if(enable.Checked&&source=="custom"&&certFile.Text.Length==0&&!(site.CertificateSource=="custom"&&runtime.SiteCertificateInfo(site)!=null)){MessageBox.Show(dialog,T("请选择证书文件和私钥文件。","Choose the certificate and private key files.","証明書と秘密鍵を選択してください。"),dialog.Text);return;}
            working=true;busy=true;dialog.UseWaitCursor=true;RefreshSite();RefreshCa();
            try
            {
                string? warning;
                int? wantedPort=null;
                if(enable.Checked&&httpsPort.Text.Trim().Length>0)
                {
                    if(!int.TryParse(httpsPort.Text.Trim(),out var parsed)){MessageBox.Show(dialog,T("HTTPS 端口请填 1–65535 的数字。","Enter an HTTPS port (1–65535).","HTTPS ポートは 1～65535 の数字で入力してください。"),dialog.Text);return;}
                    var problem=settings.PortProblem(parsed,site);
                    if(problem!=null){MessageBox.Show(dialog,problem switch{"range"=>T("HTTPS 端口请填 1–65535 的数字。","Enter an HTTPS port (1–65535).","HTTPS ポートは 1～65535 の数字で入力してください。"),"panel"=>T("该端口是面板自己使用的。","That port belongs to the panel itself.","そのポートはパネル自身が使用中です。"),"site"=>T("该端口已被另一个项目使用。","Another project already uses that port.","そのポートは他のプロジェクトが使用中です。"),"mysql"=>T("该端口是面板 MySQL 实例的端口。","That port belongs to a panel MySQL instance.","そのポートはパネルの MySQL が使用中です。"),_=>T("该端口不可用。","That port is not available.","そのポートは使用できません。")},dialog.Text);return;}
                    var holder=PortDiagnostics.ListenerPid(parsed);
                    if(holder!=null&&holder!=runtime.ServicePid(runtime.WebKey)&&!(site.Https&&site.HttpsPort==parsed)){MessageBox.Show(dialog,T($"端口 {parsed} 已被占用：",$"Port {parsed} is in use by ",$"ポート {parsed} は使用中です：")+PortDiagnostics.Describe(holder.Value).Replace("\n"," "),dialog.Text);return;}
                    wantedPort=parsed;
                }
                using(var operation=Runtime.Lock(settings)){runtime.Adopt();warning=await runtime.ConfigureHttpsAsync(site,enable.Checked,source,certFile.Text.Length>0?certFile.Text:null,keyFile.Text.Length>0?keyFile.Text:null,renewNow,wantedPort);}
                certFile.Text=keyFile.Text="";
                MessageBox.Show(dialog,(warning!=null?warning+"\n\n":"")+(enable.Checked?T($"已应用。HTTPS 地址：{Runtime.WebUrl("https",site.Domain,site.HttpsPort)}",$"Applied. HTTPS address: {Runtime.WebUrl("https",site.Domain,site.HttpsPort)}",$"適用しました：{Runtime.WebUrl("https",site.Domain,site.HttpsPort)}"):T("已关闭该项目的 HTTPS。","HTTPS turned off for this project.","HTTPS を無効にしました。")),dialog.Text,MessageBoxButtons.OK,warning!=null?MessageBoxIcon.Warning:MessageBoxIcon.Information);
            }
            catch(Exception error){MessageBox.Show(dialog,T("未能应用，已保留原设置：\n","Not applied; previous settings kept:\n","適用できませんでした（元の設定を維持）：\n")+error.Message,dialog.Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
            finally{working=false;busy=false;dialog.UseWaitCursor=false;LoadSite();RefreshCa();RefreshState();}
        }
        save.Click+=async(_,_)=>await Apply(false);renew.Click+=async(_,_)=>await Apply(true);
        dialog.FormClosing+=(_,e)=>{if(working)e.Cancel=true;};
        LoadSite();RefreshCa();
        Palette.Show(dialog,this);
    }
}
