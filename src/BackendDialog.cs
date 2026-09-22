namespace YikaiLocal;

public sealed partial class MainForm
{
    Task OpenBackend()=>Selected is {} site?Work(async()=>{
        if(string.IsNullOrWhiteSpace(site.AdminPath)||site.AdminPathSource is not ("manual" or ".env")&&!AdminEntryDetector.Exists(site.Directory,site.AdminPath)){
            progress.Text=T("正在识别后台入口…","Detecting admin entry…","管理入口を検索中…");
            var scan=await Task.Run(()=>AdminEntryDetector.Scan(site.Directory));
            if(scan.Entries.Count==1&&scan.Entries[0].Confidence>=80&&!scan.Limited&&scan.Skipped==0)RecordBackend(site,scan.Entries[0].Path,scan.Entries[0].Source);
            else if(!ChooseBackend(site,scan))return;
        }
        if(!AdminEntryDetector.TryNormalize(site.AdminPath,out var path)||path=="")throw new IOException(T("请在后台入口中填写有效的站内地址。","Set a valid local path in Admin entry.","管理入口に有効なサイト内パスを設定してください。"));
        await runtime.StartSiteAsync(site);Open(AdminEntryDetector.Url(runtime.SiteUrl(site),path));
    }):Task.CompletedTask;
    Task ConfigureBackend(Site site)=>Work(async()=>{var scan=await Task.Run(()=>AdminEntryDetector.Scan(site.Directory));ChooseBackend(site,scan);});
    void RecordBackend(Site site,string path,string source)
    {
        if(!AdminEntryDetector.TryNormalize(path,out var normalized)||normalized=="")throw new ArgumentException("Invalid admin path.");
        site.AdminPath=normalized;site.AdminPathSource=source;site.AdminPathRecordedAt=DateTime.UtcNow;settings.Save();RefreshProjectDetails();
    }
    bool ChooseBackend(Site site,AdminScanResult scan)
    {
        using var dialog=new Form{Name="backendEditor",Text=T("后台入口","Admin entry","管理入口")+" · "+site.Domain,ClientSize=new Size(720,414),Font=Font,Icon=Icon,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=5};foreach(var height in new[]{68,160,42,44,56})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));dialog.Controls.Add(layout);
        var hint=L(scan.Entries.Count==0?T("未找到明确入口，请填写后台的站内地址，例如 /admin/ 或 /index.php?s=/admin。","No entry found. Enter a local path, such as /admin/ or /index.php?s=/admin.","入口が見つかりません。/admin/ などのサイト内パスを入力してください。"):T("选择识别到的入口，或填写自定义地址。保存后会记住此项目的后台入口。","Choose a detected entry or enter a custom path. The project remembers your choice.","候補を選択するかパスを入力してください。このプロジェクトに記録します。"),9);layout.Controls.Add(hint,0,0);
        var candidates=new ListBox{Name="backendCandidates",Dock=DockStyle.Fill,IntegralHeight=false,HorizontalScrollbar=true};foreach(var entry in scan.Entries)candidates.Items.Add(entry.Path+"  ·  "+entry.Source);layout.Controls.Add(candidates,0,1);
        var path=new TextBox{Name="backendPath",Text=site.AdminPath,PlaceholderText="/admin/"};layout.Controls.Add(new InputFrame(path){Dock=DockStyle.Top,Height=36,Margin=new Padding(0,6,0,0)},0,2);
        var detail=L("",9);detail.ForeColor=Muted;layout.Controls.Add(detail,0,3);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};layout.Controls.Add(footer,0,4);
        var save=new ThemeButton{Name="saveBackend",Text=T("记住入口","Save entry","入口を保存"),Width=180,Height=36,BackColor=Palette.Accent};var cancel=new ThemeButton{Text=T("取消","Cancel","キャンセル"),Width=144,Height=36,DialogResult=DialogResult.Cancel};footer.Controls.Add(save);footer.Controls.Add(cancel);dialog.AcceptButton=save;dialog.CancelButton=cancel;
        void Validate(){save.Enabled=AdminEntryDetector.TryNormalize(path.Text,out var normalized)&&normalized!="";detail.Text=!save.Enabled?T("请填写有效的站内路径。","Enter a valid local path.","有効なサイト内パスを入力してください。"):scan.Limited||scan.Skipped>0?T("部分目录未能扫描，请确认入口。","Some folders were not scanned; review the entry.","一部のフォルダーは未確認です。入口を確認してください。"):T("下次点击“打开后台”会直接使用此地址。","Open admin will use this address next time.","次回の管理画面はこのアドレスを使用します。");}
        candidates.SelectedIndexChanged+=(_,_)=>{if(candidates.SelectedIndex>=0)path.Text=scan.Entries[candidates.SelectedIndex].Path;};path.TextChanged+=(_,_)=>Validate();if(path.Text==""&&scan.Entries.Count>0)candidates.SelectedIndex=0;Validate();
        save.Click+=(_,_)=>{Validate();if(save.Enabled)dialog.DialogResult=DialogResult.OK;};
        if(Palette.Show(dialog,this)!=DialogResult.OK)return false;
        AdminEntryDetector.TryNormalize(path.Text,out var chosen);var chosenEntry=scan.Entries.FirstOrDefault(x=>x.Path==chosen);RecordBackend(site,chosen,chosenEntry?.Source??"manual");return true;
    }
}
