namespace YikaiLocal;

// 项目卡片上的一行信息：用制表符分成“标题 + 内容”两列，标题浅色、内容正文色，行间留出空隙。
// CaptionWidth 让卡片里的几个标签共用同一个标题列宽，读起来像一张小表格。
// 通过 SetAction 可以把某一行的内容变成可点的链接（打开目录、打开网站）。
public sealed class ProjectInfoLabel : Label
{
    const TextFormatFlags Flags=TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|TextFormatFlags.VerticalCenter;
    readonly Dictionary<int,Action> actions=[];
    readonly Dictionary<int,Rectangle> hits=[];
    int hover=-1;
    int[] warnLines=[];
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int[] WarnLines {get=>warnLines;set{warnLines=value;Invalidate();}}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int CaptionWidth {get;set;}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int LineSpacing {get;set;}=12;
    public int LineHeight=>TextRenderer.MeasureText("Ag",Font).Height+LineSpacing;
    public void ClearActions(){actions.Clear();hits.Clear();hover=-1;Cursor=Cursors.Default;}
    public void SetAction(int line,Action action){actions[line]=action;Invalidate();}

    protected override void OnPaint(PaintEventArgs e)
    {
        var lines=Text.Split('\n');var height=LineHeight;var scale=DeviceDpi/96f;hits.Clear();
        var offset=Math.Max(0,(Height-lines.Length*height)/2);
        var column=CaptionWidth>0?(int)Math.Round(CaptionWidth*scale)
            :lines.Select(l=>l.Split('\t')).Where(p=>p.Length>1).Select(p=>TextRenderer.MeasureText(e.Graphics,p[0],Font,new Size(2000,100),Flags).Width+(int)Math.Round(18*scale)).DefaultIfEmpty(0).Max();
        for(var i=0;i<lines.Length;i++)
        {
            var parts=lines[i].TrimEnd('\r').Split('\t');var top=offset+i*height;
            var indent=parts.Length>1?column:0;
            if(parts.Length>1)TextRenderer.DrawText(e.Graphics,parts[0],Font,new Rectangle(0,top,column,height),Palette.Secondary,Flags);
            var value=parts[^1];var link=actions.ContainsKey(i);
            var color=warnLines.Contains(i)?Palette.Warning:link?Palette.Link:ForeColor;
            using var font=link&&hover==i?new Font(Font,FontStyle.Underline):new Font(Font,FontStyle.Regular);
            var room=Math.Max(10,Width-indent);
            TextRenderer.DrawText(e.Graphics,value,font,new Rectangle(indent,top,room,height),color,Flags);
            if(link)hits[i]=new Rectangle(indent,top,Math.Min(room,TextRenderer.MeasureText(e.Graphics,value,font,new Size(4000,100),Flags).Width),height);
        }
    }
    int LineAt(Point point)
    {
        foreach(var hit in hits)if(hit.Value.Contains(point))return hit.Key;
        return -1;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var line=LineAt(e.Location);
        if(line==hover)return;
        hover=line;Cursor=line>=0?Cursors.Hand:Cursors.Default;Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(hover<0)return;hover=-1;Cursor=Cursors.Default;Invalidate();}
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if(e.Button!=MouseButtons.Left)return;
        var line=LineAt(e.Location);
        if(line>=0&&actions.TryGetValue(line,out var action))action();
    }
}
