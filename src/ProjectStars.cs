namespace YikaiLocal;

public sealed partial class MainForm
{
    bool starredOnly;
    static Rectangle StarBounds(Rectangle row)=>new(row.Right-38,row.Top+8,32,32);
    static void DrawProjectStar(Graphics graphics,Rectangle row,bool starred)
    {
        var box=StarBounds(row);var center=new PointF(box.Left+16,box.Top+16);
        var points=Enumerable.Range(0,10).Select(i=>{var angle=-Math.PI/2+i*Math.PI/5;var radius=i%2==0?10:4.6;return new PointF(center.X+(float)(Math.Cos(angle)*radius),center.Y+(float)(Math.Sin(angle)*radius));}).ToArray();
        var old=graphics.SmoothingMode;graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if(starred){using var fill=new SolidBrush(Palette.Brand);graphics.FillPolygon(fill,points);}
        using var pen=new Pen(starred?Palette.Brand:Muted,1.4f);graphics.DrawPolygon(pen,points);graphics.SmoothingMode=old;
    }
    void ToggleProjectStar(Site site)
    {
        if(busy)return;
        var old=site.Starred;
        try{using var operation=Runtime.Lock(settings);site.Starred=!old;settings.Save();PopulateProjects();}
        catch(Exception error){site.Starred=old;projects.Invalidate();MessageBox.Show(this,error.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    void AttachProjectStars()
    {
        projects.MouseClick+=(_,e)=>{if(e.Button!=MouseButtons.Left||busy)return;var index=projects.IndexFromPoint(e.Location);if(index>=0&&StarBounds(projects.GetItemRectangle(index)).Contains(e.Location))ToggleProjectStar((Site)projects.Items[index]);};
        string previousTip="";
        projects.MouseMove+=(_,e)=>{var index=projects.IndexFromPoint(e.Location);var hit=index>=0&&StarBounds(projects.GetItemRectangle(index)).Contains(e.Location);projects.Cursor=hit?Cursors.Hand:Cursors.Default;var tip=hit?((Site)projects.Items[index]).Starred?T("取消星标","Unstar project","スター解除"):T("添加星标","Star project","スターを付ける"):"";if(tip!=previousTip){serviceTips.SetToolTip(projects,tip);previousTip=tip;}};
        projects.KeyDown+=(_,e)=>{if(e.Control&&e.KeyCode==Keys.D&&Selected is {} site){e.SuppressKeyPress=true;ToggleProjectStar(site);}};
    }
}
