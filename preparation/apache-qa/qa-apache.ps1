param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Exe)
# 易开面板 Apache 集成与单服务启停验收：只使用 $Root 下的独立配置和端口。
$ErrorActionPreference='Stop'
$report=[System.Collections.Generic.List[object]]::new()
function Check([bool]$ok,[string]$name,$detail=''){ $report.Add([ordered]@{check=$name;passed=$ok;detail="$detail"}); Write-Output ("{0} {1} {2}" -f ($(if($ok){'PASS'}else{'FAIL'}),$name,$detail)) }
function Panel([string[]]$arguments){
    $err=Join-Path $Root 'logs\panel-last-error.txt'; if(Test-Path $err){Remove-Item $err}
    # Start-Process -Wait 会等待面板拉起的服务子进程，这里只等面板本身。
    $p=[Diagnostics.Process]::Start($Exe,("--root `"$Root`" "+($arguments -join ' ')));$p.WaitForExit()
    $msg=if(Test-Path $err){(Get-Content $err -TotalCount 1)}else{''}
    Check ($p.ExitCode -eq 0) ("panel "+($arguments -join ' ')) $msg
}
function Keys(){ $f=Join-Path $Root 'temp\panel-processes.json'; if(!(Test-Path $f)){return @{}}; $h=@{}; foreach($e in (Get-Content $f -Raw | ConvertFrom-Json)){ $h[$e.Key]=$e.Pid }; $h }
function Open([int]$port){ $c=New-Object Net.Sockets.TcpClient; try{ $t=$c.ConnectAsync('127.0.0.1',$port); return $t.Wait(700) -and $c.Connected }catch{ return $false }finally{ $c.Dispose() } }
function Code([string]$url){
    # 不跟随跳转：重定向记录为 "302 /install/"，便于逐项比较两种服务器。
    $req=[Net.HttpWebRequest]::Create($url);$req.AllowAutoRedirect=$false;$req.Timeout=20000
    try{ $res=$req.GetResponse() }catch [Net.WebException]{ $res=$_.Exception.Response; if(!$res){ return '-1' } }
    try{ $code=[int]$res.StatusCode; $loc=$res.Headers['Location']; if($loc){ return "$code $loc" } return "$code" }finally{ $res.Close() }
}
function Body([string]$url){ try{ (Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 20).Content }catch{ '' } }
$cfg=Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
$cms="http://127.0.0.1:$($cfg.sites[0].httpPort)"; $php="http://127.0.0.1:$($cfg.sites[1].httpPort)"; $db="http://127.0.0.1:$($cfg.dbManagerPort)"
$cmsPort=$cfg.sites[0].httpPort; $phpPort=$cfg.sites[1].httpPort; $dbPort=$cfg.dbManagerPort; $mysqlPort=$cfg.mysql80Port
$urls=[ordered]@{
  'cms /'="$cms/"; 'cms /contact.html'="$cms/contact.html"; 'cms /config/config.php'="$cms/config/config.php"; 'cms /storage/x'="$cms/storage/x"
  'cms /.htaccess'="$cms/.htaccess"; 'cms /uploads/a.php'="$cms/uploads/a.php"; 'cms /assets/css/tailwind.css'="$cms/assets/css/tailwind.css"
  'php /'="$php/"; 'php /some/route'="$php/some/route"; 'php /storage/a'="$php/storage/a"; 'php /.env'="$php/.env"
  'db /'="$db/?site=qaphp_yikai&lang=zh"; 'db adminer'="$db/adminer.php/qaphp_yikai/zh"; 'db /common.php'="$db/common.php"; 'db /vendor/'="$db/vendor/"; 'db /index.php/x'="$db/index.php/x"
}
function Matrix([string]$server){ $m=[ordered]@{}; foreach($k in $urls.Keys){ $m[$k]=Code $urls[$k] }; $report.Add([ordered]@{check="http matrix $server";passed=$true;detail=($m|ConvertTo-Json -Compress)}); Write-Host "MATRIX $server $($m|ConvertTo-Json -Compress)"; return $m }

# A. 默认一起启动：Nginx + 项目 PHP 8.2 + MySQL 8.0 + 数据库页面
Panel @('--start')
$k=Keys
Check ($k.ContainsKey('nginx') -and !$k.ContainsKey('apache')) 'default web server is nginx' ($k.Keys -join ',')
Check ($k.ContainsKey('php-qacms_yikai') -and $k.ContainsKey('php-qaphp_yikai') -and $k.ContainsKey('php-db')) 'php processes started' ($k.Keys -join ',')
Check ($k.ContainsKey('mysql80') -and (Open $mysqlPort)) 'mysql80 started with environment'
Check ((Body "$php/") -match 'PHP project is ready') 'nginx serves blank php project'
$nginx=Matrix 'nginx'

# B. 切换到 Apache
Panel @('--web-server','apache')
$k=Keys
Check ($k.ContainsKey('apache') -and !$k.ContainsKey('nginx')) 'switched to apache' ($k.Keys -join ',')
Check ((Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json).webServer -eq 'apache') 'webServer saved as apache'
Check (@(Get-Process nginx -ErrorAction SilentlyContinue | Where-Object { $_.Path -like 'D:\yikai\soft\nginx\*' }).Count -eq 0) 'panel nginx stopped after switch'
Check ((Body "$php/") -match 'PHP project is ready') 'apache serves blank php project'
Check ((Body "$php/some/route") -match 'PHP project is ready') 'apache FallbackResource routes to index.php'
$apache=Matrix 'apache'
foreach($name in $urls.Keys){ Check (($nginx[$name] -eq $apache[$name]) -or ($nginx[$name] -in 403,404 -and $apache[$name] -in 403,404)) "same outcome $name" "nginx=$($nginx[$name]) apache=$($apache[$name])" }
Check ((Body "$db/adminer.php/qaphp_yikai/zh") -match 'qaphp_yikai') 'apache adminer path info reaches database'

# C/D. 单独停止、启动 Web 服务器
$phpPid=(Keys)['php-qaphp_yikai']
Panel @('--service','web','--action','stop')
$k=Keys
Check (!$k.ContainsKey('apache') -and !(Open $cmsPort) -and !(Open $dbPort)) 'web stop closes site ports'
Check ($k['php-qaphp_yikai'] -eq $phpPid -and (Open $mysqlPort)) 'web stop keeps php and mysql running'
Panel @('--service','web','--action','start')
Check ((Keys).ContainsKey('apache') -and (Open $cmsPort) -and (Code "$php/") -eq 200) 'web start restores apache'
$apachePid=(Keys)['apache']
Panel @('--service','web','--action','restart')
Check ((Keys)['apache'] -ne $apachePid -and (Code "$php/") -eq 200) 'web restart replaces apache process'

# E/F. 单独停止、启动 PHP
Panel @('--service','php','--action','stop')
$k=Keys
Check (!$k.ContainsKey('php-qaphp_yikai') -and !$k.ContainsKey('php-qacms_yikai') -and $k.ContainsKey('php-db')) 'php stop keeps db page php' ($k.Keys -join ',')
Check ((Code "$php/") -eq 503) 'site returns 503 while php stopped' (Code "$php/")
Panel @('--service','php','--action','start')
Check ((Code "$php/") -eq 200) 'php start restores site'

# G. 单独停止、启动、重启 MySQL 8.0
Panel @('--service','mysql80','--action','stop')
Check (!(Keys).ContainsKey('mysql80') -and !(Open $mysqlPort)) 'mysql80 stop'
Check ((Keys).ContainsKey('apache') -and (Code "$php/") -eq 200) 'mysql stop keeps web running'
Panel @('--service','mysql80','--action','start')
Check ((Keys).ContainsKey('mysql80') -and (Open $mysqlPort)) 'mysql80 start'
$mysqlPid=(Keys)['mysql80']
Panel @('--service','mysql80','--action','restart')
Check ((Keys)['mysql80'] -ne $mysqlPid -and (Open $mysqlPort)) 'mysql80 restart'

# H. 数据库页面
$dbPid=(Keys)['php-db']
Panel @('--service','dbpage','--action','restart')
Check ((Keys)['php-db'] -ne $dbPid -and (Code "$db/?site=qaphp_yikai&lang=zh") -eq 200) 'db page restart'

# I. 停用项目返回 503
$json=Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json; $json.sites[1].enabled=$false; $json|ConvertTo-Json -Depth 5|Set-Content (Join-Path $Root 'config\panel.json') -Encoding UTF8
Panel @('--service','web','--action','restart')
Check ((Code "$php/") -eq 503 -and (Code "$cms/") -ne 503) 'disabled project returns 503 on apache'
$json.sites[1].enabled=$true; $json.webServer='apache'; $json|ConvertTo-Json -Depth 5|Set-Content (Join-Path $Root 'config\panel.json') -Encoding UTF8
Panel @('--service','web','--action','restart')
Check ((Code "$php/") -eq 200) 'project re-enabled'

# J. 切回 Nginx
Panel @('--web-server','nginx')
$k=Keys
Check ($k.ContainsKey('nginx') -and !$k.ContainsKey('apache') -and (Code "$php/") -eq 200) 'switched back to nginx'
Check (@(Get-Process httpd -ErrorAction SilentlyContinue | Where-Object { $_.Path -like 'D:\yikai\soft\apache\*' }).Count -eq 0) 'no apache processes remain'

# K. 全部停止
Panel @('--stop')
Check ((Keys).Count -eq 0 -and !(Open $cmsPort) -and !(Open $dbPort) -and !(Open $mysqlPort)) 'stop all closes every service'

$failed=@($report | Where-Object { -not $_.passed }).Count
$report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot 'verification.json') -Encoding UTF8
Write-Output "FAILED=$failed TOTAL=$($report.Count)"
