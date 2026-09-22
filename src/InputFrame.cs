namespace YikaiLocal;

// A single-line native editor inside a fixed-height frame; text keeps native editing behavior.
public sealed class InputFrame:Panel
{
    readonly TextBox editor;
    public InputFrame(TextBox editor)
    {
        this.editor=editor;Height=36;TabStop=false;BackColor=Palette.Input;
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.UserPaint,true);
        editor.BorderStyle=BorderStyle.None;editor.Dock=DockStyle.None;editor.Margin=Padding.Empty;
        Controls.Add(editor);editor.GotFocus+=(_,_)=>Invalidate();editor.LostFocus+=(_,_)=>Invalidate();
        Click+=(_,_)=>editor.Focus();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);if(editor==null)return;
        var inset=(int)Math.Round(10*DeviceDpi/96f);editor.SetBounds(inset,Math.Max(1,(Height-editor.PreferredHeight)/2),Math.Max(1,Width-inset*2),editor.PreferredHeight);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);using var pen=new Pen(editor.Focused?Palette.Link:Palette.Border,editor.Focused?2:1);e.Graphics.DrawRectangle(pen,1,1,Width-3,Height-3);
    }
}
