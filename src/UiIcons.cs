using System.Drawing.Drawing2D;

namespace YikaiLocal;

public sealed class IconLabel : Label
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image? StatusIcon {get;set;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(StatusIcon!=null)e.Graphics.DrawImage(StatusIcon,0,(Height-StatusIcon.Height)/2);}
    protected override void Dispose(bool disposing){if(disposing)StatusIcon?.Dispose();base.Dispose(disposing);}
}

// Small local vector icons; no icon font or network dependency.
public static class UiIcons
{
    public static Bitmap Create(string name,Color color,int size)
    {
        var image=new Bitmap(size,size);using var g=Graphics.FromImage(image);g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/24f,size/24f);
        using var p=new Pen(color,1.8f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
        void Lines(params PointF[] points)=>g.DrawLines(p,points);
        switch(name)
        {
            case "dot":using(var brush=new SolidBrush(color))g.FillEllipse(brush,6,6,12,12);break;
            case "upgrade":g.DrawLine(p,12,19,12,5);Lines(new(6,11),new(12,5),new(18,11));g.DrawLine(p,5,3,19,3);break;
            case "download":g.DrawLine(p,12,3,12,15);Lines(new(7,10),new(12,15),new(17,10));Lines(new(4,16),new(4,21),new(20,21),new(20,16));break;
            case "close":g.DrawLine(p,6,6,18,18);g.DrawLine(p,18,6,6,18);break;
            case "info":g.DrawEllipse(p,3,3,18,18);g.DrawLine(p,12,10,12,17);g.DrawLine(p,12,6,12,6.2f);break;
            case "mail":g.DrawRectangle(p,3,5,18,14);Lines(new(3,6),new(12,13),new(21,6));break;
            case "play":g.DrawPolygon(p,[new(6,3),new(20,12),new(6,21)]);break;
            case "stop":g.DrawRectangle(p,5,5,14,14);break;
            case "globe":g.DrawEllipse(p,3,3,18,18);g.DrawEllipse(p,8,3,8,18);g.DrawLine(p,3,12,21,12);break;
            case "database":g.DrawEllipse(p,4,3,16,6);g.DrawLine(p,4,6,4,18);g.DrawLine(p,20,6,20,18);g.DrawArc(p,4,15,16,6,0,180);g.DrawArc(p,4,9,16,6,0,180);break;
            case "folder":g.DrawPolygon(p,[new(3,6),new(10,6),new(12,9),new(21,9),new(19,20),new(3,20)]);break;
            case "folder-open":Lines(new(3,18),new(3,5),new(9,5),new(12,8),new(20,8),new(20,11));g.DrawPolygon(p,[new(3,19),new(6,11),new(23,11),new(20,19)]);break;
            case "disk":g.DrawRectangle(p,3,4,18,16);g.DrawLine(p,3,15,21,15);g.DrawEllipse(p,16,17,1,1);break;
            case "settings":for(var i=0;i<3;i++){var y=5+i*7;var x=i==1?15:8;g.DrawLine(p,3,y,x-3,y);g.DrawLine(p,x+3,y,21,y);g.DrawEllipse(p,x-3,y-3,6,6);}break;
            case "remove":g.DrawEllipse(p,3,3,18,18);g.DrawLine(p,7,12,17,12);break;
            case "add":g.DrawLine(p,4,12,20,12);g.DrawLine(p,12,4,12,20);break;
            case "sync":g.DrawArc(p,4,4,16,16,200,145);g.DrawArc(p,4,4,16,16,20,145);Lines(new(20,3),new(20,9),new(14,9));Lines(new(4,21),new(4,15),new(10,15));break;
            case "login":Lines(new(13,3),new(21,3),new(21,21),new(13,21));g.DrawLine(p,3,12,15,12);Lines(new(10,7),new(15,12),new(10,17));break;
            case "check":g.DrawEllipse(p,3,3,18,18);Lines(new(7,12),new(10,15),new(17,8));break;
            case "warning":g.DrawPolygon(p,[new(12,3),new(22,21),new(2,21)]);g.DrawLine(p,12,9,12,14);g.DrawLine(p,12,17,12,17.2f);break;
            case "star": var points=Enumerable.Range(0,10).Select(i=>{var a=-Math.PI/2+i*Math.PI/5;var r=i%2==0?10:4.5;return new PointF(12+(float)(Math.Cos(a)*r),12+(float)(Math.Sin(a)*r));}).ToArray();g.DrawPolygon(p,points);break;
            case "search":g.DrawEllipse(p,3,3,12,12);g.DrawLine(p,14,14,21,21);break;
            case "tool":g.DrawLine(p,4.5f,19.5f,11.5f,12.5f);g.DrawArc(p,10,3,11,11,105,285);g.DrawLine(p,16.5f,3.2f,14.5f,7.5f);g.DrawLine(p,20.8f,7.5f,16.5f,9.5f);break;
            case "theme":g.DrawEllipse(p,3,3,18,18);using(var half=new SolidBrush(color))g.FillPie(half,3,3,18,18,90,180);break;
            case "lock":g.DrawRectangle(p,5,11,14,10);g.DrawArc(p,8,3,8,10,180,180);g.DrawLine(p,8,8,8,11);g.DrawLine(p,16,8,16,11);g.DrawLine(p,12,15,12,17);break;
            case "logs":g.DrawRectangle(p,5,3,14,18);g.DrawLine(p,8,8,16,8);g.DrawLine(p,8,12,16,12);g.DrawLine(p,8,16,14,16);break;
            default:g.DrawEllipse(p,4,4,16,16);g.DrawLine(p,12,7,12,13);g.DrawLine(p,12,16,12,16.2f);break;
        }
        return image;
    }
}
