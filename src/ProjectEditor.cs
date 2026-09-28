namespace YikaiLocal;

public sealed partial class MainForm
{
    // initialKind：从“接入已有目录”入口打开时预选该类型（null 时按新建处理）
    void ProjectDialog(Site? site,string? initialKind=null)
    {
        using var dialog=new Form{Name="projectEditor",Text=site==null?(initialKind=="import"?T("接入已有目录","Connect a folder","既存フォルダーを接続"):T("新建项目","New project","新規プロジェクト")):T("项目设置","Project settings","設定"),ClientSize=new Size(760,692),Font=Font,AutoScaleMode=AutoScaleMode.Dpi,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Icon=Icon};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=2,RowCount=12,Margin=Padding.Empty};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,168));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));dialog.Controls.Add(layout);
        for(var i=0;i<10;i++)layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        var title=new TextBox{Name="projectName",Text=site?.Title??""};var domain=new TextBox{Name="projectDomain",Text=site?.Domain??"",PlaceholderText="demo"+Settings.DefaultSuffix};
        InputFrame Input(TextBox editor)=>new(editor){Dock=DockStyle.Top,Height=36,Margin=new Padding(0,0,0,16)};
        Choice Select(string name,string[] values,int index){var c=new Choice{Name=name,Dock=DockStyle.Top,Height=36,Margin=new Padding(0,0,0,16)};c.Items.AddRange(values);c.SelectedIndex=index;return c;}
        string[] kinds=["php","yikaicms","wordpress","import"];var kind=Select("projectKind",[T("空白 PHP","Blank PHP","空の PHP"),T("YikaiCMS（最新版）","YikaiCMS (latest)","YikaiCMS（最新版）"),T("WordPress（最新版）","WordPress (latest)","WordPress（最新版）"),T("接入已有目录","Existing folder","既存フォルダー")],Math.Max(0,Array.IndexOf(kinds,site?.Template??initialKind??"yikaicms")));string Kind()=>kinds[Math.Max(0,kind.SelectedIndex)];kind.Enabled=site==null;
        // 只列实际装了的版本：最小包可能只有 8.5
        var installedPhp=runtime.InstalledPhpVersions;if(installedPhp.Length==0)installedPhp=["8.2"];
        var version=Select("projectPhp",installedPhp,0);
        // 新建项目优先使用 PHP 8.5；未安装时回退到实际存在的版本。已有项目保留原版本。
        string DefaultPhp(){var wanted=Kind()=="yikaicms"&&settings.PhpDefault=="8.0"?"8.2":settings.PhpDefault;return installedPhp.Contains(wanted)?wanted:installedPhp.Contains("8.2")?"8.2":installedPhp[0];}
        version.SelectedItem=site?.Php??DefaultPhp();bool versionTouched=false,applyingDefault=false;
        version.SelectedIndexChanged+=(_,_)=>{if(!applyingDefault)versionTouched=true;};
        kind.SelectedIndexChanged+=(_,_)=>{if(site!=null||versionTouched)return;applyingDefault=true;version.SelectedItem=DefaultPhp();applyingDefault=false;};
        var engine=Select("projectDatabase",["MySQL 8.0","MySQL 5.7","SQLite"],site?.Database=="mysql57"?1:site?.Database=="sqlite"?2:0);engine.Enabled=site==null;
        // 项目数据库：新建时可自定义数据库名、专属用户和密码（默认按项目名生成同名数据库与用户，密码随机）；已有项目只显示，不在这里修改。
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
        var defaultHint=site==null?T("无后缀时自动补 .localhost，浏览器直接打开，无需 hosts；用 .yikai 等域名时勾选“同步 hosts”即可建完自动写入。","Names without a suffix use .localhost (no hosts needed). For .yikai and other domains, keep “Sync hosts” checked.","末尾なしは .localhost（hosts 不要）。.yikai などは「hosts を同期」で作成後に自動登録。"):T("更改域名后会自动同步 hosts（.localhost 不需要）。项目目录保持不变。","Changing the domain syncs hosts automatically (not needed for .localhost). The project folder stays in place.","ドメイン変更後は hosts を自動同期します（.localhost は不要）。フォルダーは変更されません。");
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
        // 新建项目：勾选“同步 hosts”时建完自动写入 hosts（.localhost 不需要，勾选框随之置灰）。
        var syncHosts=new CheckBox{Name="projectSyncHosts",Text=T("同步 hosts","Sync hosts","hosts を同期"),Checked=true,AutoSize=true,Margin=new Padding(0,10,0,0),Visible=site==null};
        if(site==null)extras.Controls.Add(syncHosts);
        string Host(){var value=domain.Text.Trim().ToLowerInvariant();return value.Contains('.')?value:value+Settings.DefaultSuffix;}
        string DefaultDatabaseName()
        {
            // 数据库名与用户相同，均由项目名生成；点号等分隔符转下划线，控制在 MySQL 用户名的 32 字符内。
            var stem=System.Text.RegularExpressions.Regex.Replace(title.Text.Trim().ToLowerInvariant(),"[^a-z0-9]+","_").Trim('_');
            if(stem.Length==0)stem=System.Text.RegularExpressions.Regex.Replace(Host(),"[^a-z0-9]+","_").Trim('_');
            if(stem.Length>32)stem=stem[..23]+"_"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stem)))[..8].ToLowerInvariant();
            var database=engine.SelectedIndex==1?"mysql57":"mysql80";
            for(var n=1;;n++)
            {
                var suffix=n==1?"":"_"+n;
                var candidate=stem[..Math.Min(stem.Length,32-suffix.Length)]+suffix;
                if(settings.DatabaseProblem(database,candidate,candidate,"safe")==null)return candidate;
            }
        }
        domain.TextChanged+=(_,_)=>syncHosts.Enabled=Settings.NeedsHosts(Host());syncHosts.Enabled=Settings.NeedsHosts(Host());
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
                // 用户没手动改过时，数据库名和用户名从项目名生成。
                applyingDbDefault=true;
                if(!dbNameTouched)dbName.Text=valid?DefaultDatabaseName():"";
                if(!dbUserTouched)dbUser.Text=dbName.Text;
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
        kind.SelectedIndexChanged+=(_,_)=>UpdateFields();version.SelectedIndexChanged+=(_,_)=>UpdateFields();title.TextChanged+=(_,_)=>UpdateFields();domain.TextChanged+=(_,_)=>UpdateFields();engine.SelectedIndexChanged+=(_,_)=>UpdateFields();port.TextChanged+=(_,_)=>UpdateFields();
        foreach(var box in new[]{dbName,dbUser,dbPassword})box.TextChanged+=(_,_)=>{if(!applyingDbDefault)UpdateFields();};UpdateFields();
        save.Click+=(_,_)=>{UpdateFields();if(save.Enabled)dialog.DialogResult=DialogResult.OK;};
        if(site==null&&initialKind==null)
        {
            // 日常新建只问名称与程序；高级字段沿用原编辑器，展开时才显示。
            var quick=new TableLayoutPanel{Name="quickCreateProject",Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=6,Margin=Padding.Empty};
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(var height in new[]{28,48,28,48,68,52})quick.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            var quickName=new TextBox{Name="quickProjectName",MaxLength=100,PlaceholderText=T("英文、数字、. 或 -，如 mysite1","Letters, digits, . or -, e.g. mysite1","英数字・.・-、例：mysite1")};
            var quickKind=Select("quickProjectKind",[T("YikaiCMS（最新版）","YikaiCMS (latest)","YikaiCMS（最新版）"),T("WordPress（最新版）","WordPress (latest)","WordPress（最新版）")],0);
            var quickNameFrame=Input(quickName);quickNameFrame.Margin=Padding.Empty;quickKind.Margin=Padding.Empty;
            var quickHint=L("",9);quickHint.Name="quickProjectHint";quickHint.Dock=DockStyle.Fill;quickHint.AutoSize=false;quickHint.ForeColor=Muted;quickHint.Margin=new Padding(0,4,0,0);
            var quickFooter=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
            quickFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));quickFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            var more=new ThemeButton{Name="moreProjectSettings",Text=T("更多设置…","More settings…","詳細設定…"),Dock=DockStyle.Fill,Margin=new Padding(0,2,8,2)};
            var confirm=new ThemeButton{Name="confirmQuickProject",Text=T("确认创建","Create project","作成する"),Dock=DockStyle.Fill,Margin=new Padding(8,2,0,2),BackColor=Palette.Accent};
            quickFooter.Controls.Add(more,0,0);quickFooter.Controls.Add(confirm,1,0);
            quick.Controls.Add(L(T("项目名称","Project name","プロジェクト名"),10),0,0);
            quick.Controls.Add(quickNameFrame,0,1);
            quick.Controls.Add(L(T("站点程序","Site software","サイトの種類"),10),0,2);
            quick.Controls.Add(quickKind,0,3);
            quick.Controls.Add(quickHint,0,4);
            quick.Controls.Add(quickFooter,0,5);
            // 项目名只收英文字母、数字、点和中划线：带点的直接当域名（如 shop.yikai），不带点的补 .localhost。
            string SuggestedDomain(string name)
            {
                if(name.Contains('.'))return name.Trim().ToLowerInvariant();
                var slug=System.Text.RegularExpressions.Regex.Replace(name.Trim().ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-');
                if(slug.Length==0)slug="site-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name)))[..8].ToLowerInvariant();
                if(slug.Length>50)slug=slug[..50].TrimEnd('-');
                var candidate=slug+Settings.DefaultSuffix;
                for(var n=2;settings.Sites.Any(s=>s.Domain.Equals(candidate,StringComparison.OrdinalIgnoreCase))||Directory.Exists(Path.Combine(settings.Root,"wwwroot",candidate));n++)
                    candidate=slug+"-"+n+Settings.DefaultSuffix;
                return candidate;
            }
            void RefreshQuick()
            {
                var name=quickName.Text.Trim();
                var problem=QuickNameProblem(name);
                confirm.Enabled=name.Length>0&&problem==null;
                quickHint.ForeColor=problem==null?Muted:Palette.Warning;
                quickHint.Text=name.Length==0
                    ? T($"项目名只能用英文字母、数字、点（.）和中划线（-）。不带点时自动生成 .localhost 域名；带点时直接作为域名（如 shop.yikai）。默认 PHP {DefaultPhp()}、MySQL 8.0。",$"Use letters, digits, dots (.) and hyphens (-). Without a dot a .localhost domain is generated; with a dot the name is used as the domain (e.g. shop.yikai). Defaults: PHP {DefaultPhp()} and MySQL 8.0.",$"英字・数字・ドット（.）・ハイフン（-）のみ使えます。ドットなしは .localhost ドメインを生成し、ドットありはそのままドメインになります（例：shop.yikai）。既定は PHP {DefaultPhp()} / MySQL 8.0 です。")
                    : problem ?? SuggestedDomain(name)+"  ·  PHP "+DefaultPhp()+"  ·  MySQL 8.0";
            }
            string? QuickNameProblem(string name)
            {
                if(name.Length==0)return null;
                if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^[A-Za-z0-9.-]+$"))
                    return T("项目名只能用英文字母、数字、点（.）和中划线（-），不能有中文、空格或其他符号。","Only letters, digits, dots (.) and hyphens (-) are allowed — no spaces or other characters.","英字・数字・ドット（.）・ハイフン（-）以外は使えません（日本語・空白・記号は不可）。");
                var candidate=SuggestedDomain(name);
                if(!char.IsAsciiLetterOrDigit(name[0])||!char.IsAsciiLetterOrDigit(name[^1])||name.Contains("..")||!Settings.ValidDomain(candidate))
                    return T("开头和结尾要用字母或数字，点不能连用；带点时最后一段要以字母开头、至少两个字符，如 shop.yikai。","Start and end with a letter or digit, no double dots; with a dot the last part must start with a letter and have at least two characters, e.g. shop.yikai.","先頭と末尾は英数字、ドットの連続は不可。ドットありの場合、最後の部分は英字で始まる 2 文字以上にします（例：shop.yikai）。");
                if(name.Contains('.')&&(settings.Sites.Any(s=>s.Domain.Equals(candidate,StringComparison.OrdinalIgnoreCase))||Directory.Exists(Path.Combine(settings.Root,"wwwroot",candidate))))
                    return T($"{candidate} 已被其他项目或同名文件夹使用，请换一个。",$"{candidate} is already used by another project or folder.",$"{candidate} は他のプロジェクトかフォルダーで使用中です。");
                return null;
            }
            void ApplyQuick()
            {
                var name=quickName.Text.Trim();
                title.Text=name;
                domain.Text=SuggestedDomain(name);
                kind.SelectedIndex=quickKind.SelectedIndex==0?1:2;
                version.SelectedItem=DefaultPhp();
                engine.SelectedIndex=0;
                UpdateFields();
            }
            quickName.TextChanged+=(_,_)=>RefreshQuick();
            quickKind.SelectedIndexChanged+=(_,_)=>RefreshQuick();
            more.Click+=(_,_)=>{
                if(quickName.Text.Trim().Length>0)ApplyQuick();
                quick.Visible=false;layout.Visible=true;layout.AutoScroll=true;
                // Palette.Show 在弹出前按 DPI / 字号缩放过整个窗口；这里也须使用同一比例。
                var scale=DeviceDpi/96f*Palette.LayoutScale;
                var work=Screen.FromControl(this).WorkingArea;
                var border=new Size(dialog.Width-dialog.ClientSize.Width,dialog.Height-dialog.ClientSize.Height);
                var margin=(int)Math.Ceiling(32*scale);
                dialog.ClientSize=new Size(
                    Math.Min((int)Math.Ceiling(900*scale),Math.Max(1,work.Width-border.Width-2*margin)),
                    Math.Min((int)Math.Ceiling(790*scale),Math.Max(1,work.Height-border.Height-2*margin)));
                var owner=dialog.Owner?.Bounds??Bounds;
                dialog.Location=new Point(
                    Math.Clamp(owner.Left+(owner.Width-dialog.Width)/2,work.Left,Math.Max(work.Left,work.Right-dialog.Width)),
                    Math.Clamp(owner.Top+(owner.Height-dialog.Height)/2,work.Top,Math.Max(work.Top,work.Bottom-dialog.Height)));
                dialog.AcceptButton=save;title.Focus();
            };
            confirm.Click+=(_,_)=>{
                if(quickName.Text.Trim().Length==0||QuickNameProblem(quickName.Text.Trim())!=null)return;
                ApplyQuick();
                if(save.Enabled)dialog.DialogResult=DialogResult.OK;
                else quickHint.Text=hint.Text;
            };
            layout.Visible=false;dialog.Controls.Add(quick);dialog.ClientSize=new Size(540,320);dialog.AcceptButton=confirm;dialog.Shown+=(_,_)=>quickName.Focus();RefreshQuick();
        }
        if(Palette.Show(dialog,this)!=DialogResult.OK)return;
        var chosenDomain=Host();var chosenPhp=version.Text;var chosenEngine=engine.SelectedIndex;var chosenKind=Kind();var chosenPath=path.Text;var chosenTitle=title.Text;
        var chosenDbName=dbName.Text.Trim();var chosenDbUser=dbUser.Text.Trim();var chosenDbPassword=dbPassword.Text;
        // 端口：空=保持自动分配（新项目继续自增），填了=指定端口并锁定（运行期不再被自动改掉）。
        var pinPort=port.Text.Trim().Length>0;var chosenPort=pinPort?int.Parse(port.Text.Trim()):0;
        // 建完 / 改完域名后按需写 hosts（.yikai 等需要，.localhost 不需要）；UAC 里点“否”不算错误，稍后可点顶部“同步域名”。
        var wantHosts=Settings.NeedsHosts(chosenDomain)&&(site==null?syncHosts.Checked:!chosenDomain.Equals(site.Domain,StringComparison.OrdinalIgnoreCase));
        bool hostsSkipped=false;async Task AfterSave(){if(wantHosts)hostsSkipped=!await SyncHostsCore(false);}
        _=Finish();async Task Finish(){await Work(async()=>{if(site==null){var added=settings.AddSite(chosenDomain,chosenPhp,chosenEngine==0?"mysql80":chosenEngine==1?"mysql57":"sqlite",chosenKind,chosenPath,chosenTitle,chosenEngine==2?null:chosenDbName,chosenEngine==2?null:chosenDbUser,chosenEngine==2?null:chosenDbPassword,await DownloadProjectSource(chosenKind,new Site{Title=chosenTitle.Trim(),Domain=chosenDomain,Php=chosenPhp,Directory=chosenPath}));if(pinPort)added.HttpPort=chosenPort;added.PortPinned=pinPort;selectedId=added.Id;await runtime.StartSiteAsync(added);if(settings.AutoInstallCms&&added.Template=="yikaicms")await runtime.InstallCmsAsync(added);}else{var wasRunning=runtime.IsRunning(site);var portChanged=pinPort!=site.PortPinned||(pinPort&&site.HttpPort!=chosenPort);await runtime.StopSiteAsync(site);site.Title=chosenTitle.Trim();site.Domain=chosenDomain;site.Php=chosenPhp;if(pinPort)site.HttpPort=chosenPort;site.PortPinned=pinPort;settings.Save();if(wasRunning||portChanged)await runtime.StartSiteAsync(site);else if(runtime.WebServerRunning)await runtime.ReloadWebServer();selectedId=site.Id;}await AfterSave();var targetId=selectedId;search.Text="";PopulateProjects();projects.SelectedItem=settings.Sites.FirstOrDefault(s=>s.Id==targetId);});if(hostsSkipped)progress.Text=T("未同步 hosts，可稍后点顶部“同步域名”。","Hosts not synced. Use Sync domains later.","hosts 未同期。後で「ドメイン同期」を使ってください。");}
    }
}
