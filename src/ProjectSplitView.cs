namespace YikaiLocal;

// 在正常 WM_PAINT 中绘制分界，避免框架 SplitContainer 在布局期间直接申请 GDI 上下文。
public sealed class ProjectSplitView : ContainerControl
{
    public Panel Panel1 {get;}=new(){Margin=Padding.Empty};
    public Panel Panel2 {get;}=new(){Margin=Padding.Empty};
    readonly DividerGrip grip=new(){Name="sidebarDivider",Cursor=Cursors.VSplit,TabStop=true,AccessibleRole=AccessibleRole.Separator};
    int distance=360,dragOffset;
    bool dragging;
    [System.ComponentModel.DefaultValue(260)] public int Panel1MinSize {get;set;}=260;
    [System.ComponentModel.DefaultValue(780)] public int Panel2MinSize {get;set;}=780;
    [System.ComponentModel.DefaultValue(8)] public int SplitterWidth {get;set;}=8;
    [System.ComponentModel.DefaultValue(360)] public int SplitterDistance {get=>distance;set{distance=Clamp(value);PerformLayout();}}
    public event EventHandler? SplitterMoving;
    public event EventHandler? SplitterMoved;
    public ProjectSplitView()
    {
        Controls.AddRange([Panel1,Panel2,grip]);
        grip.MouseDown+=(_,e)=>{if(e.Button!=MouseButtons.Left)return;grip.Focus();dragOffset=e.X;dragging=true;grip.Capture=true;};
        grip.MouseMove+=(_,e)=>{if(!dragging)return;OnSplitterMoving(EventArgs.Empty);SplitterDistance=PointToClient(grip.PointToScreen(e.Location)).X-dragOffset;};
        grip.MouseUp+=(_,e)=>{if(e.Button==MouseButtons.Left)FinishDrag();};
        grip.MouseCaptureChanged+=(_,_)=>{if(!grip.Capture)FinishDrag();};
        grip.KeyDown+=(_,e)=>{if(e.KeyCode is not (Keys.Left or Keys.Right or Keys.Home or Keys.End))return;OnSplitterMoving(EventArgs.Empty);SplitterDistance=e.KeyCode switch{Keys.Home=>Panel1MinSize,Keys.End=>ClientSize.Width-Panel2MinSize-SplitterWidth,Keys.Left=>distance-16,_=>distance+16};OnSplitterMoved(EventArgs.Empty);e.Handled=true;};
    }
    public void SetHint(ToolTip tips,string hint){grip.AccessibleName=hint;tips.SetToolTip(grip,hint);}
    void FinishDrag(){if(!dragging)return;dragging=false;grip.Capture=false;OnSplitterMoved(EventArgs.Empty);}
    int Clamp(int value)=>Math.Clamp(value,Math.Min(Panel1MinSize,Math.Max(0,ClientSize.Width-SplitterWidth)),Math.Max(Panel1MinSize,ClientSize.Width-Panel2MinSize-SplitterWidth));
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);if(Panel1==null||Panel2==null||grip==null)return;
        distance=Clamp(distance);Panel1.SetBounds(0,0,distance,ClientSize.Height);grip.SetBounds(distance,0,SplitterWidth,ClientSize.Height);Panel2.SetBounds(distance+SplitterWidth,0,Math.Max(0,ClientSize.Width-distance-SplitterWidth),ClientSize.Height);
    }
    void OnSplitterMoving(EventArgs e)=>SplitterMoving?.Invoke(this,e);
    void OnSplitterMoved(EventArgs e)=>SplitterMoved?.Invoke(this,e);
    sealed class DividerGrip : Control
    {
        public DividerGrip(){SetStyle(ControlStyles.Selectable|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}
        protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End||base.IsInputKey(keyData);
        protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Focused?Palette.Selection:Palette.Surface);using var pen=new Pen(Palette.Divider);e.Graphics.DrawLine(pen,Width/2,0,Width/2,Height);using var brush=new SolidBrush(Palette.Secondary);for(int offset=-8;offset<=8;offset+=8)e.Graphics.FillEllipse(brush,Width/2-1,Height/2+offset,2,2);}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    }
}
