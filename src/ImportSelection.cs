namespace YikaiLocal;

public sealed partial class MainForm
{
    Action AttachImportSelection(TableLayoutPanel layout,DataGridView grid,Button scan)
    {
        layout.Controls.Remove(grid);
        var area=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        area.RowStyles.Add(new RowStyle(SizeType.Absolute,42));area.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};
        Button ActionButton(string name,string text,string icon){var button=new ThemeButton{Name=name,Text=text,Font=Font,Height=34,Margin=new Padding(0,0,8,0)};AttachButtonIcon(button,icon);button.Width=TextRenderer.MeasureText(text,Font,new Size(10000,100),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+(int)Math.Ceiling(44*DeviceDpi/96f)+8;bar.Controls.Add(button);return button;}
        var all=ActionButton("importSelectAll",T("全选","Select all","すべて選択"),"check");
        var clear=ActionButton("importClearSelection",T("取消全选","Clear selection","選択を解除"),"remove");
        var count=new Label{Name="importSelectionCount",AutoSize=false,Width=300,Height=34,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Muted,Margin=Padding.Empty};bar.Controls.Add(count);
        area.Controls.Add(bar,0,0);area.Controls.Add(grid,0,1);layout.Controls.Add(area,0,2);
        IEnumerable<ImportCandidate> Items()=>grid.Rows.Cast<DataGridViewRow>().Select(row=>row.DataBoundItem).OfType<ImportCandidate>();
        void RefreshSelection(){
            var items=Items().ToArray();var selected=items.Count(item=>item.Selected);
            bar.Enabled=grid.Enabled&&scan.Enabled;
            all.Enabled=items.Length>0&&selected<items.Length;clear.Enabled=selected>0;
            count.Text=T($"已选 {selected} / {items.Length} 个项目",$"Selected {selected} / {items.Length}",$"{items.Length} 件中 {selected} 件を選択");
            grid.Columns[0].HeaderText=selected==0?"☐":selected==items.Length?"☑":"−";
            grid.Columns[0].ToolTipText=T("点击全选或取消全选","Click to select or clear all","クリックですべて選択・解除");
        }
        void SelectAll(bool selected){if(!bar.Enabled)return;grid.EndEdit();foreach(var item in Items())item.Selected=selected;grid.Refresh();RefreshSelection();}
        all.Click+=(_,_)=>SelectAll(true);clear.Click+=(_,_)=>SelectAll(false);
        grid.ColumnHeaderMouseClick+=(_,e)=>{if(e.ColumnIndex==0)SelectAll(!Items().All(item=>item.Selected));};
        grid.CurrentCellDirtyStateChanged+=(_,_)=>{if(grid.IsCurrentCellDirty&&grid.CurrentCell is DataGridViewCheckBoxCell){grid.CommitEdit(DataGridViewDataErrorContexts.Commit);grid.EndEdit();RefreshSelection();}};
        grid.CellValueChanged+=(_,e)=>{if(e.ColumnIndex==0)RefreshSelection();};
        grid.DataBindingComplete+=(_,_)=>RefreshSelection();grid.EnabledChanged+=(_,_)=>RefreshSelection();scan.EnabledChanged+=(_,_)=>RefreshSelection();
        RefreshSelection();return RefreshSelection;
    }
}
