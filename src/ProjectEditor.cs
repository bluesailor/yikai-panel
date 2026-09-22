namespace YikaiLocal;

public sealed partial class MainForm
{
    // initialKind：从“接入已有目录”入口打开时预选该类型（null 时按新建处理）
    void ProjectDialog(Site? site,string? initialKind=null)
    {
        using var dialog=new Form{Name="projectEditor",Text=site==null?(initialKind=="import"?T("接入已有目录","Connect a folder","既存フォルダーを接続"):T("添加项目","Add project","追加")):T("项目设置","Project settings","設定"),ClientSize=new Size(760,692),Font=Font,AutoScaleMode=AutoScaleMode.Dpi,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Icon=Icon};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=2,RowCount=12,Margin=Padding.Empty};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,168));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));dialog.Controls.Add(layout);
        for(var i=0;i<10;i++)layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        var title=new TextBox{Name="projectName",Text=site?.Title??""};var domain=new TextBox{Name="projectDomain",Text=site?.Domain??"",PlaceholderText="demo.yikai"};
        InputFrame Input(TextBox editor)=>new(editor){Dock=DockStyle.Top,Height=36,Margin=new Padding(0,0,0,16)};
        Choice Select(string name,string[] values,int index){var c=new Choice{Name=name,Dock=DockStyle.Top,Height=36,Margin=new Padding(0,0,0,16)};c.Items.AddRange(values);c.SelectedIndex=index;return c;}
        string[] kinds=["php","yikaicms","wordpress","import"];var kind=Select("projectKind",[T("空白 PHP","Blank PHP","空の PHP"),T("YikaiCMS（最新版）","YikaiCMS (latest)","YikaiCMS（最新版）"),T("WordPress（最新版）","WordPress (latest)","WordPress（最新版）"),T("接入已有目录","Existing folder","既存フォルダー")],Math.Max(0,Array.IndexOf(kinds,site?.Template??initialKind??"php")));string Kind()=>kinds[Math.Max(0,kind.SelectedIndex)];kind.Enabled=site==null;
        // 只列实际装了的版本：最小包可能只有 8.5
        var installedPhp=runtime.InstalledPhpVersions;if(installedPhp.Length==0)installedPhp=["8.2"];
        var version=Select("projectPhp",installedPhp,0);
        // 新项目使用“默认 PHP 版本”；YikaiCMS 需要 8.2+，默认 8.0 时回落到 8.2。用户手动改过版本后不再覆盖。
        string DefaultPhp(){var wanted=Kind()=="yikaicms"&&settings.PhpDefault=="8.0"?"8.2":settings.PhpDefault;return installedPhp.Contains(wanted)?wanted:installedPhp[0];}
        version.SelectedItem=site?.Php??DefaultPhp();bool versionTouched=false,applyingDefault=false;
        version.SelectedIndexChanged+=(_,_)=>{if(!applyingDefault)versionTouched=true;};
        kind.SelectedIndexChanged+=(_,_)=>{if(site!=null||versionTouched)return;applyingDefault=true;version.SelectedItem=DefaultPhp();applyingDefault=false;};
        var engine=Select("projectDatabase",["MySQL 8.0","MySQL 5.7","SQLite"],site?.Database=="mysql57"?1:site?.Database=="sqlite"?2:0);engine.Enabled=site==null;
        // 项目数据库：新建时可自定义数据库名、专属用户和密码（默认按域名生成，密码随机）；已有项目只显示，不在这里修改。
        var dbName=new TextBox{Name="projectDatabaseName",Text=site?.DatabaseName??"",ReadOnly=site!=null,MaxLength=64};
        var dbUser=new TextBox{Name="projectDatabaseUser",Text=site==null?"":site.DatabaseUser??settings.MysqlUser+T("（共用 root）"," (shared root)","（共有 root）"),ReadOnly=site!=null,MaxLength=32};
        var dbPassword=new TextBox{Name="projectDatabasePassword",Text=site==null?Settings.GeneratePassword():site.DatabasePassword??"—",ReadOnly=site!=null,MaxLength=64};
        var dbPasswordInput=Input(dbPassword);dbPasswordInput.Dock=DockStyle.Fill;dbPasswordInput.Margin=Padding.Empty;
        var regenerate=new ThemeButton{Name="regenerateDatabasePassword",Dock=DockStyle.Fill,Margin=new Padding(8,0,0,0),Visible=site==null};AttachButtonIcon(regenerate,"sync");
        var passwordRow=new TableLayoutPanel{Dock=DockStyle.Top,Height=36,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,16)};passwordRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));passwordRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));passwordRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,site==null?44:0));passwordRow.Controls.Add(dbPasswordInput,0,0);passwordRow.Controls.Add(regenerate,1,0);
        bool dbNameTouched=false,dbUserTouched=false,applyingDbDefault=false;
        dbName.TextChanged+=(_,_)=>{if(!applyingDbDefault)dbNameTouched=true;};dbUser.TextChanged+=(_,_)=>{if(!applyingDbDefault)dbUserTouched=true;};
        regenerate.Click+=(_,_)=>dbPassword.Text=Settings.GeneratePassword();
        var path=new TextBox{Name="projectDirectory",Text=site?.Directory??"",ReadOnly=true,PlaceholderText=T("填写域名后自动生成","Generated after entering a domain","ドメイン入力後に自動設定")};var pathInput=Input(path);pathInput.Dock=DockStyle.Fill;pathInput.Margin=Padding.Empty;
        var browse=new ThemeButton{Name="browseDirectory",Text="…",Dock=DockStyle.Fill,Margin=new Padding(8,0,0,0)};string importedFolder=site?.Directory??"";string newParent=Path.Combine(settings.Root,"wwwroot");
        // 目录按钮：新建时选择存放位置（在其中创建 <域名> 文件夹），接入时选择已有目录；编辑已有项目不移动目录，隐藏按钮。
        if(site!=null)browse.Visible=false;var browseTip=new ToolTip();dialog.Disposed+=(_,_)=>browseTip.Dispose();
        var pathRow=new TableLayoutPanel{Dock=DockStyle.Top,Height=36,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,16)};pathRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,site==null?44:0));pathRow.Controls.Add(pathInput,0,0);pathRow.Controls.Add(browse,1,0);
        // 端口：留空自动分配；填了就是指定端口（常用端口 80 这样用），运行期不再被自动改掉。
        var port=new TextBox{Name="projectPort",Text=site is {PortPinned:true}?site.HttpPort.ToString():"",PlaceholderText=T("留空自动分配；常用端口 80","Empty = automatic; 80 for a standard port","空欄で自動、常用は 80"),MaxLength=5};
        string[] captions=[T("项目名称","Project name","名前"),T("本地域名","Local domain","ドメイン"),T("项目类型","Project type","種類"),"PHP",T("数据库","Database","データベース"),T("数据库名","Database name","データベース名"),T("数据库用户","Database user","DB ユーザー"),T("数据库密码","DB password","DB パスワード"),T("项目目录","Project folder","フォルダー"),T("端口","Port","ポート")];Control[] fields=[Input(title),Input(domain),kind,version,engine,Input(dbName),Input(dbUser),passwordRow,pathRow,Input(port)];
        for(var i=0;i<fields.Length;i++){var label=L(captions[i],10.5f);label.Dock=DockStyle.Top;label.Height=36;label.Margin=new Padding(0,0,12,16);layout.Controls.Add(label,0,i);layout.Controls.Add(fields[i],1,i);}
        var defaultHint=site==null?T("无后缀时自动补 .yikai。创建后可在顶部同步域名。","Names without a suffix use .yikai. Sync domains from the toolbar.","末尾は .yikai。作成後に上部でドメインを同期できます。"):T("更改域名后请在顶部同步域名。项目目录保持不变。","Sync domains after changing the name. The project folder stays in place.","変更後に上部でドメインを同期してください。フォルダーは変更されません。");
        var hint=L(defaultHint,9);hint.ForeColor=Muted;hint.Margin=new Padding(0,0,0,12);layout.Controls.Add(hint,0,10);layout.SetColumnSpan(hint,2);
        var footerRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,12,0,0)};footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));footerRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.Controls.Add(footerRow,0,11);layout.SetColumnSpan(footerRow,2);
        var extras=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Margin=Padding.Empty};footerRow.Controls.Add(extras,0,0);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty};footerRow.Controls.Add(footer,1,0);
        var save=new ThemeButton{Name="saveProject",Text=site==null?T("创建项目","Create project","作成"):T("保存更改","Save changes","変更を保存"),Size=new Size(170,40),Margin=Padding.Empty,BackColor=Palette.Accent};
        var cancel=new ThemeButton{Name="cancelProject",Text=T("取消","Cancel","キャンセル"),DialogResult=DialogResult.Cancel,Size=new Size(120,40),Margin=new Padding(0,0,10,0)};footer.Controls.Add(save);footer.Controls.Add(cancel);dialog.AcceptButton=save;dialog.CancelButton=cancel;
        // 已有项目：SSL 证书和 PHP 扩展都可以只改这一个项目。
        if(site!=null)
        {
            var certificates=new ThemeButton{Name="projectSsl",Text=T("SSL 证书…","SSL certificate…","SSL 証明書…"),Size=new Size(150,40),Margin=Padding.Empty};
            certificates.Click+=(_,_)=>ShowSslDialog(site);
            var extensions=new ThemeButton{Name="projectExtensions",Text=T("PHP 扩展…","PHP extensions…","PHP 拡張…"),Size=new Size(150,40),Margin=new Padding(0,0,10,0)};
            extensions.Click+=(_,_)=>ShowPhpExtensions(site.Php,site);
            extras.Controls.Add(extensions);extras.Controls.Add(certificates);
        }
        string Host(){var value=domain.Text.Trim().ToLowerInvariant();return value.Contains('.')?value:value+".yikai";}
        // 端口校验：空=自动；非空则查范围、与面板自身/其它项目冲突，以及是否被别的程序占用。
        // 占用判定用 PortDiagnostics：面板自己的 Web 服务器占用不算（编辑运行中的项目时就是它）。
        string? PortIssue()
        {
            var text=port.Text.Trim();
            if(text.Length==0)return null;
            if(!int.TryParse(text,out var value))return T("端口请填 1–65535 的数字。","Enter a port number (1–65535).","ポートは 1～65535 の数字で入力してください。");
            var problem=settings.PortProblem(value,site);
            if(problem!=null)return problem switch{
                "range"=>T("端口请填 1–65535 的数字。","Enter a port number (1–65535).","ポートは 1～65535 の数字で入力してください。"),
                "panel"=>T("该端口是面板自己使用的（数据库页面或其 PHP）。","That port belongs to the panel itself.","そのポートはパネル自身が使用しています。"),
                "site"=>T("该端口已被另一个项目使用。","Another project already uses that port.","そのポートは他のプロジェクトが使用中です。"),
                "mysql"=>T("该端口是面板 MySQL 实例的端口。","That port belongs to a panel MySQL instance.","そのポートはパネルの MySQL が使用しています。"),
                _=>defaultHint};
            var holder=PortDiagnostics.ListenerPid(value);
            if(holder!=null&&holder!=runtime.ServicePid(runtime.WebKey))
                return T($"端口 {value} 已被占用：{PortDiagnostics.Describe(holder.Value).Replace("\n"," ")}。请先停掉该程序，或改用其它端口。",
                    $"Port {value} is in use by {PortDiagnostics.Describe(holder.Value).Replace("\n"," ")}. Stop that program or pick another port.",
                    $"ポート {value} は {PortDiagnostics.Describe(holder.Value).Replace("\n"," ")} が使用中です。停止するか別のポートにしてください。");
            return null;
        }
        void UpdateFields()
        {
            var host=Host();bool valid=Settings.ValidDomain(host)&&!settings.Sites.Any(s=>s!=site&&s.Domain.Equals(host,StringComparison.OrdinalIgnoreCase));
            browse.Enabled=site==null;
            browseTip.SetToolTip(browse,Kind()=="import"?T("选择已有项目目录","Choose an existing project folder","既存フォルダーを選択"):T("选择存放位置，将在其中创建以域名命名的文件夹","Choose where to create the project folder","プロジェクトフォルダーの作成場所を選択"));
            if(site==null)path.Text=Kind()=="import"?importedFolder:valid?Path.Combine(newParent,host):"";
            var targetExists=site==null&&Kind()!="import"&&valid&&Directory.Exists(path.Text);
            string? dbProblem=null;
            if(site==null)
            {
                var mysql=engine.SelectedIndex!=2;
                foreach(var box in new[]{dbName,dbUser,dbPassword})box.Enabled=mysql;regenerate.Enabled=mysql;
                // 用户没手动改过时，数据库名和用户名跟随域名生成。
                applyingDbDefault=true;
                if(!dbNameTouched)dbName.Text=valid?Settings.DatabaseNameFor(System.Text.RegularExpressions.Regex.Replace(host,"[^a-z0-9]","_")):"";
                if(!dbUserTouched)dbUser.Text=dbName.Text.Length>0?Settings.DatabaseUserFor(dbName.Text):"";
                applyingDbDefault=false;
                if(mysql&&valid)dbProblem=settings.DatabaseProblem(engine.SelectedIndex==1?"mysql57":"mysql80",dbName.Text.Trim(),dbUser.Text.Trim(),dbPassword.Text);
            }
            var portIssue=PortIssue();
            save.Enabled=valid&&!targetExists&&dbProblem==null&&portIssue==null&&!(Kind()=="yikaicms"&&version.Text=="8.0")&&!(Kind()=="wordpress"&&engine.SelectedIndex==2)&&(site!=null||Kind()!="import"||Directory.Exists(importedFolder));
            hint.Text=dbProblem switch{
                "name"=>T("数据库名只能用字母、数字和下划线（1–64 位），且不能是系统库。","Database name: 1–64 letters, digits or underscores; not a system database.","DB 名は英数字とアンダースコア 1～64 文字（システム DB は不可）。"),
                "name-used"=>T("已有项目使用这个数据库名，请换一个。","Another project already uses this database name.","この DB 名は他のプロジェクトで使用中です。"),
                "user"=>T("数据库用户名只能用字母、数字和下划线（1–32 位），不能用 root。","Database user: 1–32 letters, digits or underscores; not root.","DB ユーザーは英数字とアンダースコア 1～32 文字（root 不可）。"),
                "user-used"=>T("已有项目使用这个数据库用户，请换一个。","Another project already uses this database user.","この DB ユーザーは他のプロジェクトで使用中です。"),
                "password"=>T("请填写数据库密码（1–64 位，不含换行）。","Enter a database password (1–64 characters, no line breaks).","DB パスワードを入力してください（1～64 文字）。"),
                _=>null}??(portIssue??(Kind()=="wordpress"&&engine.SelectedIndex==2?T("WordPress 需要 MySQL，请选择 MySQL 8.0 或 5.7。","WordPress requires MySQL. Choose MySQL 8.0 or 5.7.","WordPress には MySQL が必要です。MySQL 8.0 または 5.7 を選択してください。"):Kind()=="yikaicms"&&version.Text=="8.0"?T("YikaiCMS 需要 PHP 8.2 或更高版本。","YikaiCMS requires PHP 8.2 or later.","YikaiCMS には PHP 8.2 以降が必要です。"):domain.Text.Trim().Length>0&&!valid?T("域名无效或已存在，请换一个域名。","This domain is invalid or already in use.","無効なドメイン、または使用中のドメインです。"):targetExists?T("该位置已有同名文件夹，请换个域名或点击 … 选择其他存放位置。","A folder with this name already exists. Change the domain or choose another location with ….","同名フォルダーがあります。ドメインを変えるか … で別の場所を選択してください。"):Kind()=="import"&&site==null&&!Directory.Exists(importedFolder)?T("点击目录右侧按钮，选择已有项目目录。","Choose an existing project folder with the browse button.","参照ボタンから既存のプロジェクトフォルダーを選択してください。"):defaultHint));
        }
        browse.Click+=(_,_)=>{
            var importing=Kind()=="import";
            using var picker=new FolderBrowserDialog{Description=importing?T("选择已有项目目录","Choose an existing project folder","既存フォルダーを選択"):T("选择存放位置，将在其中创建以域名命名的项目文件夹","Choose where to create the project folder (named after the domain)","プロジェクトフォルダーの作成場所を選択"),UseDescriptionForTitle=true,InitialDirectory=importing?(Directory.Exists(importedFolder)?importedFolder:settings.Root):newParent};
            if(picker.ShowDialog(dialog)!=DialogResult.OK)return;
            if(importing)importedFolder=picker.SelectedPath;else newParent=picker.SelectedPath;
            UpdateFields();
        };
        kind.SelectedIndexChanged+=(_,_)=>UpdateFields();version.SelectedIndexChanged+=(_,_)=>UpdateFields();domain.TextChanged+=(_,_)=>UpdateFields();engine.SelectedIndexChanged+=(_,_)=>UpdateFields();port.TextChanged+=(_,_)=>UpdateFields();
        foreach(var box in new[]{dbName,dbUser,dbPassword})box.TextChanged+=(_,_)=>{if(!applyingDbDefault)UpdateFields();};UpdateFields();
        save.Click+=(_,_)=>{UpdateFields();if(save.Enabled)dialog.DialogResult=DialogResult.OK;};
        if(Palette.Show(dialog,this)!=DialogResult.OK)return;
        var chosenDomain=Host();var chosenPhp=version.Text;var chosenEngine=engine.SelectedIndex;var chosenKind=Kind();var chosenPath=path.Text;var chosenTitle=title.Text;
        var chosenDbName=dbName.Text.Trim();var chosenDbUser=dbUser.Text.Trim();var chosenDbPassword=dbPassword.Text;
        // 端口：空=保持自动分配（新项目继续自增），填了=指定端口并锁定（运行期不再被自动改掉）。
        var pinPort=port.Text.Trim().Length>0;var chosenPort=pinPort?int.Parse(port.Text.Trim()):0;
        _=Work(async()=>{if(site==null){var added=settings.AddSite(chosenDomain,chosenPhp,chosenEngine==0?"mysql80":chosenEngine==1?"mysql57":"sqlite",chosenKind,chosenPath,chosenTitle,chosenEngine==2?null:chosenDbName,chosenEngine==2?null:chosenDbUser,chosenEngine==2?null:chosenDbPassword,await DownloadProjectSource(chosenKind));if(pinPort)added.HttpPort=chosenPort;added.PortPinned=pinPort;selectedId=added.Id;await runtime.StartSiteAsync(added);if(settings.AutoInstallCms&&added.Template=="yikaicms")await runtime.InstallCmsAsync(added);}else{var wasRunning=runtime.IsRunning(site);var portChanged=pinPort!=site.PortPinned||(pinPort&&site.HttpPort!=chosenPort);await runtime.StopSiteAsync(site);site.Title=chosenTitle.Trim();site.Domain=chosenDomain;site.Php=chosenPhp;if(pinPort)site.HttpPort=chosenPort;site.PortPinned=pinPort;settings.Save();if(wasRunning||portChanged)await runtime.StartSiteAsync(site);else if(runtime.WebServerRunning)await runtime.ReloadWebServer();selectedId=site.Id;}var targetId=selectedId;search.Text="";PopulateProjects();projects.SelectedItem=settings.Sites.FirstOrDefault(s=>s.Id==targetId);});
    }
}
