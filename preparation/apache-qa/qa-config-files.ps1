param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Exe)
# 配置文件编辑：检查、保存并应用、失败回滚。$Root 必须有独立的 soft\php\8.2（不是指向正式环境的链接）。
$ErrorActionPreference='Stop'
if((Get-Item (Join-Path $Root 'soft\php\8.2')).Attributes -match 'ReparsePoint'){ throw 'soft\php\8.2 is a link to a real environment; refusing to edit its php.ini.' }
$report=[System.Collections.Generic.List[object]]::new()
$utf8=New-Object Text.UTF8Encoding $false
function Check([bool]$ok,[string]$name,$detail=''){ $report.Add([ordered]@{check=$name;passed=$ok;detail="$detail"}); Write-Output ("{0} {1} {2}" -f ($(if($ok){'PASS'}else{'FAIL'}),$name,$detail)) }
function Panel([string]$arguments,[int]$expect=0){
    $err=Join-Path $Root 'logs\panel-last-error.txt'; if(Test-Path $err){Remove-Item $err}
    $p=[Diagnostics.Process]::Start($Exe,("--root `"$Root`" "+$arguments));$p.WaitForExit()
    $script:lastError=if(Test-Path $err){[IO.File]::ReadAllText($err)}else{''}
    Check ($p.ExitCode -eq $expect) ("panel "+$arguments) (($script:lastError -split "`n")[0])
}
function Keys(){ $f=Join-Path $Root 'temp\panel-processes.json'; if(!(Test-Path $f)){return @{}}; $h=@{}; foreach($e in (Get-Content $f -Raw | ConvertFrom-Json)){ $h[$e.Key]=$e.Pid }; $h }
function Config(){ Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
function Save($json){ $json | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Root 'config\panel.json') -Encoding UTF8 }
function Code([string]$url,[int]$postBytes=0){
    $req=[Net.HttpWebRequest]::Create($url);$req.AllowAutoRedirect=$false;$req.Timeout=20000
    if($postBytes -gt 0){$req.Method='POST';$req.ContentType='application/octet-stream';$body=New-Object byte[] $postBytes;$req.ContentLength=$postBytes;try{$st=$req.GetRequestStream();$st.Write($body,0,$body.Length);$st.Close()}catch{ return -2 }}
    try{ $res=$req.GetResponse() }catch [Net.WebException]{ $res=$_.Exception.Response; if(!$res){ return -1 } }
    try{ return [int]$res.StatusCode }finally{ $res.Close() }
}
function Body([string]$url){ try{ (Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 20).Content }catch{ '' } }
function Input([string]$name,[string]$text){ $f=Join-Path $Root "temp\qa-$name"; [IO.File]::WriteAllText($f,$text,$utf8); return $f }
function MysqlValue([string]$sql){
    $cfg=Config; $ini=Join-Path $Root 'temp\qa-client.ini'
    [IO.File]::WriteAllText($ini,"[client]`nhost=127.0.0.1`nport=$($cfg.mysql80Port)`nuser=root`npassword=$($cfg.mysqlPassword)`n",$utf8)
    $out=& (Join-Path $Root 'soft\mysql\8.0\bin\mysql.exe') "--defaults-file=$ini" --batch --skip-column-names -e $sql; Remove-Item $ini; return "$out".Trim()
}

$phpIni=Join-Path $Root 'soft\php\8.2\php.ini'; $originalPhp=[IO.File]::ReadAllText($phpIni)
$dbIni=Join-Path $Root 'config\phpmyadmin-php.ini'; $originalDb=[IO.File]::ReadAllText($dbIni)
foreach($c in 'custom-nginx.conf','custom-apache.conf','custom-mysql80.ini'){ $f=Join-Path $Root "config\$c"; if(Test-Path $f){Remove-Item $f} }
Panel '--stop'
$json=Config; $json.webServer='nginx'; foreach($s in $json.sites){$s.enabled=$true;$s.php='8.2'}; Save $json
$site=(Config).sites | Where-Object id -eq 'qaphp_yikai'; $php="http://127.0.0.1:$($site.httpPort)/"
[IO.File]::WriteAllText((Join-Path $site.directory 'qa-ini.php'),"<?php echo ini_get('memory_limit');",$utf8)

# 1. php.ini 语法错误：检查不通过、文件不变
$bad=Input 'php-bad.ini' ($originalPhp+"`nbroken = `"unterminated`n")
Panel "--config-check php8.2 --input `"$bad`"" 1
Check ($script:lastError -match 'syntax error') 'php.ini syntax error reported' (($script:lastError -split "`n")[0])
Check ([IO.File]::ReadAllText($phpIni) -eq $originalPhp) 'php.ini unchanged after failed check'

# 2. 运行中保存有效 php.ini：重启 PHP 8.2 并生效，旧文件已备份
Panel '--start'
$before=Keys
$good=Input 'php-good.ini' ($originalPhp+"`nmemory_limit=321M`n")
Panel "--config-save php8.2 --input `"$good`""
$after=Keys
Check ($after['php-qaphp_yikai'] -ne $before['php-qaphp_yikai'] -and $after['php-db'] -eq $before['php-db']) 'php 8.2 restarted, db page untouched'
Check ((Body "${php}qa-ini.php") -eq '321M') 'new memory_limit visible to project' (Body "${php}qa-ini.php")
Check (@(Get-ChildItem (Join-Path $Root 'backups\config') -Filter 'php-8.2-*.ini').Count -ge 1) 'previous php.ini backed up'

# 3. 运行中保存无效 php.ini：拒绝、文件和进程不变
$pidNow=(Keys)['php-qaphp_yikai']
Panel "--config-save php8.2 --input `"$bad`"" 1
Check ([IO.File]::ReadAllText($phpIni).Contains('memory_limit=321M') -and !([IO.File]::ReadAllText($phpIni).Contains('unterminated'))) 'invalid php.ini not written'
Check ((Keys)['php-qaphp_yikai'] -eq $pidNow -and (Code $php) -eq 200) 'php keeps running after rejected save'

# 4. Nginx 自定义配置：覆盖默认上传上限；语法错误回滚
Panel "--config-save nginx --input `"$(Input 'nginx-good.conf' "client_max_body_size 1k;`n")`""
$generated=[IO.File]::ReadAllText((Join-Path $Root 'config\panel-nginx.conf'))
Check ($generated.Contains('custom-nginx.conf') -and !$generated.Contains('client_max_body_size 160m')) 'nginx includes custom file and drops default'
Start-Sleep 2; $c=Code $php 5000; Check ($c -eq 413) 'nginx custom limit applied (413)' $c
Panel "--config-save nginx --input `"$(Input 'nginx-bad.conf' "not_a_directive on;`n")`"" 1
Check ([IO.File]::ReadAllText((Join-Path $Root 'config\custom-nginx.conf')).Contains('1k') -and (Code $php) -eq 200) 'invalid nginx config rolled back, site up'
Panel "--config-save nginx --input `"$(Input 'nginx-empty.conf' "# empty`n")`""
Start-Sleep 2; $c=Code $php 5000; Check ($c -eq 200) 'nginx default limit restored' $c

# 5. Apache 自定义配置
Panel '--web-server apache'
Panel "--config-save apache --input `"$(Input 'apache-good.conf' "Header always set X-Yikai-Custom qa`n")`""
$h=try{(Invoke-WebRequest -UseBasicParsing $php -TimeoutSec 20).Headers['X-Yikai-Custom']}catch{''}
Check ($h -eq 'qa') 'apache custom header applied' $h
Panel "--config-save apache --input `"$(Input 'apache-bad.conf' "NotADirective 1`n")`"" 1
Check ([IO.File]::ReadAllText((Join-Path $Root 'config\custom-apache.conf')).Contains('X-Yikai-Custom') -and (Code $php) -eq 200) 'invalid apache config rolled back, site up'
Panel "--config-save apache --input `"$(Input 'apache-empty.conf' "# empty`n")`""
Panel '--web-server nginx'

# 6. MySQL 8.0 自定义 my.ini
$mysqlPid=(Keys)['mysql80']
Panel "--config-save mysql80 --input `"$(Input 'mysql-good.ini' "[mysqld]`nmax_allowed_packet=64M`n")`""
Check ((Keys)['mysql80'] -ne $mysqlPid -and (MysqlValue 'SELECT @@max_allowed_packet') -eq '67108864') 'mysql restarted with custom option' (MysqlValue 'SELECT @@max_allowed_packet')
$mysqlPid=(Keys)['mysql80']
Panel "--config-save mysql80 --input `"$(Input 'mysql-unknown.ini' "[mysqld]`nnot_an_option=1`n")`"" 1
$k=Keys
Check ($k.ContainsKey('mysql80') -and (MysqlValue 'SELECT @@max_allowed_packet') -eq '67108864' -and [IO.File]::ReadAllText((Join-Path $Root 'config\custom-mysql80.ini')).Contains('64M')) 'unknown mysql option rolled back and mysql restarted' ($script:lastError -replace "`r?`n",' | ')
Check ($script:lastError -match 'not_an_option') 'error names the bad option'
Panel "--config-check mysql80 --input `"$(Input 'mysql-format.ini' "max_allowed_packet=64M`n")`"" 1
Check ($script:lastError -match 'mysqld') 'missing section header rejected by check'
Panel "--config-save mysql80 --input `"$(Input 'mysql-fails.ini' "[mysqld]`ninnodb_buffer_pool_size=4096T`n")`"" 1
$k=Keys
Check ($k.ContainsKey('mysql80') -and (MysqlValue 'SELECT @@max_allowed_packet') -eq '67108864' -and [IO.File]::ReadAllText((Join-Path $Root 'config\custom-mysql80.ini')).Contains('64M')) 'failed mysql start rolled back and restarted' ($script:lastError -replace "`r?`n",' | ')

# 7. 数据库页面 php.ini
$dbPid=(Keys)['php-db']
Panel "--config-save dbpage --input `"$(Input 'db.ini' ($originalDb+"`n; qa comment`n"))`""
Check ((Keys)['php-db'] -ne $dbPid -and (Code "http://127.0.0.1:$((Config).dbManagerPort)/?site=qaphp_yikai&lang=zh") -eq 200) 'db page restarted with new ini'

# 清理：恢复原文件
Panel '--stop'
[IO.File]::WriteAllText($phpIni,$originalPhp,$utf8); [IO.File]::WriteAllText($dbIni,$originalDb,$utf8)
foreach($c in 'custom-nginx.conf','custom-apache.conf','custom-mysql80.ini'){ $f=Join-Path $Root "config\$c"; if(Test-Path $f){Remove-Item $f} }
Remove-Item (Join-Path $site.directory 'qa-ini.php'); Get-ChildItem (Join-Path $Root 'temp') -Filter 'qa-*' | Remove-Item
$failed=@($report | Where-Object { -not $_.passed }).Count
$report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot 'verification-config-files.json') -Encoding UTF8
Write-Output "FAILED=$failed TOTAL=$($report.Count)"
