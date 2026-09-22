namespace YikaiLocal;

// 主窗口大小与顶部工具栏自适应。
public sealed partial class MainForm
{
    readonly List<(Button Button,string Text)> toolbarButtons=[];
    bool fittingToolbar;

    int ToolbarButtonWidth(Button button)=>string.IsNullOrEmpty(button.Text)
        ?(int)Math.Ceiling(40*DeviceDpi/96f)
        :Math.Max(104,TextRenderer.MeasureText(button.Text,button.Font,new Size(10000,100),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+(int)Math.Ceiling(44*DeviceDpi/96f));

    // 宽度不够时按顺序把按钮收成只有图标（悬停仍显示文字提示），变宽后恢复文字。
    void FitToolbar(FlowLayoutPanel commands)
    {
        if(fittingToolbar||toolbarButtons.Count==0||commands.ClientSize.Width<=0)return;
        fittingToolbar=true;commands.SuspendLayout();
        try
        {
            foreach(var (button,text) in toolbarButtons){if(button.Text!=text){button.Text=text;((ThemeButton)button).CenterContent=false;}button.Width=ToolbarButtonWidth(button);}
            int Needed()=>commands.Controls.Cast<Control>().Sum(c=>c.Width+c.Margin.Horizontal);
            foreach(var name in new[]{"aboutPanel","upgradePanel","syncDomains","environmentConfig","environmentTools","stopEnvironment","startEnvironment"})
            {
                if(Needed()<=commands.ClientSize.Width)break;
                var button=toolbarButtons.FirstOrDefault(x=>x.Button.Name==name).Button;if(button==null)continue;
                button.Text="";((ThemeButton)button).CenterContent=true;button.Width=ToolbarButtonWidth(button);
            }
        }
        finally{commands.ResumeLayout(true);fittingToolbar=false;}
    }

    // 首次打开：按当前屏幕可用区域取约 72% 宽、82% 高（逻辑尺寸 1180–1600 × 760–1040）；之后恢复上次关闭时的大小和最大化状态。
    // fontScale 跟着界面字号走：字大了窗口最小尺寸也要跟着大，否则内容放不下。
    internal static Rectangle InitialBounds(Rectangle workArea,float scale,int? savedWidth,int? savedHeight,out Size minimum,float fontScale=1f)
    {
        int Physical(float logical)=>(int)Math.Round(logical*scale*fontScale);
        minimum=new Size(Math.Min(Physical(1120),workArea.Width),Math.Min(Physical(740),workArea.Height));
        int width,height;
        if(savedWidth is >0&&savedHeight is >0){width=(int)Math.Round(savedWidth.Value*scale);height=(int)Math.Round(savedHeight.Value*scale);}
        else{width=(int)Math.Clamp(workArea.Width*0.72,Physical(1180),Physical(1600));height=(int)Math.Clamp(workArea.Height*0.82,Physical(760),Physical(1040));}
        width=Math.Clamp(width,minimum.Width,workArea.Width);height=Math.Clamp(height,minimum.Height,workArea.Height);
        return new Rectangle(workArea.X+(workArea.Width-width)/2,workArea.Y+(workArea.Height-height)/2,width,height);
    }
    void RestoreWindowPlacement()
    {
        var workArea=Screen.FromPoint(Cursor.Position).WorkingArea;
        var bounds=InitialBounds(workArea,DeviceDpi/96f,settings.WindowWidth,settings.WindowHeight,out var minimum,settings.FontSize/10f);
        MinimumSize=minimum;StartPosition=FormStartPosition.Manual;Bounds=bounds;
        if(settings.WindowMaximized)WindowState=FormWindowState.Maximized;
    }
    // 改了界面字号之后：最小尺寸跟着变，窗口太小就撑大一点。
    void ApplyMinimumSize()
    {
        if(!IsHandleCreated)return;
        var workArea=Screen.FromControl(this).WorkingArea;var factor=settings.FontSize/10f*DeviceDpi/96f;
        MinimumSize=new Size(Math.Min((int)Math.Round(1120*factor),workArea.Width),Math.Min((int)Math.Round(740*factor),workArea.Height));
        if(WindowState==FormWindowState.Normal&&(Width<MinimumSize.Width||Height<MinimumSize.Height))
            Size=new Size(Math.Max(Width,MinimumSize.Width),Math.Max(Height,MinimumSize.Height));
    }
    void SaveWindowPlacement()
    {
        if(WindowState==FormWindowState.Minimized||!IsHandleCreated)return;
        var scale=DeviceDpi/96f;var bounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;
        var width=(int)Math.Round(bounds.Width/scale);var height=(int)Math.Round(bounds.Height/scale);var maximized=WindowState==FormWindowState.Maximized;
        if(settings.WindowWidth==width&&settings.WindowHeight==height&&settings.WindowMaximized==maximized)return;
        settings.WindowWidth=width;settings.WindowHeight=height;settings.WindowMaximized=maximized;
        try{settings.Save();}catch(IOException){}
    }
}
