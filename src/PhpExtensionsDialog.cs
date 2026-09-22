namespace YikaiLocal;

// PHP 扩展勾选：
// - 版本模式（配置 → PHP 扩展）：改的是该 PHP 版本的 php.ini，使用这个版本的项目共用。
// - 项目模式（项目设置或右键菜单）：只改这一个项目，保存为相对版本设置的增减，写进项目自己的 php.ini。
public sealed partial class MainForm
{
    sealed record ExtensionItem(PhpExtensionInfo Info,string Label){public override string ToString()=>Label;}

    void ShowPhpExtensions(string? initialVersion=null,Site? site=null)
    {
        if(busy)return;
        var versions=runtime.InstalledPhp().ToList();
        if(versions.Count==0){MessageBox.Show(this,T("没有已安装的 PHP 版本。","No PHP version is installed.","インストール済みの PHP がありません。"),Text,MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
        var title=site==null?T("PHP 扩展","PHP extensions","PHP 拡張"):T($"PHP 扩展 · {site}",$"PHP extensions · {site}",$"PHP 拡張 · {site}");
        using var dialog=new Form{Name="phpExtensions",Text=title,ClientSize=new Size(740,680),Font=Font,Icon=Icon,AutoScaleMode=AutoScaleMode.Dpi,StartPosition=FormStartPosition.CenterParent,MinimumSize=new Size(660,560)};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,18,24,18),ColumnCount=1,RowCount=5};
        foreach(var h in new[]{48,64})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(var h in new[]{48,56})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        dialog.Controls.Add(layout);

        var version=new Choice{Name="extensionPhp",Dock=DockStyle.Top,Height=36,Margin=new Padding(0,0,0,12)};
        if(site==null)foreach(var v in versions)version.Items.Add("PHP "+v);
        else version.Items.Add(T($"PHP {site.Php} · 只影响项目 {site}",$"PHP {site.Php} · only for {site}",$"PHP {site.Php} · {site} のみ"));
        version.SelectedIndex=site==null?Math.Max(0,versions.IndexOf(initialVersion??settings.PhpDefault)):0;
        version.Enabled=site==null;
        layout.Controls.Add(version,0,0);
        var hint=L(site==null
            ?T("勾选要启用的扩展。这里改的是该 PHP 版本的 php.ini，使用这个版本的项目都会生效；保存时检查并重启该版本 PHP，扩展加载失败会被拒绝并恢复原配置。","These are the PHP version's own settings: every project on this version is affected. Saving checks php.ini and restarts that PHP; an extension that fails to load is rejected and the previous file restored.","この PHP バージョンの設定です（同じバージョンのプロジェクトすべてに影響）。保存時に検査して再起動し、読み込めない拡張は元に戻します。")
            :T("只影响这个项目：在该 PHP 版本设置的基础上增减，保存到项目自己的 php.ini，并只重启这个项目的 PHP。","Only this project: ticks are stored as changes on top of the PHP version settings, written to the project's own php.ini, and restart this project only.","このプロジェクトのみ：バージョン設定との差分として保存し、このプロジェクトだけ再起動します。"),9);
        hint.ForeColor=Muted;hint.TextAlign=ContentAlignment.TopLeft;hint.AutoEllipsis=false;layout.Controls.Add(hint,0,1);
        var list=new CheckedListBox{Name="extensionList",Dock=DockStyle.Fill,CheckOnClick=true,IntegralHeight=false,BorderStyle=BorderStyle.FixedSingle,Font=new Font(Font.FontFamily,Fs(10f)),Margin=new Padding(0,0,0,10)};
        layout.Controls.Add(list,0,2);
        var status=L("",9);status.Name="extensionStatus";status.ForeColor=Muted;status.TextAlign=ContentAlignment.TopLeft;status.AutoEllipsis=false;layout.Controls.Add(status,0,3);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,10,0,0)};layout.Controls.Add(footer,0,4);
        var save=new ThemeButton{Name="saveExtensions",Text=T("保存并应用","Save and apply","保存して適用"),Size=new Size(180,40),Margin=Padding.Empty,BackColor=Palette.Accent};
        var reset=new ThemeButton{Name="resetExtensions",Text=T("放弃修改","Discard changes","変更を破棄"),Size=new Size(150,40),Margin=new Padding(0,0,10,0)};
        var follow=new ThemeButton{Name="followVersion",Text=T("跟随版本设置","Follow the version","バージョン設定に合わせる"),Size=new Size(190,40),Margin=new Padding(0,0,10,0),Visible=site!=null};
        var close=new ThemeButton{Text=T("关闭","Close","閉じる"),Size=new Size(110,40),Margin=new Padding(0,0,10,0),DialogResult=DialogResult.Cancel};
        footer.Controls.AddRange([save,reset,follow,close]);dialog.CancelButton=close;

        var items=new List<ExtensionItem>();bool loading=false,working=false;
        string Current()=>site?.Php??versions[version.SelectedIndex];
        string Summary()
        {
            var on=Enumerable.Range(0,list.Items.Count).Count(list.GetItemChecked);
            var text=T($"已勾选 {on} 个，共 {list.Items.Count} 个可用扩展。",$"{on} of {list.Items.Count} extensions ticked.",$"{list.Items.Count} 件中 {on} 件を選択。");
            if(site==null)return text;
            var changed=items.Count(i=>i.Info.Custom);
            return text+(changed>0
                ?T($" 其中 {changed} 项与 PHP {site.Php} 的设置不同。",$" {changed} differ from the PHP {site.Php} settings.",$" うち {changed} 件はバージョン設定と異なります。")
                :T(" 当前与版本设置一致。"," Same as the version settings."," バージョン設定と同じです。"));
        }
        void Load()
        {
            loading=true;list.Items.Clear();items.Clear();
            foreach(var info in runtime.PhpExtensionList(Current(),site))
            {
                var notes=new List<string>();
                if(info.Zend)notes.Add("zend_extension");
                if(info.Required)notes.Add(T("必需","required","必須"));
                if(info.Custom)notes.Add(T("本项目单独设置","project setting","プロジェクト設定"));
                items.Add(new(info,info.Name+(notes.Count>0?"　·　"+string.Join(" · ",notes):"")));
                list.Items.Add(items[^1],info.Enabled);
            }
            loading=false;status.Text=Summary();
        }
        list.ItemCheck+=(_,e)=>{
            if(loading)return;
            if(working){e.NewValue=e.CurrentValue;return;}
            if(items[e.Index].Info.Required&&e.NewValue==CheckState.Unchecked)
            {
                e.NewValue=CheckState.Checked;
                status.Text=T($"{items[e.Index].Info.Name} 是面板和 YikaiCMS 需要的扩展，不能关闭。",$"{items[e.Index].Info.Name} is required by the panel and YikaiCMS.",$"{items[e.Index].Info.Name} はパネルと YikaiCMS に必要です。");
                return;
            }
            BeginInvoke(()=>status.Text=Summary());
        };
        version.SelectedIndexChanged+=(_,_)=>Load();
        reset.Click+=(_,_)=>{Load();status.Text=site==null?T("已恢复为当前 php.ini 的设置。","Reloaded from the current php.ini.","現在の php.ini に戻しました。"):T("已恢复为当前保存的设置。","Reloaded the saved settings.","保存済みの設定に戻しました。");};
        async Task Persist(Func<Task<string>> action)
        {
            if(working||busy)return;
            working=true;save.Enabled=reset.Enabled=follow.Enabled=version.Enabled=false;
            status.Text=T("正在检查并应用，请稍候…","Checking and applying…","検査して適用しています…");
            string? message=null;
            try{await Work(async()=>{message=await action();});}
            finally
            {
                working=false;
                if(!dialog.IsDisposed)
                {
                    save.Enabled=reset.Enabled=follow.Enabled=true;version.Enabled=site==null;Load();
                    status.Text=message??T("未应用，配置保持原样。","Not applied; the configuration is unchanged.","適用していません。設定は元のままです。");
                }
            }
        }
        save.Click+=async(_,_)=>{
            var wanted=items.Where((_,index)=>list.GetItemChecked(index)).Select(item=>item.Info.Name).ToList();
            if(site!=null)
            {
                if(items.Count(i=>i.Info.Enabled)==wanted.Count&&items.Where(i=>i.Info.Enabled).All(i=>wanted.Contains(i.Info.Name))){status.Text=T("没有需要保存的改动。","Nothing to save.","変更はありません。");return;}
                await Persist(()=>runtime.SaveSiteExtensionsAsync(site,wanted));
                return;
            }
            var file=runtime.ConfigFileByKey("php"+Current());
            string current;
            try{current=runtime.ReadConfig(file);}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException){status.Text=error.Message;return;}
            var changes=items.Select((item,index)=>(item.Info.Name,item.Info.Zend,list.GetItemChecked(index))).ToList();
            var text=Runtime.ApplyPhpExtensions(current,changes);
            if(text.ReplaceLineEndings("\n")==current.ReplaceLineEndings("\n")){status.Text=T("没有需要保存的改动。","Nothing to save.","変更はありません。");return;}
            await Persist(()=>runtime.SaveConfigAsync(file,text));
        };
        follow.Click+=async(_,_)=>{
            if(site==null)return;
            var defaults=runtime.PhpExtensionList(site.Php).Where(e=>e.Enabled).Select(e=>e.Name).ToList();
            await Persist(()=>runtime.SaveSiteExtensionsAsync(site,defaults));
        };
        Load();
        dialog.FormClosing+=(_,e)=>{if(working)e.Cancel=true;};
        Palette.Show(dialog,this);
    }
}
