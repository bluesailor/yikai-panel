using System.Drawing.Drawing2D;

namespace YikaiLocal;

// 项目标题 + 右侧版本徽标（如 “YikaiCMS 1.19.9”）；无徽标时与普通标题一致。
public sealed class ProjectHeadingLabel : Label
{
    string badge="";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Badge {get=>badge;set{if(badge==value)return;badge=value;AccessibleDescription=value;Invalidate();}}

    protected override void OnPaint(PaintEventArgs e)
    {
        const TextFormatFlags flags=TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding;
        var scale=DeviceDpi/96f;var gap=(int)Math.Round(12*scale);var padX=(int)Math.Round(9*scale);
        using var badgeFont=new Font(Font.FontFamily,Font.Size*0.69f,FontStyle.Bold);
        var badgeText=badge==""?Size.Empty:TextRenderer.MeasureText(e.Graphics,badge,badgeFont,new Size(10000,100),flags);
        var badgeWidth=badge==""?0:badgeText.Width+padX*2;
        var textRoom=Math.Max(0,Width-(badgeWidth==0?0:badgeWidth+gap));
        var textWidth=Math.Min(textRoom,TextRenderer.MeasureText(e.Graphics,Text,Font,new Size(10000,Height),flags).Width);
        TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(0,0,textRoom,Height),ForeColor,flags|TextFormatFlags.EndEllipsis);
        if(badgeWidth==0)return;
        var height=Math.Min(badgeText.Height+(int)Math.Round(4*scale),Height-2);
        var box=new Rectangle(Math.Min(textWidth+gap,Math.Max(0,Width-badgeWidth-1)),(Height-height)/2,badgeWidth,height);
        var fill=Palette.Blend(Palette.Brand,BackColor,0.10f);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Palette.Rounded(box,height/2f))
        using(var brush=new SolidBrush(fill))
        using(var pen=new Pen(Palette.Blend(Palette.Brand,BackColor,0.35f)))
        {e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);}
        TextRenderer.DrawText(e.Graphics,badge,badgeFont,box,Palette.Brand,flags|TextFormatFlags.HorizontalCenter);
    }
}
