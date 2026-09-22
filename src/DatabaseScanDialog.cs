namespace YikaiLocal;

public sealed partial class MainForm
{
    void AttachDatabaseScan(DataGridView grid,Form dialog,Control fields,Button scan,Button import,Button browse,Button close,Label note,Action loadCurrent)
    {
        using var original=UiIcons.Create("search",Blue,18);
        var icon=(Image)original.Clone();
        var title=T("扫描源码","Scan source","ソース検索");
        var column=new DataGridViewImageColumn{Name="ScanSource",HeaderText=T("扫描","Scan","検索"),Image=icon,ImageLayout=DataGridViewImageCellLayout.Normal,AutoSizeMode=DataGridViewAutoSizeColumnMode.None,Width=72,ReadOnly=true,DefaultCellStyle=new DataGridViewCellStyle{SelectionBackColor=Palette.Surface}};
        grid.Columns.Add(column);grid.Disposed+=(_,_)=>icon.Dispose();
        grid.CellToolTipTextNeeded+=(_,e)=>{if(e.ColumnIndex==column.Index)e.ToolTipText=title;};
        bool scanning=false;
        dialog.FormClosing+=(_,e)=>{if(scanning)e.Cancel=true;};
        grid.CellClick+=async(_,e)=>
        {
            if(scanning||e.RowIndex<0||e.ColumnIndex!=column.Index||grid.Rows[e.RowIndex].DataBoundItem is not ImportCandidate candidate)return;
            grid.EndEdit();scanning=true;
            var controls=new Control[]{grid,fields,scan,import,browse,close};var enabled=controls.Select(c=>c.Enabled).ToArray();foreach(var control in controls)control.Enabled=false;
            var oldNote=note.Text;note.Text=T("正在读取源码中的数据库配置…","Reading database configuration from source…","ソースの DB 設定を検索中…");
            try
            {
                var result=await new PhpStudyImport(settings,runtime).ScanSourceAsync(candidate.Folder);
                if(dialog.IsDisposed)return;
                using var picker=new Form{Text=title+" · "+candidate.Domain,Size=new Size(850,440),MinimumSize=new Size(700,380),StartPosition=FormStartPosition.CenterParent,Font=Font,Icon=Icon};
                var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),RowCount=3,ColumnCount=1};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,94));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));picker.Controls.Add(layout);
                var message=T($"检查 {result.Files} 个配置文件，找到 {result.Matches.Count} 个候选。跳过 {result.Skipped} 个不明确或过大的配置。",$"Checked {result.Files} config files; found {result.Matches.Count} candidates. Skipped {result.Skipped} ambiguous or large configs.",$"設定 {result.Files} 件を確認、候補 {result.Matches.Count} 件。不明・大容量 {result.Skipped} 件を除外。");
                message+="\n"+T("选择要使用的连接；静态扫描不能确认实际运行分支，未测试连接。","Choose a connection. Static scanning cannot confirm the active runtime branch; connection not tested.","使用する接続を選択。実行時の分岐と接続は未確認です。");
                if(result.Limited)message+="\n"+T("达到扫描范围限制，结果可能不完整。","Scan limit reached; results may be incomplete.","検索上限に到達。一部未確認です。");
                layout.Controls.Add(new Label{Text=message,Dock=DockStyle.Fill,AutoEllipsis=true},0,0);
                var list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true,IntegralHeight=false};
                foreach(var db in result.Matches)list.Items.Add(db.ConfigFile+"  ·  "+(db.Driver=="sqlite"?"SQLite · "+db.SqlitePath:$"MySQL · {db.Host}:{db.Port} / {db.Name} · {db.User}"));
                layout.Controls.Add(list,0,1);
                var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};layout.Controls.Add(buttons,0,2);
                var choose=new ThemeButton{Text=T("使用此连接","Use connection","この接続を使用"),Width=210,Height=36,Enabled=false};var cancel=new ThemeButton{Text=T("取消","Cancel","キャンセル"),DialogResult=DialogResult.Cancel,Width=144,Height=36};buttons.Controls.Add(choose);buttons.Controls.Add(cancel);picker.CancelButton=cancel;
                list.SelectedIndexChanged+=(_,_)=>choose.Enabled=list.SelectedIndex>=0;
                choose.Click+=(_,_)=>{if(list.SelectedIndex<0)return;candidate.Database=result.Matches[list.SelectedIndex];if(candidate.Database.Kind=="yikaicms"&&candidate.Php=="8.0")candidate.Php="8.2";picker.DialogResult=DialogResult.OK;};
                if(Palette.Show(picker,dialog)==DialogResult.OK){loadCurrent();grid.Refresh();}
            }
            catch(Exception error){if(!dialog.IsDisposed)MessageBox.Show(dialog,error.Message,title);}
            finally{scanning=false;if(!dialog.IsDisposed){for(var i=0;i<controls.Length;i++)controls[i].Enabled=enabled[i];note.Text=oldNote;}}
        };
    }
}
