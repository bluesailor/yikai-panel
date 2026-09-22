$ErrorActionPreference='Stop'
$config=Get-Content D:\yikai\config\panel.json -Raw|ConvertFrom-Json
$base="http://127.0.0.1:$($config.dbManagerPort)"
$report=@()
$php='D:\yikai\soft\php\8.2\php.exe'
$helper='D:\yikai-soft\dev\yikai-panel\preparation\qa-database.php'
foreach($site in $config.sites|Where-Object {$_.id -like 'qa_*'}){
    $id=$site.id
    $seed=& $php $helper $id seed
    if($LASTEXITCODE -ne 0){throw 'Seed failed'}
    foreach($lang in @('zh','en','ja')){
        $page=Invoke-WebRequest "$base/index.php?site=$id&lang=$lang" -SessionVariable session
        if($page.Content -notmatch 'qa_roundtrip' -or $page.Content -notmatch "<html lang=`"$lang`">"){throw "Page mismatch: $id $lang"}
        $report+=@{site=$id;check="page-$lang";passed=$true}
    }
    $csrf=[regex]::Match($page.Content,'name="csrf" value="([^"]+)"').Groups[1].Value
    $result=Invoke-RestMethod "$base/action.php?site=$id&lang=en" -Method Post -WebSession $session -Body @{csrf=$csrf;action='backup'}
    if(!$result.ok){throw 'Backup failed'}
    $extension=if($site.database -eq 'sqlite'){'sqlite'}else{'sql'}
    $file="D:\yikai-soft\dev\yikai-panel\preparation\$id.$extension"
    Invoke-WebRequest ($base+$result.download) -WebSession $session -OutFile $file
    $changed=& $php $helper $id change
    if($changed -ne 'changed'){throw 'Change failed'}
    $restore=Invoke-RestMethod "$base/action.php?site=$id&lang=en" -Method Post -WebSession $session -Form @{csrf=$csrf;action='restore';backup=Get-Item $file}
    if(!$restore.ok){throw 'Restore failed'}
    $actual=& $php $helper $id check
    if($actual -ne $seed){throw "Roundtrip failed: $id"}
    $report+=@{site=$id;check='backup-download-restore-utf8';passed=$true;bytes=(Get-Item $file).Length}
    $adminer=Invoke-WebRequest "$base/adminer.php/$id/en" -SessionVariable adminSession
    if($adminer.Content -notmatch 'qa_roundtrip'){throw "Adminer failed: $id"}
    $report+=@{site=$id;check='adminer-auto-login';passed=$true}
}
$report|ConvertTo-Json -Depth 5|Set-Content D:\yikai-soft\dev\yikai-panel\preparation\panel-verification.json
$report|Format-Table
