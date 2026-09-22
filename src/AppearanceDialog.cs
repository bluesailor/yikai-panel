namespace YikaiLocal;

// 外观：界面主题、界面字号，以及编辑器（配置文件、伪静态规则）用的编程字体和字号。
// 改动立即生效并保存，这个窗口本身也跟着换配色和字号。
public sealed partial class MainForm
{
    static readonly float[] FontSizes=[10f,11f,12f];
    static readonly float[] CodeFontSizes=[9f,10f,11f,12f,14f];
    // 常见的编程等宽字体，按顺序取本机已安装的。
    static readonly string[] CodeFontCandidates=["Consolas","Cascadia Mono","Cascadia Code","JetBrains Mono","Fira Code","Source Code Pro","IBM Plex Mono","DejaVu Sans Mono","Courier New","Lucida Console"];
    static List<string> AvailableCodeFonts(string current)
    {
        using var installed=new System.Drawing.Text.InstalledFontCollection();
        var names=installed.Families.Select(f=>f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list=CodeFontCandidates.Where(names.Contains).ToList();
        if(!list.Contains(current,StringComparer.OrdinalIgnoreCase)&&names.Contains(current))list.Insert(0,current);
        return list.Count>0?list:["Consolas"];
    }
    // 配置文件编辑器、伪静态编辑器和地址列表共用的等宽字体。
    Font CreateCodeFont()=>new(settings.CodeFont,settings.CodeFontSize);

    void ShowAppearance()
    {
        if(busy)return;
        using var dialog=new Form{Name="appearance",Text=T("外观","Appearance","外観"),ClientSize=new Size(760,680),Font=Font,Icon=Icon,AutoScaleMode=AutoScaleMode.Dpi,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,18,24,18),ColumnCount=1,RowCount=8};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        // 固定行高（按 100% 书写，打开时和文字一起按显示缩放、界面字号放大），不用自动撑开，避免放大后行宽失控。
        foreach(var _ in new[]{1,2,3,4})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,90));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));
        dialog.Controls.Add(layout);

        // 主题或字号变化后要重新上色、重新设字号的控件。
        var cards=new List<TableLayoutPanel>();var strong=new List<(Label Label,float Size,bool Bold)>();var muted=new List<(Label Label,float Size)>();
        Label Strong(string text,float size,bool bold){var label=L(text,size,bold);strong.Add((label,size,bold));return label;}
        Label Faint(string text,float size){var label=L(text,size);label.ForeColor=Palette.Secondary;muted.Add((label,size));return label;}

        layout.Controls.Add(Strong(T("界面设置","Interface","インターフェース"),11,true),0,0);
        // 每一行：左边名称和说明（自动撑开，不会截断），右边选择框。
        Choice Row(int index,string title,string description,string[] options,int selected)
        {
            var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=new Padding(0,0,0,12),BackColor=Palette.Card,Padding=new Padding(16,10,16,10)};
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210));
            row.RowStyles.Add(new RowStyle(SizeType.Percent,50));row.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            row.Paint+=(_,e)=>{using var edge=new Pen(Palette.Divider);e.Graphics.DrawRectangle(edge,0,0,row.Width-1,row.Height-1);};
            cards.Add(row);
            var name=Strong(title,10.5f,false);name.Margin=Padding.Empty;row.Controls.Add(name,0,0);
            var note=Faint(description,9);note.Margin=Padding.Empty;row.Controls.Add(note,0,1);
            var choice=new Choice{Dock=DockStyle.Fill,Margin=new Padding(12,8,0,8)};
            foreach(var option in options)choice.Items.Add(option);
            choice.SelectedIndex=selected;row.Controls.Add(choice,1,0);row.SetRowSpan(choice,2);
            layout.Controls.Add(row,0,index);return choice;
        }
        var themes=new[]{"system","light","dark"};
        var theme=Row(1,T("界面主题","Theme","テーマ"),T("浅色、深色，或跟随 Windows 的应用模式。","Light, dark, or follow the Windows app mode.","ライト・ダーク・Windows に合わせる。"),
            [T("跟随系统","Follow system","システムに合わせる"),T("浅色","Light","ライト"),T("深色","Dark","ダーク")],Math.Max(0,Array.IndexOf(themes,settings.Theme)));
        theme.Name="appearanceTheme";
        var size=Row(2,T("界面字号","Text size","文字サイズ"),T("调整界面文字大小，窗口内的间距会一起变化。","Changes the interface text size; spacing follows along.","文字サイズを変更します（間隔も合わせて変わります）。"),
            [T("标准","Standard","標準"),T("大","Large","大"),T("特大","Extra large","特大")],Math.Max(0,Array.IndexOf(FontSizes,settings.FontSize)));
        size.Name="appearanceFontSize";
        var fonts=AvailableCodeFonts(settings.CodeFont);
        var codeFont=Row(3,T("编程字体","Code font","コードフォント"),T("配置文件、伪静态规则等编辑框使用的等宽字体。","Monospaced font for the configuration and rewrite editors.","設定ファイルなどの編集欄で使う等幅フォント。"),
            [..fonts],Math.Max(0,fonts.FindIndex(f=>f.Equals(settings.CodeFont,StringComparison.OrdinalIgnoreCase))));
        codeFont.Name="appearanceCodeFont";
        var codeSize=Row(4,T("编程字号","Code text size","コード文字サイズ"),T("只影响编辑框里的代码，不影响界面文字。","Applies to the editors only, not to the interface.","編集欄のみに適用されます。"),
            [..CodeFontSizes.Select(s=>$"{s:0} pt")],Math.Max(0,Array.IndexOf(CodeFontSizes,settings.CodeFontSize)));
        codeSize.Name="appearanceCodeSize";

        layout.Controls.Add(Strong(T("编程字体预览","Code preview","コードプレビュー"),10.5f,true),0,5);
        var preview=new TextBox{Name="appearancePreview",Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,WordWrap=false,ScrollBars=ScrollBars.None,BorderStyle=BorderStyle.FixedSingle,Margin=new Padding(0,6,0,6),
            Text=string.Join(Environment.NewLine,["location ~ \\.php$ {","    fastcgi_pass 127.0.0.1:9082;   # 0O1lI|","    include \"D:/yikai/config/fastcgi_params\";","}"])};
        layout.Controls.Add(preview,0,6);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,10,0,0)};layout.Controls.Add(footer,0,7);
        var close=new ThemeButton{Text=T("关闭","Close","閉じる"),Size=new Size(130,40),Margin=Padding.Empty,DialogResult=DialogResult.Cancel};footer.Controls.Add(close);dialog.CancelButton=close;

        // 换主题 / 换字号后，这个窗口里带固定颜色和固定字号的控件要一起更新。
        void Restyle()
        {
            foreach(var card in cards)card.BackColor=Palette.Card;
            foreach(var (label,_,_) in strong)label.ForeColor=Palette.Text;
            foreach(var (label,_) in muted)label.ForeColor=Palette.Secondary;
            ReplaceFont(preview,CreateCodeFont());
            Palette.Apply(dialog);dialog.Invalidate(true);
        }
        bool applying=false,pending=false;
        void Apply()
        {
            if(applying){pending=true;return;}
            applying=true;
            settings.Theme=themes[Math.Max(0,theme.SelectedIndex)];
            settings.FontSize=FontSizes[Math.Clamp(size.SelectedIndex,0,FontSizes.Length-1)];
            settings.CodeFont=fonts[Math.Clamp(codeFont.SelectedIndex,0,fonts.Count-1)];
            settings.CodeFontSize=CodeFontSizes[Math.Clamp(codeSize.SelectedIndex,0,CodeFontSizes.Length-1)];
            settings.Save();
            Palette.Use(settings.Theme);Palette.LayoutScale=settings.FontSize/10f;
            // 主窗口字体被菜单、已打开的窗口共用，不能手动释放；字号没变时也不用换（只改主题或编程字体的情况）。
            if(Math.Abs(Font.Size-settings.FontSize)>0.01f)Font=new Font("Microsoft YaHei UI",settings.FontSize);
            BeginInvoke(()=>{
                Build();UpdateTray();ApplyMinimumSize();
                if(dialog.IsDisposed){applying=false;return;}
                Restyle();
                applying=false;
                if(pending){pending=false;Apply();}
            });
        }
        foreach(var choice in new[]{theme,size,codeFont,codeSize})choice.SelectedIndexChanged+=(_,_)=>Apply();
        preview.Font=CreateCodeFont();
        Palette.Show(dialog,this);
    }
}
