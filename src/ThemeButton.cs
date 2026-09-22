namespace YikaiLocal;

public sealed class ThemeButton:Button
{
    bool hover,pressed;
    // 主入口按钮：图标和文字整体居中，圆角更大。
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool CenterContent {get;set;}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float CornerRadius {get;set;}=6f;
    // 主按钮（近黑底）：禁用时保持同一形态、降低浓度，文字加粗；没有图标的按钮文字居中。
    bool Primary=>BackColor==Palette.Accent;
    Font? boldFont;
    Font TextFont=>Primary?(boldFont is {} f&&f.Size==Font.Size&&f.FontFamily.Equals(Font.FontFamily)?f:(boldFont=new Font(Font,FontStyle.Bold))):Font;
    static Color Blend(Color color,Color background,float amount)=>Color.FromArgb((int)Math.Round(background.R+(color.R-background.R)*amount),(int)Math.Round(background.G+(color.G-background.G)*amount),(int)Math.Round(background.B+(color.B-background.B)*amount));
    protected override void Dispose(bool disposing){if(disposing)boldFont?.Dispose();base.Dispose(disposing);}
    public ThemeButton()=>SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left)pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)pressed=false;Invalidate();base.OnMouseCaptureChanged(e);}
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space)pressed=true;Invalidate();base.OnKeyDown(e);}
    protected override void OnKeyUp(KeyEventArgs e){pressed=false;Invalidate();base.OnKeyUp(e);}
    protected override void OnEnabledChanged(EventArgs e){pressed=false;hover=false;Invalidate();base.OnEnabledChanged(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){pressed=false;Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        var surround=Parent?.BackColor??Palette.Canvas;
        var fill=!Enabled?(Primary?Blend(Palette.Accent,surround,0.32f):FlatAppearance.BorderSize==0?Palette.Surface:Palette.ButtonDisabled):pressed?FlatAppearance.MouseDownBackColor:hover?FlatAppearance.MouseOverBackColor:BackColor;
        if(fill.IsEmpty)fill=BackColor;
        e.Graphics.Clear(surround);
        var radius=CornerRadius*DeviceDpi/96f;var diameter=Math.Min(radius*2,Math.Min(Width-1,Height-1));
        using var shape=new System.Drawing.Drawing2D.GraphicsPath();
        shape.AddArc(0,0,diameter,diameter,180,90);shape.AddArc(Width-1-diameter,0,diameter,diameter,270,90);shape.AddArc(Width-1-diameter,Height-1-diameter,diameter,diameter,0,90);shape.AddArc(0,Height-1-diameter,diameter,diameter,90,90);shape.CloseFigure();
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,shape);
        if(FlatAppearance.BorderSize>0&&!Primary){using var border=new Pen(FlatAppearance.BorderColor.IsEmpty?Palette.Border:FlatAppearance.BorderColor);e.Graphics.DrawPath(border,shape);}
        var ink=Primary?Palette.OnAccent:Enabled?ForeColor:Palette.Secondary;var font=TextFont;
        var inset=(int)Math.Round((Width<60?4:10)*DeviceDpi/96f);var gap=Image==null?0:(int)Math.Round(8*DeviceDpi/96f);
        var iconSize=Image==null?0:(int)Math.Round(16*DeviceDpi/96f);
        var flags=TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding;
        if(!ShowKeyboardCues)flags|=TextFormatFlags.HidePrefix;
        var available=Math.Max(1,Width-inset*2-iconSize-gap);
        var textWidth=Math.Min(available,TextRenderer.MeasureText(e.Graphics,Text,font,new Size(10000,Height),TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Width);
        var left=Width<60||CenterContent||Image==null?Math.Max(inset,(Width-textWidth-iconSize-gap)/2):inset;
        if(Image is {} image)e.Graphics.DrawImage(image,new Rectangle(left,(Height-iconSize)/2,iconSize,iconSize));
        TextRenderer.DrawText(e.Graphics,Text,font,new Rectangle(left+iconSize+gap,0,textWidth,Height),ink,flags);
        if(Focused&&ShowFocusCues&&Enabled){using var ring=new Pen(Palette.Link,1.5f*DeviceDpi/96f);var pad=2.5f*DeviceDpi/96f;using var focus=new System.Drawing.Drawing2D.GraphicsPath();var r=Math.Max(1f,diameter-pad);focus.AddArc(pad,pad,r,r,180,90);focus.AddArc(Width-1-pad-r,pad,r,r,270,90);focus.AddArc(Width-1-pad-r,Height-1-pad-r,r,r,0,90);focus.AddArc(pad,Height-1-pad-r,r,r,90,90);focus.CloseFigure();e.Graphics.DrawPath(ring,focus);}
    }
}
