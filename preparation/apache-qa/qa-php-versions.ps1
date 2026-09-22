param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Exe)
# 按 PHP 版本单独启停、默认 PHP 版本：qacms 用 8.2，qaphp 用 8.5，只使用 $Root 的独立端口。
$ErrorActionPreference='Stop'
$report=[System.Collections.Generic.List[object]]::new()
function Check([bool]$ok,[string]$name,$detail=''){ $report.Add([ordered]@{check=$name;passed=$ok;detail="$detail"}); Write-Output ("{0} {1} {2}" -f ($(if($ok){'PASS'}else{'FAIL'}),$name,$detail)) }
function Panel([string[]]$arguments){
    $err=Join-Path $Root 'logs\panel-last-error.txt'; if(Test-Path $err){Remove-Item $err}
    $p=[Diagnostics.Process]::Start($Exe,("--root `"$Root`" "+($arguments -join ' ')));$p.WaitForExit()
    $msg=if(Test-Path $err){(Get-Content $err -TotalCount 1)}else{''}
    Check ($p.ExitCode -eq 0) ("panel "+($arguments -join ' ')) $msg
}
function Keys(){ $f=Join-Path $Root 'temp\panel-processes.json'; if(!(Test-Path $f)){return @{}}; $h=@{}; foreach($e in (Get-Content $f -Raw | ConvertFrom-Json)){ $h[$e.Key]=$e.Pid }; $h }
function Code([string]$url){
    $req=[Net.HttpWebRequest]::Create($url);$req.AllowAutoRedirect=$false;$req.Timeout=20000
    try{ $res=$req.GetResponse() }catch [Net.WebException]{ $res=$_.Exception.Response; if(!$res){ return -1 } }
    try{ return [int]$res.StatusCode }finally{ $res.Close() }
}
function Config(){ Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json }
function Save($json){ $json | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Root 'config\panel.json') -Encoding UTF8 }
function ExeOf([int]$procId){ (Get-CimInstance Win32_Process -Filter "ProcessId=$procId").CommandLine }

$json=Config; $json.webServer='nginx'; $json.phpDefault='8.2'; foreach($s in $json.sites){$s.enabled=$true}; $json.sites[0].php='8.2'; $json.sites[1].php='8.5'; Save $json
$cms="http://127.0.0.1:$($json.sites[0].httpPort)/"; $php="http://127.0.0.1:$($json.sites[1].httpPort)/"

Panel @('--start')
$k=Keys
Check ((ExeOf $k['php-qacms_yikai']) -like '*soft\php\8.2\php-cgi.exe*') 'qacms runs php 8.2'
Check ((ExeOf $k['php-qaphp_yikai']) -like '*soft\php\8.5\php-cgi.exe*') 'qaphp runs php 8.5'
Check ((Code $php) -eq 200 -and (Code $cms) -eq 302) 'both projects reachable'

# 只停 8.5：8.2 项目和数据库页面不受影响
$cmsPid=$k['php-qacms_yikai']; $dbPid=$k['php-db']
Panel @('--service','php8.5','--action','stop')
$k=Keys
Check (!$k.ContainsKey('php-qaphp_yikai') -and $k['php-qacms_yikai'] -eq $cmsPid -and $k['php-db'] -eq $dbPid) 'php8.5 stop keeps php8.2 and db page' ($k.Keys -join ',')
Check ((Code $php) -eq 502 -or (Code $php) -eq 503) 'php 8.5 project unavailable' (Code $php)
Check ((Code $cms) -eq 302) 'php 8.2 project still served'
Panel @('--service','php8.5','--action','start')
Check ((Keys).ContainsKey('php-qaphp_yikai') -and (Code $php) -eq 200) 'php8.5 start restores project'

# 只重启 8.2
$k=Keys; $phpPid=$k['php-qaphp_yikai']
Panel @('--service','php8.2','--action','restart')
$k=Keys
Check ($k['php-qacms_yikai'] -ne $cmsPid -and $k['php-qaphp_yikai'] -eq $phpPid -and (Code $cms) -eq 302) 'php8.2 restart only replaces 8.2 process'

# 没有项目使用 8.0：启动不报错也不产生进程
$before=(Keys).Count
Panel @('--service','php8.0','--action','start')
Check ((Keys).Count -eq $before) 'php8.0 start with no projects starts nothing'

# Apache 下同样按版本生效
Panel @('--web-server','apache')
Panel @('--service','php8.5','--action','stop')
Check ((Code $php) -eq 503 -and (Code $cms) -eq 302) 'apache: php8.5 stop isolates 8.5 project' ("php=$(Code $php) cms=$(Code $cms)")
Panel @('--service','php','--action','start')
Check ((Code $php) -eq 200) 'apache: php start restores all versions'
Panel @('--web-server','nginx')

# 默认 PHP 版本用于新项目
Panel @('--stop')
$json=Config; $json.phpDefault='8.5'; Save $json
Panel @('--create-site','qadefault','--template','php','--database','sqlite')
$created=(Config).sites | Where-Object id -eq 'qadefault_yikai'
Check ($created -and $created.php -eq '8.5') 'new project uses default PHP 8.5' $created.php
$json=Config; $json.sites=@($json.sites | Where-Object id -ne 'qadefault_yikai'); $json.phpDefault='8.2'; $json.sites[1].php='8.2'; Save $json
$dir=Join-Path $Root 'wwwroot\qadefault.yikai'; if(Test-Path $dir){Remove-Item -Recurse -Force $dir}
Check ((Keys).Count -eq 0) 'environment stopped'

$failed=@($report | Where-Object { -not $_.passed }).Count
$report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot 'verification-php-versions.json') -Encoding UTF8
Write-Output "FAILED=$failed TOTAL=$($report.Count)"
