namespace YikaiLocal;

// Native menu selection with a consistently rendered, DPI-aware closed state.
public sealed class Choice : Control
{
    readonly ContextMenuStrip menu=new();
    int selectedIndex=-1;
    public List<object> Items { get; }=[];
    public event EventHandler? SelectedIndexChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get=>selectedIndex;
        set {selectedIndex=value;Invalidate();SelectedIndexChanged?.Invoke(this,EventArgs.Empty);}
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem {get=>selectedIndex>=0&&selectedIndex<Items.Count?Items[selectedIndex]:null;set=>SelectedIndex=Items.IndexOf(value!);}
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text {get=>SelectedItem?.ToString()??"";set {var i=Items.FindIndex(x=>x.ToString()==value);if(i>=0)SelectedIndex=i;}}
    public Choice()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        BackColor=Palette.Input;Height=36;TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.ComboBox;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Enabled?BackColor:Palette.Surface);
        using var border=new Pen(Focused?Palette.Link:Palette.Border);
        e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);
        var ink=Enabled?ForeColor:Palette.Secondary;
        TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(10,0,Math.Max(1,Width-36),Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
        using var pen=new Pen(ink,1.2f);var x=Width-19;var y=Height/2-2;e.Graphics.DrawLines(pen,[new Point(x,y),new Point(x+4,y+4),new Point(x+8,y)]);
    }
    protected override void OnClick(EventArgs e){base.OnClick(e);Focus();ShowChoices();}
    void ShowChoices()
    {
        menu.Font=Font;
        while(menu.Items.Count>0)menu.Items[0].Dispose();
        for(var i=0;i<Items.Count;i++){var index=i;var item=new ToolStripMenuItem(Items[i].ToString()){ForeColor=Palette.Text,Checked=i==SelectedIndex};item.Click+=(_,_)=>SelectedIndex=index;menu.Items.Add(item);}
        menu.Show(this,new Point(0,Height));
    }
    // Closed runs before WinForms finishes hiding the menu. Keep it alive until its owner is disposed.
    protected override void Dispose(bool disposing){if(disposing)menu.Dispose();base.Dispose(disposing);}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(e.KeyCode is Keys.Space or Keys.Enter){ShowChoices();e.Handled=true;}
        else if(e.KeyCode==Keys.Down&&SelectedIndex<Items.Count-1){SelectedIndex++;e.Handled=true;}
        else if(e.KeyCode==Keys.Up&&SelectedIndex>0){SelectedIndex--;e.Handled=true;}
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
}
