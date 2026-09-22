namespace YikaiLocal;

// 配置文件编辑器：左侧选择文件，右侧编辑；检查语法、保存并应用（自动备份，失败恢复）。
public sealed partial class MainForm
{
    void ShowConfigEditor(string? initialKey=null)
    {
        if(busy)return;
        var files=runtime.ConfigFiles();
        using var dialog=new Form{Name="configEditor",Text=T("配置文件","Configuration files","設定ファイル"),Size=new Size(1080,760),MinimumSize=new Size(900,600),StartPosition=FormStartPosition.CenterParent,Font=Font,Icon=Icon};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=2,RowCount=1};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,300));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));dialog.Controls.Add(layout);
        var list=new ListBox{Name="configFiles",Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=40,IntegralHeight=false,Margin=new Padding(0,0,16,0)};
        foreach(var file in files)list.Items.Add(file);
        list.DrawItem+=(_,e)=>{
            if(e.Index<0)return;var file=(ConfigFile)list.Items[e.Index];var selected=(e.State&DrawItemState.Selected)!=0;
            using(var back=new SolidBrush(Palette.Surface))e.Graphics.FillRectangle(back,e.Bounds);
            if(selected){var card=Rectangle.Inflate(e.Bounds,-2,-2);card.Width-=1;card.Height-=1;var mode=e.Graphics.SmoothingMode;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var shape=RoundedBox(card,6))using(var bg=new SolidBrush(Palette.Selected))using(var edge=new Pen(Palette.Divider)){e.Graphics.FillPath(bg,shape);e.Graphics.DrawPath(edge,shape);}e.Graphics.SmoothingMode=mode;}
            using var font=selected?new Font(list.Font,FontStyle.Bold):new Font(list.Font,FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics,file.Title,font,new Rectangle(e.Bounds.X+12,e.Bounds.Y,e.Bounds.Width-16,e.Bounds.Height),Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        };
        layout.Controls.Add(list,0,0);
        var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty};
        // 页脚行高要按“按钮 40 + 外边距 8 + 上内边距 14”算，取 68 留余量；58 会裁掉按钮底部
        foreach(var h in new[]{30,52,44})right.RowStyles.Add(new RowStyle(SizeType.Absolute,h));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,68));layout.Controls.Add(right,1,0);
        var pathLabel=L("",9);pathLabel.Name="configPath";pathLabel.ForeColor=Muted;right.Controls.Add(pathLabel,0,0);
        var hint=new Label{Name="configHint",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,Fs(9f)),ForeColor=Muted,AutoEllipsis=true};right.Controls.Add(hint,0,1);
        var findRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,8)};findRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));findRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));findRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));
        var find=new TextBox{Name="configFind",PlaceholderText=T("查找（Ctrl+F，回车查找下一个）","Find (Ctrl+F, Enter for next)","検索（Ctrl+F、Enter で次へ）")};findRow.Controls.Add(new InputFrame(find){Dock=DockStyle.Fill,Margin=new Padding(0,0,8,0)},0,0);
        var next=new ThemeButton{Name="configFindNext",Text=T("下一个","Next","次へ"),Dock=DockStyle.Fill,Margin=new Padding(0,0,8,0)};findRow.Controls.Add(next,1,0);
        var external=new ThemeButton{Name="configOpenFolder",Text=T("打开所在目录","Open folder","フォルダーを開く"),Dock=DockStyle.Fill,Margin=Padding.Empty};findRow.Controls.Add(external,2,0);
        right.Controls.Add(findRow,0,2);
        var editor=new TextBox{Name="configText",Dock=DockStyle.Fill,Multiline=true,AcceptsTab=true,AcceptsReturn=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=CreateCodeFont(),MaxLength=1_048_576,BorderStyle=BorderStyle.FixedSingle};right.Controls.Add(editor,0,3);
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,14,0,0)};right.Controls.Add(bottom,0,4);
        var save=new ThemeButton{Name="configSave",Text=T("保存并应用","Save and apply","保存して適用"),Size=new Size(190,40),Margin=Padding.Empty,BackColor=Palette.Accent};
        var check=new ThemeButton{Name="configCheck",Text=T("检查语法","Check syntax","構文を検査"),Size=new Size(150,40),Margin=new Padding(0,0,10,0)};
        var reload=new ThemeButton{Name="configReload",Text=T("放弃修改","Discard changes","変更を破棄"),Size=new Size(150,40),Margin=new Padding(0,0,10,0)};
        var close=new ThemeButton{Text=T("关闭","Close","閉じる"),Size=new Size(120,40),Margin=new Padding(0,0,10,0),DialogResult=DialogResult.Cancel};
        bottom.Controls.AddRange([save,check,reload,close]);dialog.CancelButton=close;

        ConfigFile? current=null;string loaded="";bool working=false;
        bool Dirty()=>current!=null&&editor.Text.ReplaceLineEndings("\n")!=loaded.ReplaceLineEndings("\n");
        string Hint(ConfigFile file)=>file.Kind switch{
            "php"=>T($"PHP {file.Version} 的主配置，使用该版本的项目共用。保存后检查语法，并重启正在运行的 PHP {file.Version}。",$"Main configuration for PHP {file.Version}, shared by projects using it. Saving checks syntax and restarts running PHP {file.Version}.",$"PHP {file.Version} の設定。保存時に構文を検査し、起動中の PHP {file.Version} を再起動します。"),
            "dbpage"=>T("数据库管理页面使用的 PHP 配置。保存后检查语法，并重启数据库页面。","PHP settings for the DB manager. Saving checks syntax and restarts it.","DB 管理用の PHP 設定。保存時に構文を検査し再起動します。"),
            "nginx"=>T("Nginx 主配置由面板生成，这里的内容会载入 http 块，对所有项目生效。保存前检查语法，运行中自动重载。","The Nginx config is generated; this file is included in the http block for all projects. Checked before saving and reloaded if running.","Nginx 設定は自動生成されます。この内容は http ブロックに読み込まれます。保存前に検査し、起動中なら再読込します。"),
            "apache"=>T("Apache 主配置由面板生成，这里的内容全局载入，同名指令覆盖默认值。保存前检查语法，运行中自动重启。","The Apache config is generated; this file is included globally and overrides defaults. Checked before saving and restarted if running.","Apache 設定は自動生成されます。この内容は全体に読み込まれます。保存前に検査し、起動中なら再起動します。"),
            _=>T($"追加在面板生成的 MySQL {(file.Version=="mysql57"?"5.7":"8.0")} 配置之后，同名参数以这里为准。保存前检查参数，运行中会重启；启动失败自动恢复原配置。",$"Appended after the generated MySQL config; options here win. Checked before saving; running MySQL restarts and rolls back if it fails.","生成された MySQL 設定の後に追加されます。保存前に検査し、起動中なら再起動、失敗時は元に戻します。")};
        void Load(ConfigFile file)
        {
            current=file;try{loaded=runtime.ReadConfig(file);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){loaded="";MessageBox.Show(dialog,e.Message,dialog.Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
            editor.Text=loaded.ReplaceLineEndings(Environment.NewLine);editor.SelectionStart=0;editor.ScrollToCaret();
            pathLabel.Text=file.FilePath;hint.Text=Hint(file);serviceTips.SetToolTip(pathLabel,file.FilePath);
        }
        var switching=false;
        list.SelectedIndexChanged+=(_,_)=>{
            if(switching||list.SelectedItem is not ConfigFile file||file==current)return;
            if(Dirty()&&MessageBox.Show(dialog,T("放弃当前未保存的修改？","Discard unsaved changes?","未保存の変更を破棄しますか？"),dialog.Text,MessageBoxButtons.OKCancel)!=DialogResult.OK){switching=true;list.SelectedItem=current;switching=false;return;}
            Load(file);list.Invalidate();
        };
        void FindNext()
        {
            if(find.Text.Length==0)return;
            var start=editor.SelectionStart+editor.SelectionLength;var index=editor.Text.IndexOf(find.Text,Math.Min(start,editor.Text.Length),StringComparison.OrdinalIgnoreCase);
            if(index<0)index=editor.Text.IndexOf(find.Text,StringComparison.OrdinalIgnoreCase);
            if(index<0){System.Media.SystemSounds.Beep.Play();return;}
            editor.Select(index,find.Text.Length);editor.ScrollToCaret();editor.Focus();
        }
        next.Click+=(_,_)=>FindNext();
        find.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;FindNext();}};
        dialog.KeyPreview=true;dialog.KeyDown+=(_,e)=>{if(e.Control&&e.KeyCode==Keys.F){e.SuppressKeyPress=true;find.Focus();find.SelectAll();}};
        external.Click+=(_,_)=>{if(current!=null)Open(Path.GetDirectoryName(current.FilePath)!);};
        reload.Click+=(_,_)=>{if(current!=null&&(!Dirty()||MessageBox.Show(dialog,T("放弃修改并重新载入文件？","Discard edits and reload the file?","変更を破棄して再読込しますか？"),dialog.Text,MessageBoxButtons.OKCancel)==DialogResult.OK))Load(current);};
        async Task Apply(bool saving)
        {
            if(working||current is not {} file)return;
            // 文件在面板外被改过时提醒，避免覆盖别处的修改。
            string onDisk;try{onDisk=runtime.ReadConfig(file);}catch(IOException){onDisk=loaded;}
            if(saving&&onDisk.ReplaceLineEndings("\n")!=loaded.ReplaceLineEndings("\n")&&MessageBox.Show(dialog,T("文件在打开后被其他程序修改过，仍要用当前内容覆盖？","The file changed on disk since it was opened. Overwrite it?","開いた後にファイルが変更されました。上書きしますか？"),dialog.Text,MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
            working=true;busy=true;list.Enabled=editor.Enabled=bottom.Enabled=findRow.Enabled=false;var text=editor.Text.ReplaceLineEndings("\n");
            dialog.UseWaitCursor=true;
            try
            {
                string message;
                using(var operation=Runtime.Lock(settings)){runtime.Adopt();if(saving)message=await runtime.SaveConfigAsync(file,text);else{await runtime.CheckConfigAsync(file,text);message=T("语法检查通过，尚未保存。","Syntax is valid. Not saved yet.","構文は正常です。まだ保存していません。");}}
                if(saving){loaded=text;}
                MessageBox.Show(dialog,message+(saving?"\n"+T("原文件已备份到 backups\\config。","The previous file was backed up to backups\\config.","元のファイルは backups\\config に保存しました。"):""),dialog.Text,MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
            catch(Exception error){MessageBox.Show(dialog,(saving?T("未保存，已保留原配置：\n","Not saved; the previous configuration is kept:\n","保存していません。元の設定を維持しました：\n"):T("检查未通过：\n","Check failed:\n","検査に失敗しました：\n"))+error.Message,dialog.Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
            finally{dialog.UseWaitCursor=false;working=false;busy=false;list.Enabled=editor.Enabled=bottom.Enabled=findRow.Enabled=true;RefreshState();}
        }
        check.Click+=async(_,_)=>await Apply(false);save.Click+=async(_,_)=>await Apply(true);
        dialog.FormClosing+=(_,e)=>{if(working){e.Cancel=true;return;}if(Dirty()&&MessageBox.Show(dialog,T("放弃未保存的修改？","Discard unsaved changes?","未保存の変更を破棄しますか？"),dialog.Text,MessageBoxButtons.OKCancel)!=DialogResult.OK)e.Cancel=true;};
        list.SelectedItem=files.FirstOrDefault(f=>f.Key==initialKey)??files[0];
        dialog.Shown+=(_,_)=>editor.Focus();
        Palette.Show(dialog,this);
    }
}
