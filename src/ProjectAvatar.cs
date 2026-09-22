using System.Drawing.Drawing2D;
using System.Globalization;

namespace YikaiLocal;

// 项目列表头像：项目名的第一个字母或汉字，底色按项目标识固定分配；右下角小圆点表示运行状态。
public sealed partial class MainForm
{
    static string AvatarText(Site site)
    {
        var letters=StringInfo.GetTextElementEnumerator(site.ToString().Trim());
        while(letters.MoveNext()){var text=(string)letters.Current;if(text.Length>0&&char.IsLetterOrDigit(text,0))return text.ToUpperInvariant();}
        return "#";
    }
    // FNV-1a：跨进程稳定（string.GetHashCode 每次启动都会变），同一项目颜色始终一致。
    static Color AvatarColor(Site site)
    {
        uint hash=2166136261;
        foreach(var c in site.Id){hash^=c;hash*=16777619;}
        return Palette.Avatars[hash%(uint)Palette.Avatars.Length];
    }
    static GraphicsPath RoundedBox(RectangleF r,float radius)=>Palette.Rounded(r,radius);
    // 列表行右侧的小徽标（HTTPS）：靠右排，空间不足时不画。
    static int DrawRowBadge(Graphics graphics,int right,Rectangle line,string text,Color color,Color rowBack,FontFamily family,float fontSize)
    {
        const TextFormatFlags flags=TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;
        using var font=new Font(family,fontSize,FontStyle.Bold);
        var size=TextRenderer.MeasureText(graphics,text,font,new Size(10000,100),flags);
        var box=new Rectangle(right-size.Width-14,line.Y+(line.Height-size.Height-6)/2,size.Width+14,size.Height+6);
        if(box.X<line.X+60)return right;
        var old=graphics.SmoothingMode;graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=RoundedBox(box,box.Height/2f))using(var fill=new SolidBrush(Palette.Blend(color,rowBack,0.12f)))using(var edge=new Pen(Palette.Blend(color,rowBack,0.38f)))
        {graphics.FillPath(fill,path);graphics.DrawPath(edge,path);}
        graphics.SmoothingMode=old;
        TextRenderer.DrawText(graphics,text,font,box,color,flags|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        return box.X-6;
    }
    static void DrawProjectAvatar(Graphics graphics,Rectangle box,Site site,bool running,Color rowBack,FontFamily family)
    {
        var old=graphics.SmoothingMode;graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var color=AvatarColor(site);
        using(var path=RoundedBox(box,box.Height*0.24f))using(var fill=new SolidBrush(color))graphics.FillPath(fill,path);
        using(var font=new Font(family,box.Height*0.52f,FontStyle.Bold,GraphicsUnit.Pixel))
            TextRenderer.DrawText(graphics,AvatarText(site),font,box,Color.White,color,TextFormatFlags.SingleLine|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
        var size=box.Height*0.3f;var dot=new RectangleF(box.Right-size+3,box.Bottom-size+3,size,size);
        using(var ring=new SolidBrush(rowBack))graphics.FillEllipse(ring,RectangleF.Inflate(dot,2,2));
        using(var state=new SolidBrush(running?Palette.Success:Palette.Stopped))graphics.FillEllipse(state,dot);
        graphics.SmoothingMode=old;
    }
}
