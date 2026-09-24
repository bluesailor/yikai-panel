namespace YikaiLocal;

public sealed partial class MainForm
{
    ProjectInfoLabel backendDetail=null!,directoryDetail=null!,siteUpdateDetail=null!;
    Button backendSettings=null!,directoryRefresh=null!,checkSiteUpdate=null!;
    readonly Dictionary<string,DirectorySizeResult> directorySizes=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> directoryErrors=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,SiteUpdateResult> siteUpdateResults=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> siteUpdateErrors=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> siteUpdatePending=new(StringComparer.OrdinalIgnoreCase);
    CancellationTokenSource? directoryCancellation;
    string? directoryPending;
    void AddProjectDetails(TableLayoutPanel card,int row)
    {
        // 与上面的信息行共用标题列宽；整块限制在卡片内容宽度内，右侧按钮不会被拉到宽屏边缘。
        var details=new TableLayoutPanel{Dock=DockStyle.Fill,MaximumSize=new Size(Px(CardContentWidth),0),ColumnCount=2,RowCount=3,Margin=Padding.Empty};details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Px(300)));for(var i=0;i<3;i++)details.RowStyles.Add(new RowStyle(SizeType.Absolute,Px(40)));
        ProjectInfoLabel Detail(string name)=>new(){Name=name,Font=new Font(Font.FontFamily,Fs(9.5f)),ForeColor=Ink,Dock=DockStyle.Fill,CaptionWidth=CaptionColumn};
        backendDetail=Detail("backendDetail");directoryDetail=Detail("directoryDetail");siteUpdateDetail=Detail("siteUpdateDetail");
        backendSettings=B(T("后台入口","Admin entry","管理入口"),()=>{if(Selected is {} s)_=ConfigureBackend(s);},icon:"settings");backendSettings.Name="backendSettings";
        directoryRefresh=B(T("重新计算","Recalculate","再計算"),()=>_ = MeasureSelectedDirectory(true),icon:"sync");directoryRefresh.Name="refreshDirectorySize";
        checkSiteUpdate=B(T("检测更新","Check updates","更新を確認"),()=>_ = CheckSelectedSiteUpdate(),icon:"sync");checkSiteUpdate.Name="checkSiteUpdate";
        foreach(var button in new[]{backendSettings,directoryRefresh,checkSiteUpdate}){button.Font=new Font(Font.FontFamily,Fs(9f));button.Dock=DockStyle.Fill;button.FlatAppearance.BorderSize=0;button.BackColor=Palette.Card;button.Margin=new Padding(0,0,24,6);}
        details.Controls.Add(backendDetail,0,0);details.Controls.Add(backendSettings,1,0);details.Controls.Add(directoryDetail,0,1);details.Controls.Add(directoryRefresh,1,1);card.Controls.Add(details,0,row);
        details.Controls.Add(siteUpdateDetail,0,2);details.Controls.Add(checkSiteUpdate,1,2);
        serviceTips.SetToolTip(directoryRefresh,T("计算项目目录内文件的总大小；不包含独立 MySQL 数据，跳过目录链接。","File sizes in the project folder; excludes external MySQL data and links.","フォルダー内のファイル容量。外部 MySQL データとリンクは対象外。"));
        serviceTips.SetToolTip(checkSiteUpdate,T("仅检测 YikaiCMS / WordPress 核心程序更新；不会下载或安装。","Checks YikaiCMS / WordPress core updates only; does not download or install.","YikaiCMS / WordPress 本体の更新のみ確認します。ダウンロードやインストールは行いません。"));
    }
    void RefreshProjectDetails()
    {
        if(building||IsDisposed||backendDetail==null)return;
        var site=Selected;backendSettings.Enabled=directoryRefresh.Enabled=!busy&&site!=null;
        var admin=T("后台","Admin","管理")+"\t";var capacity=T("容量","Size","容量")+"\t";
        backendDetail.Text=site==null?"":admin+(string.IsNullOrWhiteSpace(site.AdminPath)?T("点击右侧按钮识别，或打开后台时自动记录","Detected when you open the admin page","管理画面を開くと自動検出"):site.AdminPath+T(" · 已记录"," · Saved"," · 記録済み"));
        serviceTips.SetToolTip(backendDetail,site==null?"":site.AdminPath+"\n"+site.AdminPathSource+(site.AdminPathRecordedAt is {} date?"\n"+date.ToLocalTime().ToString("g"):""));
        if(site==null){directoryDetail.Text=siteUpdateDetail.Text="";checkSiteUpdate.Visible=false;projectCard.RowStyles[4].Height=Px(84);directoryCancellation?.Cancel();return;}
        var installation=SiteUpdates.Detect(site.Directory);
        checkSiteUpdate.Visible=installation!=null;projectCard.RowStyles[4].Height=Px(installation==null?84:124);
        siteUpdateDetail.ForeColor=Ink;
        if(installation==null)siteUpdateDetail.Text="";
        else
        {
            var updateKey=site.Directory+"|"+installation.Product+"|"+installation.Version;
            checkSiteUpdate.Enabled=!busy&&!siteUpdatePending.Contains(updateKey);
            var caption=T("更新","Update","更新")+"\t";
            if(siteUpdatePending.Contains(updateKey))siteUpdateDetail.Text=caption+T("正在检测…","Checking…","確認中…");
            else if(siteUpdateResults.TryGetValue(updateKey,out var update))
            {
                siteUpdateDetail.ForeColor=update.State==SiteUpdateState.Available?Palette.Warning:update.State==SiteUpdateState.Restricted?Palette.Secondary:Palette.Success;
                siteUpdateDetail.Text=caption+(update.State switch
                {
                    SiteUpdateState.Available=>T($"{installation.Product} {installation.Version} → {update.LatestVersion} 可更新",$"{installation.Product} {installation.Version} → {update.LatestVersion} available",$"{installation.Product} {installation.Version} → {update.LatestVersion} に更新可能"),
                    SiteUpdateState.Restricted=>T("更新服务未授权此域名，请在站点后台查看","Update access is unavailable for this domain; check the site admin","このドメインは更新確認の対象外です。管理画面で確認してください"),
                    _=>T($"{installation.Product} {installation.Version} 已是最新",$"{installation.Product} {installation.Version} is current",$"{installation.Product} {installation.Version} は最新版です")
                });
            }
            else siteUpdateDetail.Text=caption+(siteUpdateErrors.ContainsKey(updateKey)?T("检测失败，请重试","Check failed; retry","確認できませんでした。再試行してください"):T($"{installation.Product} {installation.Version} · 尚未检测",$"{installation.Product} {installation.Version} · Not checked",$"{installation.Product} {installation.Version} · 未確認"));
            serviceTips.SetToolTip(siteUpdateDetail,siteUpdateErrors.TryGetValue(updateKey,out var error)?error:T("仅检测核心程序，不会自动升级或修改文件。","Checks core only; does not upgrade or change files.","本体のみ確認します。自動更新やファイル変更は行いません。"));
        }
        var key=site.Directory;
        if(directorySizes.TryGetValue(key,out var result)){
            var prefix=result.Limited||result.Skipped>0?T("至少 ","At least ","少なくとも "):"";
            directoryDetail.Text=capacity+prefix+DirectorySize.Format(result.Bytes)+T($" · {result.Files:N0} 个文件",$" · {result.Files:N0} files",$" · {result.Files:N0} ファイル");
            serviceTips.SetToolTip(directoryDetail,T("文件总大小，不含独立 MySQL 数据。","File sizes; excludes external MySQL data.","ファイル容量。外部 MySQL データは含みません。")+"\n"+result.ScannedAt.ToLocalTime().ToString("g")+T($"\n跳过 {result.Skipped} 项",$"\nSkipped {result.Skipped} entries",$"\n除外 {result.Skipped} 件")+(result.Limited?T(" · 已达扫描上限"," · Scan limit reached"," · 検索上限に到達"):""));
        }else if(directoryErrors.TryGetValue(key,out var error)){directoryDetail.Text=capacity+T("未能读取，请重试","Unavailable; retry","取得できませんでした");serviceTips.SetToolTip(directoryDetail,error);}
        else directoryDetail.Text=capacity+(directoryPending==key?T("正在计算…","Calculating…","計算中…"):T("尚未计算","Not calculated","未計算"));
        if(!renderOnly&&IsHandleCreated&&!busy&&directoryPending!=key&&!directorySizes.ContainsKey(key)&&!directoryErrors.ContainsKey(key))_ = MeasureSelectedDirectory(false);
    }
    async Task MeasureSelectedDirectory(bool force)
    {
        if(Selected is not {} site||busy)return;var key=site.Directory;if(!force&&directorySizes.ContainsKey(key))return;
        directoryCancellation?.Cancel();var cancellation=new CancellationTokenSource();directoryCancellation=cancellation;directoryPending=key;directoryErrors.Remove(key);if(force)directorySizes.Remove(key);
        directoryDetail.Text=T("容量","Size","容量")+"\t"+T("正在计算…","Calculating…","計算中…");
        try{var result=await Task.Run(()=>DirectorySize.Measure(key,cancellation.Token));if(!cancellation.IsCancellationRequested&&!IsDisposed)directorySizes[key]=result;}
        catch(OperationCanceledException){}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException){if(!IsDisposed)directoryErrors[key]=e.Message;}
        finally{if(directoryCancellation==cancellation){directoryPending=null;directoryCancellation=null;}cancellation.Dispose();if(!IsDisposed)RefreshProjectDetails();}
    }
    void CancelDirectoryMeasure(){directoryCancellation?.Cancel();directoryPending=null;}

    async Task CheckSelectedSiteUpdate()
    {
        if(busy||Selected is not {} site||SiteUpdates.Detect(site.Directory) is not {} installation)return;
        var key=site.Directory+"|"+installation.Product+"|"+installation.Version;
        if(!siteUpdatePending.Add(key))return;
        siteUpdateResults.Remove(key);siteUpdateErrors.Remove(key);RefreshProjectDetails();
        try
        {
            using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(12)};
            client.DefaultRequestHeaders.UserAgent.ParseAdd("YikaiPanel/"+PanelUpdate.CurrentVersion);
            using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            siteUpdateResults[key]=await SiteUpdates.CheckAsync(installation,site.Domain,settings.Language,site.Php,client,cancellation.Token);
        }
        catch(Exception e) when(e is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {siteUpdateErrors[key]=e.Message;}
        finally{siteUpdatePending.Remove(key);if(!IsDisposed)RefreshProjectDetails();}
    }
}
