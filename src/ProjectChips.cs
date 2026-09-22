using System.Drawing.Drawing2D;

namespace YikaiLocal;

// 项目卡片上的一排小标签（PHP 版本、数据库、运行状态）：圆角淡色底 + 同色文字，放不下的自动省略。
public sealed class ProjectChips : Control
{
    const TextFormatFlags Flags=TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;
    (string Text,Color Color)[] chips=[];
    // 上次绘制时是否有标签放不下（宽度太窄）。
    public bool Truncated {get;private set;}
    public ProjectChips()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        TabStop=false;
    }
    public void Set(params (string Text,Color Color)[] value){chips=value;Invalidate();}
    public string Describe()=>string.Join(" · ",chips.Select(c=>c.Text));
    public int Count=>chips.Length;
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var scale=DeviceDpi/96f;var padX=(int)Math.Round(11*scale);var padY=(int)Math.Round(5*scale);var gap=(int)Math.Round(8*scale);var x=0;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;Truncated=false;
        foreach(var chip in chips)
        {
            var size=TextRenderer.MeasureText(e.Graphics,chip.Text,Font,new Size(2000,100),Flags);
            var box=new Rectangle(x,Math.Max(0,(Height-size.Height-padY*2)/2),size.Width+padX*2,size.Height+padY*2);
            // 宽度不够时把最后一个标签压缩并加省略号，不整块丢掉。
            if(box.Right>Width){Truncated=true;box.Width=Math.Max(padX*2,Width-x);if(box.Width<padX*3)break;}
            using(var path=Palette.Rounded(box,box.Height/2f))
            using(var fill=new SolidBrush(Palette.Blend(chip.Color,BackColor,0.10f)))
            using(var edge=new Pen(Palette.Blend(chip.Color,BackColor,0.32f)))
            {e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(edge,path);}
            TextRenderer.DrawText(e.Graphics,chip.Text,Font,box,chip.Color,Flags|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            x=box.Right+gap;
            if(Truncated)break;
        }
    }
}
