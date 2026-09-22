using System.ComponentModel;

namespace YikaiLocal;

public sealed partial class MainForm
{
    async void ShowPhpStudyImport()
    {
        if(busy)return;
        using var dialog=new Form{Name="phpStudyImport",Text=T("从 PHPStudy 复制导入","Copy from PHPStudy","PHPStudy からコピー取込"),Size=new Size(1100,780),MinimumSize=new Size(1000,740),StartPosition=FormStartPosition.CenterParent,Font=Font};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=5};foreach(var h in new[]{52,62})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,158));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,55));dialog.Controls.Add(layout);
        var top=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,50));top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));layout.Controls.Add(top,0,0);
        var path=new TextBox{Text="D:\\phpstudy_pro",Dock=DockStyle.Fill};var browse=new ThemeButton{Text="…",Dock=DockStyle.Fill};var scan=new ThemeButton{Text=T("扫描项目","Scan projects","スキャン"),Dock=DockStyle.Fill};top.Controls.Add(path,0,0);top.Controls.Add(browse,1,0);top.Controls.Add(scan,2,0);
        var note=L(T("勾选项目后，网站文件和完整数据库一起复制，并核对全部表与记录数。\n原 PHPStudy 保留；YikaiCMS / WordPress 自动更新连接，其他程序需手动修改。","Copy selected project files and complete databases, then verify tables and row counts.\nPHPStudy stays unchanged. Connections update automatically for YikaiCMS / WordPress.","選択したサイトと DB 全体をコピーし、テーブルと件数を照合します。\n元の PHPStudy は保持。接続設定の自動更新は YikaiCMS / WordPress に対応。"),9);note.Margin=Padding.Empty;layout.Controls.Add(note,0,1);
        var grid=new DataGridView{Dock=DockStyle.Fill,AutoGenerateColumns=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,RowHeadersVisible=false,BackgroundColor=Palette.Surface,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
        grid.Columns.Add(new DataGridViewCheckBoxColumn{DataPropertyName="Selected",HeaderText="✓",FillWeight=25});
        void Col(string prop,string text,bool readOnly,float weight){grid.Columns.Add(new DataGridViewTextBoxColumn{DataPropertyName=prop,HeaderText=text,ReadOnly=readOnly,FillWeight=weight});}
        Col("Domain",T("源域名","Source domain","元ドメイン"),true,90);Col("TargetDomain",T("目标域名","Target domain","先ドメイン"),false,100);Col("Folder",T("源目录","Source folder","元フォルダー"),true,170);Col("Php","PHP",false,35);Col("DatabaseSummary",T("源数据库","Source DB","元 DB"),true,80);layout.Controls.Add(grid,0,2);grid.CellFormatting+=(_,e)=>{if(grid.Columns[e.ColumnIndex].DataPropertyName=="DatabaseSummary"&&e.Value is string value){e.Value=value=="unknown"?T("待设置","Set database","要設定"):value=="none"?T("仅文件","Files only","ファイルのみ"):value;}};
        var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=4};for(var i=0;i<4;i++)fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));for(var i=0;i<4;i++)fields.RowStyles.Add(new RowStyle(SizeType.Percent,25));layout.Controls.Add(fields,0,3);
        var driver=new Choice{Dock=DockStyle.Fill};driver.Items.AddRange([T("请选择数据库类型","Choose database type","DB 種類を選択"),"MySQL","SQLite",T("仅网站文件","Files only","ファイルのみ")]);
        var host=new TextBox{Dock=DockStyle.Fill};var dbName=new TextBox{Dock=DockStyle.Fill};var user=new TextBox{Dock=DockStyle.Fill};var password=new TextBox{Dock=DockStyle.Fill,UseSystemPasswordChar=true};var port=new NumericUpDown{Dock=DockStyle.Fill,Minimum=1,Maximum=65535,Value=3306};var engine=new Choice{Dock=DockStyle.Fill};engine.Items.AddRange(["MySQL 8.0","MySQL 5.7"]);engine.SelectedIndex=0;
        string[] captions=[T("类型","Type","種類"),T("Host / SQLite 文件","Host / SQLite file","Host / SQLite ファイル"),T("源数据库名","Source database","元 DB 名"),T("源端口","Source port","元ポート"),T("源用户","Source user","元ユーザー"),T("源密码","Source password","元パスワード"),T("目标数据库版本","Target engine","移行先 DB")];Control[] inputs=[driver,host,dbName,port,user,password,engine];for(var i=0;i<7;i++){var row=i<4?0:2;var col=i%4;fields.Controls.Add(L(captions[i],8),col,row);fields.Controls.Add(inputs[i],col,row+1);}
        var apply=new ThemeButton{Text=T("应用到此行","Apply to row","この行に適用"),Dock=DockStyle.Fill};fields.Controls.Add(apply,3,3);
        Col("Status",T("迁移状态","Migration status","移行状況"),true,95);
        ImportCandidate? Current()=>grid.CurrentRow?.DataBoundItem as ImportCandidate;
        void LoadCurrent(){if(Current() is not {} c)return;driver.SelectedIndex=c.Database.Driver=="mysql"?1:c.Database.Driver=="sqlite"?2:c.Database.Driver=="none"?3:0;host.Text=c.Database.Driver=="sqlite"?c.Database.SqlitePath:c.Database.Host;dbName.Text=c.Database.Name;port.Value=Math.Clamp(c.Database.Port,1,65535);user.Text=c.Database.User;password.Text=c.Database.Password;engine.SelectedIndex=c.Engine=="mysql57"?1:0;}
        grid.SelectionChanged+=(_,_)=>LoadCurrent();
        void Apply(){if(Current() is not {} c)return;c.Database.Driver=driver.SelectedIndex==1?"mysql":driver.SelectedIndex==2?"sqlite":driver.SelectedIndex==3?"none":"unknown";if(c.Database.Driver=="sqlite")c.Database.SqlitePath=host.Text;else c.Database.Host=host.Text;c.Database.Name=dbName.Text;c.Database.Port=(int)port.Value;c.Database.User=user.Text;c.Database.Password=password.Text;c.Engine=engine.SelectedIndex==1?"mysql57":"mysql80";grid.Refresh();}
        apply.Click+=(_,_)=>Apply();
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};layout.Controls.Add(bottom,0,4);var import=new ThemeButton{Text=T("复制所选项目","Copy selected","選択項目をコピー"),Width=190,Height=40,Enabled=false};var close=new ThemeButton{Text=T("关闭","Close","閉じる"),Width=100,Height=40,DialogResult=DialogResult.Cancel};bottom.Controls.Add(import);bottom.Controls.Add(close);dialog.CancelButton=close;
        AttachDatabaseScan(grid,dialog,fields,scan,import,browse,close,note,()=>LoadCurrent());
        var refreshSelection=AttachImportSelection(layout,grid,scan);
        List<ImportCandidate> candidates=[];var transfer=new PhpStudyImport(settings,runtime);bool importing=false;
        browse.Click+=(_,_)=>{using var picker=new FolderBrowserDialog();if(picker.ShowDialog(dialog)==DialogResult.OK)path.Text=picker.SelectedPath;};
        scan.Click+=async(_,_)=>{scan.Enabled=import.Enabled=false;try{candidates=await transfer.ScanAsync(path.Text);grid.DataSource=new BindingList<ImportCandidate>(candidates);import.Enabled=candidates.Count>0;}catch(Exception error){MessageBox.Show(dialog,error.Message);}finally{scan.Enabled=true;}};
        import.Click+=async(_,_)=>
        {
            grid.EndEdit();Apply();var selected=candidates.Where(c=>c.Selected).ToArray();if(selected.Length==0)return;
            if(selected.Any(c=>c.Database.Driver=="unknown")){MessageBox.Show(dialog,T("请先为所选项目设置数据库类型，或明确选择仅网站文件。","Choose a database type or Files only for each selected project.","DB 種類、またはファイルのみを選択してください。"));return;}
            if(selected.Any(c=>c.Database.Driver=="none")&&MessageBox.Show(dialog,T("仅网站文件不会修改应用的数据库连接。启动后可能仍连接原数据库，确定按此方式复制？","Files-only keeps the app database settings. The copied app may still use the source database. Continue?","ファイルのみでは元の DB 接続が残ります。この方法で続行しますか？"),dialog.Text,MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            if(MessageBox.Show(dialog,T($"复制 {selected.Length} 个项目？原项目不会更改。",$"Copy {selected.Length} projects? Source projects stay unchanged.",$"{selected.Length} 件をコピーしますか？元のデータは変更しません。"),dialog.Text,MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            importing=true;scan.Enabled=import.Enabled=close.Enabled=browse.Enabled=grid.Enabled=fields.Enabled=false;var successes=new List<string>();var failures=new List<string>();
            string Phase(string phase)=>phase switch{
                "files"=>T("复制网站文件","Copying files","ファイルをコピー中"),
                "connect"=>T("检查数据库连接","Checking databases","DB 接続を確認中"),
                "export"=>T("导出完整数据库","Exporting database","DB 全体をエクスポート中"),
                "import"=>T("导入完整数据库","Importing database","DB 全体をインポート中"),
                "verify"=>T("核对表与记录数","Verifying tables and rows","テーブルと件数を照合中"),
                "configure"=>T("更新数据库连接","Updating connection","接続設定を更新中"),
                _=>T("迁移完成","Migration complete","移行完了")
            };
            foreach(var c in selected)c.Status=T("等待迁移","Waiting","待機中");grid.Refresh();
            try{using var operation=Runtime.Lock(settings);runtime.Adopt();foreach(var c in selected){try{
                var result=await transfer.ImportAsync(c,new Progress<ImportProgress>(state=>{if(!importing)return;c.Status=Phase(state.Phase);note.Text=c.Domain+" · "+c.Status;grid.Refresh();}));
                c.Status=result.ManualConfig?T("已复制 · 待改连接","Copied · configure DB","コピー済 · 接続設定待ち"):T("迁移完成","Complete","完了");
                var detail=T($"{result.Files.Count} 个文件 · {result.Database.Tables} 张表 · {result.Database.Rows} 条记录",$"{result.Files.Count} files · {result.Database.Tables} tables · {result.Database.Rows} rows",$"{result.Files.Count} ファイル · {result.Database.Tables} テーブル · {result.Database.Rows} 件");
                successes.Add(result.Site.Domain+" · "+detail+(result.ManualConfig?T("（需手动修改应用数据库连接）"," (update app database settings)","（アプリの DB 設定を変更）"):""));c.Selected=false;
            }catch(Exception error){c.Status=T("迁移失败 · 可重试","Failed · retry","失敗 · 再試行可");failures.Add(c.Domain+": "+error.Message);}}}
            catch(Exception error){failures.Add(error.Message);}
            finally{importing=false;scan.Enabled=import.Enabled=close.Enabled=browse.Enabled=grid.Enabled=fields.Enabled=true;grid.Refresh();refreshSelection();PopulateProjects();}
            MessageBox.Show(dialog,T("处理结束，项目保持停止；检查结果后启动并同步域名。","Import finished. Projects remain stopped; review, start and sync domains.","取込完了。確認後、起動とドメイン同期を行ってください。")+"\n\n"+string.Join("\n",successes)+"\n"+string.Join("\n",failures));
        };
        dialog.FormClosing+=(_,e)=>{if(importing)e.Cancel=true;};
        await Task.Yield();Palette.Show(dialog,this);
    }
}
