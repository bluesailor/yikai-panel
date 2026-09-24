using System.ComponentModel;

namespace YikaiLocal;

// “扫描目录添加项目”：选一个目录（默认 wwwroot），把子目录列出来勾选后批量登记。
// 名字带点的目录（demo.yikai）直接用目录名当域名；不带点的目录默认跳过，
// 勾选“同时添加不带点的目录”后，纯英文/数字/-/_ 的名字自动补上 .localhost。
// 登记只写面板配置：不复制文件、不覆盖已有 index.php、不改动目录内容。
public sealed partial class MainForm
{
    sealed class ScanRow
    {
        public bool Selected{get;set;}=true;
        public string Folder{get;set;}="";
        public string Domain{get;set;}="";
        public string Kind{get;set;}="";
        public string Database{get;set;}="";
        public string Php{get;set;}="";
        public Settings.ScanCandidate Candidate{get;set;}=null!;
    }

    void ScanFolderForProjects()
    {
        if(busy)return;
        var defaultFolder=Path.Combine(settings.Root,"wwwroot");
        using var picker=new FolderBrowserDialog
        {
            Description=T("选择要扫描的目录（默认 wwwroot）","Choose the folder to scan (default wwwroot)","スキャンするフォルダーを選択（既定は wwwroot）"),
            UseDescriptionForTitle=true,
            SelectedPath=Directory.Exists(defaultFolder)?defaultFolder:settings.Root,
        };
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        ShowScanFolderDialog(picker.SelectedPath);
    }

    // 对话框本体：选目录之外的部分都在这里，界面检查程序可以直接调它（先选目录的那一步没法自动点）
    void ShowScanFolderDialog(string startFolder)
    {
        using var dialog=new Form{Name="scanFolderProjects",Text=T("扫描目录添加项目","Scan a folder for projects","フォルダーをスキャンして追加"),Size=new Size(1080,760),MinimumSize=new Size(940,640),StartPosition=FormStartPosition.CenterParent,Font=Font};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=7};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,104));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,55));
        dialog.Controls.Add(layout);

        var top=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4};
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,50));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));
        var path=new TextBox{Name="scanFolderPath",Dock=DockStyle.Fill,Text=startFolder};
        var browse=new ThemeButton{Name="scanFolderBrowse",Text="…",Dock=DockStyle.Fill};
        var php=new Choice{Name="scanFolderPhp",Dock=DockStyle.Fill};
        var installed=runtime.InstalledPhpVersions;
        foreach(var version in installed)php.Items.Add(version);
        var basePhp=installed.Contains(settings.PhpDefault)?settings.PhpDefault:installed.Length>0?installed[0]:settings.PhpDefault;
        php.SelectedItem=basePhp;
        var scan=new ThemeButton{Name="scanFolderStart",Text=T("扫描目录","Scan","スキャン"),Dock=DockStyle.Fill};
        top.Controls.Add(path,0,0);top.Controls.Add(browse,1,0);top.Controls.Add(php,2,0);top.Controls.Add(scan,3,0);
        layout.Controls.Add(top,0,0);

        var note=L(T("只登记不复制：目录里的文件保持原样，登记后项目是停止状态，需要时再启动。识别到 config/version.php 的目录按 YikaiCMS 处理（用它的伪静态规则），数据库名尽量从项目配置里读出。",
            "Registered in place: files are untouched and projects start stopped. A folder with config/version.php is treated as YikaiCMS; the database name is read from the project config when possible.",
            "登録のみでコピーしません。ファイルはそのまま、追加直後は停止状態です。config/version.php があるフォルダーは YikaiCMS として扱います。"),9);
        note.Margin=Padding.Empty;layout.Controls.Add(note,0,1);

        var includePlain=new CheckBox{Name="scanFolderIncludePlain",AutoSize=true,FlatStyle=FlatStyle.System,Checked=false,
            Text=T("同时添加名字不带点的目录（自动补上 .localhost；名字只能用英文字母、数字、- 和 _，且不能是中文）",
                   "Also add folders without a dot (append .localhost; names must use letters, digits, - or _ only)",
                   "ドットなしのフォルダーも追加（.localhost を補います。名前は英数字・-・_ のみ）")};
        includePlain.Margin=new Padding(0,4,0,0);layout.Controls.Add(includePlain,0,2);

        var grid=new DataGridView{Name="scanFolderList",Dock=DockStyle.Fill,AutoGenerateColumns=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,RowHeadersVisible=false,BackgroundColor=Palette.Surface,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
        grid.Columns.Add(new DataGridViewCheckBoxColumn{DataPropertyName="Selected",HeaderText="✓",FillWeight=22});
        void Col(string prop,string text,float weight){grid.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=prop,HeaderText=text,ReadOnly=true,FillWeight=weight});}
        Col("Folder",T("目录","Folder","フォルダー"),110);Col("Domain",T("域名","Domain","ドメイン"),100);Col("Kind",T("类型","Type","種類"),55);Col("Database",T("数据库","Database","DB"),80);Col("Php","PHP",35);
        layout.Controls.Add(grid,0,3);

        var statusLine=L("",9);statusLine.Name="scanFolderStatus";statusLine.Margin=Padding.Empty;layout.Controls.Add(statusLine,0,4);
        var skippedList=new ListBox{Name="scanFolderSkipped",Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle,SelectionMode=SelectionMode.None,BackColor=Palette.Surface,ForeColor=Muted,IntegralHeight=false,ScrollAlwaysVisible=false};
        layout.Controls.Add(skippedList,0,5);

        var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
        layout.Controls.Add(bottom,0,6);
        var add=new ThemeButton{Name="scanFolderAdd",Text=T("添加所选项目","Add selected","選択項目を追加"),Width=190,Height=40,Enabled=false};
        var close=new ThemeButton{Name="scanFolderClose",Text=T("关闭","Close","閉じる"),Width=100,Height=40,DialogResult=DialogResult.Cancel};
        bottom.Controls.Add(close);bottom.Controls.Add(add);dialog.CancelButton=close;

        List<ScanRow> rows=[];
        string PickPhp(string template)=>Settings.ScanPhpFor(template,php.SelectedItem as string ?? basePhp);
        void Rescan()
        {
            var folder=path.Text.Trim();
            if(!Directory.Exists(folder))
            {
                MessageBox.Show(dialog,T("目录不存在：","Folder not found: ","フォルダーがありません：")+folder,dialog.Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return;
            }
            var (candidates,skipped)=settings.ScanFolder(folder,includePlain.Checked);
            rows=[..candidates.Select(c=>new ScanRow{Folder=Path.GetFileName(c.Directory),Domain=c.Domain,
                Kind=c.Template=="yikaicms"?"YikaiCMS":"PHP",
                Database=(c.Database=="sqlite"?"SQLite":c.Database=="mysql57"?"MySQL 5.7":"MySQL 8.0")+(c.DatabaseName.Length>0?" · "+c.DatabaseName:""),
                Php=PickPhp(c.Template),Candidate=c})];
            grid.DataSource=new BindingList<ScanRow>(rows);
            skippedList.Items.Clear();
            foreach(var reason in skipped)skippedList.Items.Add(reason.Reason);
            statusLine.Text=rows.Count==0
                ?T($"没有找到可添加的目录（跳过 {skipped.Count} 个，下面列出原因）。","Nothing to add ({skipped.Count} skipped; reasons below).",$"追加できるフォルダーがありません（{skipped.Count} 件を省略、理由は下記）。")
                :T($"找到 {rows.Count} 个可添加的目录，跳过 {skipped.Count} 个。取消勾选不想添加的，然后点“添加所选项目”。",
                   $"{rows.Count} folders can be added, {skipped.Count} skipped. Uncheck any you do not want, then choose Add selected.",
                   $"{rows.Count} 件を追加できます（{skipped.Count} 件を省略）。不要なものはチェックを外してください。");
            add.Enabled=rows.Count>0;
        }

        browse.Click+=(_,_)=>{using var folder=new FolderBrowserDialog{SelectedPath=path.Text};if(folder.ShowDialog(dialog)==DialogResult.OK){path.Text=folder.SelectedPath;Rescan();}};
        scan.Click+=(_,_)=>Rescan();
        path.Leave+=(_,_)=>Rescan();
        includePlain.CheckedChanged+=(_,_)=>Rescan();
        php.SelectedIndexChanged+=(_,_)=>{foreach(var row in rows)row.Php=PickPhp(row.Candidate.Template);grid.Refresh();};
        add.Click+=(_,_)=>
        {
            grid.EndEdit();
            var picked=rows.Where(r=>r.Selected).ToArray();
            if(picked.Length==0)return;
            add.Enabled=scan.Enabled=close.Enabled=browse.Enabled=grid.Enabled=false;
            try
            {
                var outcome=settings.RegisterCandidates(picked.Select(r=>r.Candidate),php.SelectedItem as string ?? basePhp);
                selectedId=outcome.Added.Count>0?outcome.Added[0].Id:selectedId;
                search.Text="";PopulateProjects();
                if(outcome.Added.Count>0)projects.SelectedItem=settings.Sites.FirstOrDefault(s=>s.Id==selectedId);
                string Summary()=>T($"已添加 {outcome.Added.Count} 个项目，跳过 {outcome.Skipped.Count} 个；项目为停止状态。",
                                    $"Added {outcome.Added.Count} projects, skipped {outcome.Skipped.Count}; they are stopped.",
                                    $"{outcome.Added.Count} 件を追加し、{outcome.Skipped.Count} 件を省略しました（停止状態）。");
                status.Text=Summary();
                if(outcome.Skipped.Count==0){dialog.DialogResult=DialogResult.OK;return;}
                MessageBox.Show(dialog,Summary()+"\n\n"+string.Join("\n",outcome.Skipped.Select(s=>s.Reason)),dialog.Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);
                Rescan();
            }
            catch(Exception error){MessageBox.Show(dialog,error.Message,dialog.Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
            finally{if(!dialog.IsDisposed&&dialog.Visible){add.Enabled=scan.Enabled=close.Enabled=browse.Enabled=grid.Enabled=true;add.Enabled=rows.Count>0;}}
        };
        Rescan();
        Palette.Show(dialog,this);
    }
}
