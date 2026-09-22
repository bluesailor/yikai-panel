using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace YikaiLocal;

internal static class Palette
{
    static Color Hex(int rgb)=>Color.FromArgb((rgb>>16)&255,(rgb>>8)&255,rgb&255);
    // 一套配色的全部颜色；浅色和深色各一份，界面里所有颜色都从这里取。
    internal sealed record Theme(bool Dark,Color Canvas,Color Surface,Color Raised,Color Selection,Color Selected,Color Border,Color Divider,
        Color Text,Color Secondary,Color Link,Color Brand,Color Accent,Color OnAccent,Color AccentHover,Color AccentPressed,
        Color Success,Color Warning,Color Stopped,Color Input,Color Workspace,Color Card,Color ButtonFill,Color ButtonHover,Color ButtonPressed,Color ButtonDisabled);

    // 浅色：奶白主区、暖灰侧栏、白色卡片 + 细浅边框、近黑主按钮、赤陶色强调（对比度见 docs/development.md）。
    static readonly Theme LightTheme=new(false,Hex(0xFAF9F6),Hex(0xF4F2EE),Hex(0xEDEAE4),Hex(0xECE8E1),Hex(0xFFFFFF),Hex(0xDDD8CF),Hex(0xE8E4DD),
        Hex(0x1F1E1C),Hex(0x6B665F),Hex(0xA04830),Hex(0xA04830),Hex(0x2B2A27),Hex(0xFAF9F6),Hex(0x3B3935),Hex(0x1A1917),
        Hex(0x2F6B4A),Hex(0x85581A),Hex(0xB7B1A8),Hex(0xFFFFFF),Hex(0xFAF9F6),Hex(0xFFFFFF),Hex(0xF7F5F1),Hex(0xEDE9E3),Hex(0xE4DFD7),Hex(0xF7F5F1));
    // 深色：暖调深灰主区、稍亮卡片、赤陶色主按钮与链接；正文 12.9:1、辅助文字 6.4:1。
    static readonly Theme DarkTheme=new(true,Hex(0x1B1A18),Hex(0x232120),Hex(0x2C2A27),Hex(0x33302B),Hex(0x302D29),Hex(0x3B3833),Hex(0x302E2A),
        Hex(0xECE7E0),Hex(0xA8A199),Hex(0xE08A5F),Hex(0xE08A5F),Hex(0xB0552F),Hex(0xFFF6F1),Hex(0xC2603A),Hex(0x97441F),
        Hex(0x6FBF8F),Hex(0xE0AE55),Hex(0x6E6862),Hex(0x1F1E1C),Hex(0x1B1A18),Hex(0x232120),Hex(0x2C2A27),Hex(0x363330),Hex(0x403C38),Hex(0x26241F));

    static Theme current=LightTheme;
    public static bool IsDark=>current.Dark;
    // 主题：light / dark / system（跟随 Windows 的“应用模式”）。
    public static void Use(string mode)=>current=mode switch{"dark"=>DarkTheme,"light"=>LightTheme,_=>SystemDark()?DarkTheme:LightTheme};
    public static bool SystemDark()
    {
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value&&value==0;
        }
        catch(Exception e) when(e is System.Security.SecurityException or UnauthorizedAccessException or IOException){return false;}
    }

    public static Color Canvas=>current.Canvas;
    public static Color Surface=>current.Surface;
    public static Color Raised=>current.Raised;
    public static Color Selection=>current.Selection;
    public static Color Selected=>current.Selected;
    public static Color Border=>current.Border;
    public static Color Divider=>current.Divider;
    public static Color Text=>current.Text;
    public static Color Secondary=>current.Secondary;
    public static Color Link=>current.Link;
    public static Color Brand=>current.Brand;
    public static Color Accent=>current.Accent;
    public static Color OnAccent=>current.OnAccent;
    public static Color AccentHover=>current.AccentHover;
    public static Color AccentPressed=>current.AccentPressed;
    public static Color Success=>current.Success;
    public static Color Warning=>current.Warning;
    public static Color Stopped=>current.Stopped;
    public static Color Input=>current.Input;
    public static Color Workspace=>current.Workspace;
    public static Color Card=>current.Card;
    public static Color ButtonFill=>current.ButtonFill;
    public static Color ButtonHover=>current.ButtonHover;
    public static Color ButtonPressed=>current.ButtonPressed;
    public static Color ButtonDisabled=>current.ButtonDisabled;
    // 项目头像底色：白字对比度均 ≥ 4.98:1（青、石墨、紫、赤陶、绿、蓝、赭、玫红、靛、橄榄），深浅主题通用。
    public static readonly Color[] Avatars=[Hex(0x1E7A8C),Hex(0x5A5A58),Hex(0x6A4CC0),Hex(0xA04830),Hex(0x2F6B4A),Hex(0x2F62B0),Hex(0x8A5A14),Hex(0xA63C6B),Hex(0x44508F),Hex(0x5E6B2A)];

    // 圆角矩形路径：卡片、徽标和列表选中项共用。
    public static GraphicsPath Rounded(RectangleF box,float radius)
    {
        var path=new GraphicsPath();var d=Math.Min(radius*2,Math.Min(box.Width,box.Height));
        path.AddArc(box.X,box.Y,d,d,180,90);path.AddArc(box.Right-d,box.Y,d,d,270,90);path.AddArc(box.Right-d,box.Bottom-d,d,d,0,90);path.AddArc(box.X,box.Bottom-d,d,d,90,90);
        path.CloseFigure();return path;
    }
    // 在背景色上按比例混合出淡色，用于徽标底色和边框。
    public static Color Blend(Color color,Color background,float amount)=>Color.FromArgb(
        (int)Math.Round(background.R+(color.R-background.R)*amount),
        (int)Math.Round(background.G+(color.G-background.G)*amount),
        (int)Math.Round(background.B+(color.B-background.B)*amount));
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    // 界面字号带来的额外放大倍数（标准字号为 1），由主窗口在启动和改外观时设置。
    public static float LayoutScale {get;set;}=1f;
    // 所有对话框的尺寸按“标准字号、100% 显示缩放”书写，打开前统一按实际缩放放大一次：
    // 窗体没有开启 WinForms 自动缩放，不放大的话文字（按磅值）会比格子大，被截断或挤出窗口。
    public static DialogResult Show(Form form,Form owner){ScaleDialog(form,owner);Apply(form);return form.ShowDialog(owner);}
    static void ScaleDialog(Form form,Control owner)
    {
        var factor=owner.DeviceDpi/96f*LayoutScale;
        if(Math.Abs(factor-1f)<0.01f)return;
        form.Scale(new SizeF(factor,factor));
        foreach(var control in Descendants(form))
        {
            if(control is ListBox list&&list.DrawMode!=DrawMode.Normal)list.ItemHeight=Math.Min(255,(int)Math.Round(list.ItemHeight*factor));
            if(control is DataGridView grid){grid.RowTemplate.Height=(int)Math.Round(grid.RowTemplate.Height*factor);grid.ColumnHeadersHeight=(int)Math.Round(grid.ColumnHeadersHeight*factor);}
        }
        // 放大后不能超出屏幕可用区域（1080p 屏开 150% 时尤其要注意）。
        var work=Screen.FromControl(owner).WorkingArea;
        if(form.Width>work.Width||form.Height>work.Height)form.Size=new Size(Math.Min(form.Width,work.Width),Math.Min(form.Height,work.Height));
    }
    static IEnumerable<Control> Descendants(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    public static void Apply(Control root)
    {
        if(root is Form form){form.BackColor=Canvas;form.ForeColor=Text;if(!form.IsHandleCreated)form.HandleCreated+=(_,_)=>Title(form);else Title(form);}
        foreach(Control c in root.Controls)
        {
            if(c.ForeColor==SystemColors.ControlText||c.ForeColor==SystemColors.WindowText)c.ForeColor=Text;
            if(c.BackColor==SystemColors.Control)c.BackColor=root.BackColor;
            switch(c)
            {
                case Button b:
                    b.UseVisualStyleBackColor=false;b.FlatStyle=FlatStyle.Flat;
                    if(b.BackColor!=Accent)b.BackColor=b.FlatAppearance.BorderSize==0?Surface:ButtonFill;b.ForeColor=b.BackColor==Accent?OnAccent:Text;
                    b.FlatAppearance.BorderColor=b.BackColor==Accent?Accent:Border;b.FlatAppearance.MouseOverBackColor=b.BackColor==Accent?AccentHover:b.FlatAppearance.BorderSize==0?Selection:ButtonHover;b.FlatAppearance.MouseDownBackColor=b.BackColor==Accent?AccentPressed:b.FlatAppearance.BorderSize==0?Selection:ButtonPressed;break;
                case InputFrame frame:frame.BackColor=Input;break;
                case TextBox t:t.BackColor=t.Parent is InputFrame?Input:Input;t.ForeColor=Text;t.BorderStyle=t.Parent is InputFrame?BorderStyle.None:BorderStyle.FixedSingle;break;
                case Choice choice:choice.BackColor=Input;choice.ForeColor=Text;break;
                case CheckedListBox checkedList:checkedList.BackColor=Input;checkedList.ForeColor=Text;break;
                case ComboBox cb:cb.BackColor=Raised;cb.ForeColor=Text;cb.FlatStyle=FlatStyle.Flat;break;
                case NumericUpDown n:n.BackColor=Input;n.ForeColor=Text;break;
                case ListBox l:l.BackColor=Surface;l.ForeColor=Text;break;
                case LinkLabel link:link.LinkColor=Link;link.ActiveLinkColor=Text;link.VisitedLinkColor=Link;break;
                case DataGridView grid:
                    grid.EnableHeadersVisualStyles=false;grid.BackgroundColor=Surface;grid.GridColor=Border;grid.BorderStyle=BorderStyle.None;
                    grid.DefaultCellStyle.BackColor=Surface;grid.DefaultCellStyle.ForeColor=Text;grid.DefaultCellStyle.SelectionBackColor=Selection;grid.DefaultCellStyle.SelectionForeColor=Text;
                    grid.ColumnHeadersDefaultCellStyle.BackColor=Raised;grid.ColumnHeadersDefaultCellStyle.ForeColor=Secondary;grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Raised;
                    grid.EditingControlShowing+=(_,e)=>{e.Control.BackColor=Input;e.Control.ForeColor=Text;};break;
            }
            Apply(c);
        }
        ToolStripManager.Renderer=new MenuRenderer();
    }
    // 标题栏跟随主题：深色时使用系统的深色标题栏。
    static void Title(Form form){try{int dark=IsDark?1:0,background=ColorTranslator.ToWin32(Canvas),foreground=ColorTranslator.ToWin32(Text);DwmSetWindowAttribute(form.Handle,20,ref dark,4);DwmSetWindowAttribute(form.Handle,35,ref background,4);DwmSetWindowAttribute(form.Handle,36,ref foreground,4);}catch(DllNotFoundException){}}
    // ToolStripProfessionalRenderer 默认用系统高亮色画选中项、用系统高亮文字色画文字，深色下会变成蓝底黑字。
    sealed class MenuRenderer:ToolStripProfessionalRenderer
    {
        public MenuRenderer():base(new MenuColors()){RoundedEdges=false;}
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if(!e.Item.Selected||!e.Item.Enabled){base.OnRenderMenuItemBackground(e);return;}
            using var fill=new SolidBrush(Selection);
            using var edge=new Pen(Border);
            var box=new Rectangle(2,0,Math.Max(1,e.Item.Width-5),Math.Max(1,e.Item.Height-1));
            e.Graphics.FillRectangle(fill,box);e.Graphics.DrawRectangle(edge,box);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor=e.Item.Enabled?Text:Secondary;
            base.OnRenderItemText(e);
        }
    }
    sealed class MenuColors:ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground=>Surface;
        public override Color ImageMarginGradientBegin=>Surface;
        public override Color ImageMarginGradientMiddle=>Surface;
        public override Color ImageMarginGradientEnd=>Surface;
        public override Color MenuItemSelected=>Selection;
        public override Color MenuItemBorder=>Border;
        public override Color MenuBorder=>Border;
        public override Color CheckBackground=>Selection;
        public override Color CheckSelectedBackground=>Selection;
        public override Color SeparatorDark=>Border;
        public override Color SeparatorLight=>Border;
        public override Color ButtonSelectedHighlight=>Selection;
        public override Color ButtonSelectedHighlightBorder=>Border;
        public override Color ButtonSelectedGradientBegin=>Selection;
        public override Color ButtonSelectedGradientMiddle=>Selection;
        public override Color ButtonSelectedGradientEnd=>Selection;
        public override Color ButtonSelectedBorder=>Border;
        public override Color ButtonPressedHighlight=>Raised;
        public override Color ButtonPressedGradientBegin=>Raised;
        public override Color ButtonPressedGradientMiddle=>Raised;
        public override Color ButtonPressedGradientEnd=>Raised;
        public override Color MenuItemSelectedGradientBegin=>Selection;
        public override Color MenuItemSelectedGradientEnd=>Selection;
        public override Color MenuItemPressedGradientBegin=>Raised;
        public override Color MenuItemPressedGradientMiddle=>Raised;
        public override Color MenuItemPressedGradientEnd=>Raised;
    }
}
