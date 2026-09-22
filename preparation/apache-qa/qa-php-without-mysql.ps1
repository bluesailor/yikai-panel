param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Exe)
# 回归：MySQL 停止时单独启动 PHP 不应报错；MySQL 之后启动时补建新项目数据库。只使用 $Root 的独立端口。
$ErrorActionPreference='Stop'
$report=[System.Collections.Generic.List[object]]::new()
function Check([bool]$ok,[string]$name,$detail=''){ $report.Add([ordered]@{check=$name;passed=$ok;detail="$detail"}); Write-Output ("{0} {1} {2}" -f ($(if($ok){'PASS'}else{'FAIL'}),$name,$detail)) }
function Panel([string[]]$arguments,[int]$expect=0){
    $err=Join-Path $Root 'logs\panel-last-error.txt'; if(Test-Path $err){Remove-Item $err}
    $p=[Diagnostics.Process]::Start($Exe,("--root `"$Root`" "+($arguments -join ' ')));$p.WaitForExit()
    $msg=if(Test-Path $err){(Get-Content $err -TotalCount 1)}else{''}
    Check ($p.ExitCode -eq $expect) ("panel "+($arguments -join ' ')) $msg
}
function Keys(){ $f=Join-Path $Root 'temp\panel-processes.json'; if(!(Test-Path $f)){return @{}}; $h=@{}; foreach($e in (Get-Content $f -Raw | ConvertFrom-Json)){ $h[$e.Key]=$e.Pid }; $h }
function Code([string]$url){
    $req=[Net.HttpWebRequest]::Create($url);$req.AllowAutoRedirect=$false;$req.Timeout=20000
    try{ $res=$req.GetResponse() }catch [Net.WebException]{ $res=$_.Exception.Response; if(!$res){ return -1 } }
    try{ return [int]$res.StatusCode }finally{ $res.Close() }
}
function Config(){ Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json }
function Save($json){ $json | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Root 'config\panel.json') -Encoding UTF8 }
function HasDatabase([string]$name){
    $cfg=Config; $ini=Join-Path $Root 'temp\qa-client.ini'
    Set-Content $ini "[client]`nhost=127.0.0.1`nport=$($cfg.mysql80Port)`nuser=root`npassword=$($cfg.mysqlPassword)" -Encoding ascii
    $out=& (Join-Path $Root 'soft\mysql\8.0\bin\mysql.exe') "--defaults-file=$ini" --batch --skip-column-names -e "SHOW DATABASES LIKE '$name'"
    Remove-Item $ini; return ($out -eq $name)
}

Panel @('--stop')
# 新项目（MySQL 8.0，尚无数据库），与现有项目一起启用
$json=Config; $json.webServer='nginx'; $json.sites=@($json.sites | Where-Object id -ne 'qanewdb_yikai'); Save $json
$dir=Join-Path $Root 'wwwroot\qanewdb.yikai'; if(Test-Path $dir){Remove-Item -Recurse -Force $dir}
Panel @('--create-site','qanewdb','--template','php','--database','mysql80','--php','8.2')
$json=Config; $i=0; foreach($s in $json.sites){ $s.enabled=$true; $s.httpPort=18081+$i; $s.fastCgiPort=19085+$i; $i++ }; Save $json
$new=(Config).sites | Where-Object id -eq 'qanewdb_yikai'; $url="http://127.0.0.1:$($new.httpPort)/"

# 1. MySQL 停止时只启动 PHP：成功，MySQL 保持停止
Panel @('--service','php','--action','start')
$k=Keys
Check ($k.ContainsKey('php-qanewdb_yikai') -and $k.ContainsKey('php-qaphp_yikai') -and $k.ContainsKey('php-qacms_yikai')) 'php starts without mysql' ($k.Keys -join ',')
Check (!$k.ContainsKey('mysql80')) 'mysql stays stopped'
Check ((Get-Content (Join-Path $Root 'logs\panel.log') -Tail 20 -Encoding UTF8 | Select-String 'database deferred · qanewdb.yikai').Count -ge 1) 'deferred database logged'
Panel @('--service','web','--action','start')
Check ((Code $url) -eq 200) 'blank php project served while mysql stopped'

# 2. 启动 MySQL 8.0：补建新项目数据库
Panel @('--service','mysql80','--action','start')
Check (HasDatabase 'qanewdb_yikai') 'mysql start creates deferred database'

# 3. 建库失败时提示带 PHP 的原因（不再只有 "php.exe:"）：用非法数据库名触发 bootstrap.php 报错
Panel @('--stop')
$json=Config; ($json.sites | Where-Object id -eq 'qanewdb_yikai').databaseName='bad name'; Save $json
Panel @('--start') 1
$msg=(Get-Content (Join-Path $Root 'logs\panel-last-error.txt') -Raw)
Check ($msg -match 'Invalid database name') 'failure message includes php reason' (($msg -split "`n")[0])
Panel @('--stop')
$json=Config; $json.sites=@($json.sites | Where-Object id -ne 'qanewdb_yikai'); Save $json
if(Test-Path $dir){Remove-Item -Recurse -Force $dir}
Check ((Keys).Count -eq 0) 'environment stopped'

$failed=@($report | Where-Object { -not $_.passed }).Count
$report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot 'verification-php-without-mysql.json') -Encoding UTF8
Write-Output "FAILED=$failed TOTAL=$($report.Count)"
