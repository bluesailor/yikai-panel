param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Exe)
# 新建项目时自定义数据库名、专属用户和密码：创建、权限隔离、PHP 连接、安装表单预填、数据库页面、改密同步、MySQL 5.7。只用 $Root 的独立端口。
$ErrorActionPreference='Stop'
$report=[System.Collections.Generic.List[object]]::new()
$utf8=New-Object Text.UTF8Encoding $false
function Check([bool]$ok,[string]$name,$detail=''){ $report.Add([ordered]@{check=$name;passed=$ok;detail="$detail"}); Write-Output ("{0} {1} {2}" -f ($(if($ok){'PASS'}else{'FAIL'}),$name,$detail)) }
function Panel([string]$arguments,[int]$expect=0){
    $err=Join-Path $Root 'logs\panel-last-error.txt'; if(Test-Path $err){Remove-Item $err}
    $p=[Diagnostics.Process]::Start($Exe,("--root `"$Root`" "+$arguments));$p.WaitForExit()
    $script:lastError=if(Test-Path $err){[IO.File]::ReadAllText($err)}else{''}
    Check ($p.ExitCode -eq $expect) ("panel "+($arguments -replace '--db-password "[^"]*"','--db-password ***')) (($script:lastError -split "`n")[0])
}
function Config(){ Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
function Save($json){ $json | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Root 'config\panel.json') -Encoding UTF8 }
function Body([string]$url){ try{ (Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 30).Content }catch{ "ERR $($_.Exception.Message)" } }
function Quote([string]$v){ '"'+$v.Replace('\','\\').Replace('"','\"')+'"' }  # MySQL 选项文件里的双引号字符串
function ArgQuote([string]$v){ '"'+$v.Replace('"','\"')+'"' }  # Windows 命令行参数：反斜杠只在引号前才需转义
function Mysql([string]$kind,[string]$user,[string]$password,[string]$sql){
    $cfg=Config; $port=if($kind -eq 'mysql57'){$cfg.mysql57Port}else{$cfg.mysql80Port}; $ver=if($kind -eq 'mysql57'){'5.7'}else{'8.0'}
    $ini=Join-Path $Root ('temp\qa-client-'+[guid]::NewGuid().ToString('N')+'.ini')
    [IO.File]::WriteAllText($ini,"[client]`nhost=127.0.0.1`nport=$port`nuser=$(Quote $user)`npassword=$(Quote $password)`n",$utf8)
    $psi=New-Object Diagnostics.ProcessStartInfo (Join-Path $Root "soft\mysql\$ver\bin\mysql.exe"); $psi.Arguments="--defaults-file=`"$ini`" --protocol=TCP --batch --skip-column-names -e `"$sql`""; $psi.UseShellExecute=$false; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    $p=[Diagnostics.Process]::Start($psi); $o=$p.StandardOutput.ReadToEndAsync(); $e=$p.StandardError.ReadToEndAsync(); $p.WaitForExit(); Remove-Item $ini
    return [pscustomobject]@{Code=$p.ExitCode;Out=$o.Result.Trim();Err=$e.Result.Trim()}
}

$cleanup=@('qadbuser_yikai','qadb57_yikai','qafail_yikai','qafail2_yikai')
Panel '--stop'
$json=Config; $json.webServer='nginx'; $json.sites=@($json.sites | Where-Object { $cleanup -notcontains $_.id }); Save $json
foreach($n in 'qadbuser.yikai','qadb57.yikai','qafail.yikai','qafail2.yikai'){ $d=Join-Path $Root "wwwroot\$n"; if(Test-Path $d){Remove-Item -Recurse -Force $d} }

$password="Qa'p\w 9#"
# 1. 非法设置在复制文件前被拒绝
Panel "--create-site qafail --template php --database mysql80 --db-name qa_custom_db --db-user root --db-password x" 1
Check ($script:lastError -match 'Database user' -and !(Test-Path (Join-Path $Root 'wwwroot\qafail.yikai'))) 'root as project user rejected before copying files'
Panel "--create-site qafail2 --template php --database mysql80 --db-name mysql --db-user qa_x --db-password x" 1
Check ($script:lastError -match 'Database name' -and !(Test-Path (Join-Path $Root 'wwwroot\qafail2.yikai'))) 'system database name rejected'

# 2. 创建 YikaiCMS 项目，自定义库名、用户、带特殊字符的密码
Panel "--create-site qadbuser --template yikaicms --database mysql80 --php 8.2 --db-name qa_custom_db --db-user qa_custom_user --db-password $(ArgQuote $password)"
$site=(Config).sites | Where-Object id -eq 'qadbuser_yikai'
Check ($site.databaseName -eq 'qa_custom_db' -and $site.databaseUser -eq 'qa_custom_user' -and $site.databasePassword -eq $password) 'project stores database name, user and password'
Panel "--create-site qafail --template php --database mysql80 --db-name qa_custom_db --db-user qa_other --db-password x" 1
Check ($script:lastError -match 'database name') 'duplicate database name rejected'

$json=Config; $i=0; foreach($s in $json.sites){ $s.enabled=$true; $s.httpPort=18081+$i; $s.fastCgiPort=19085+$i; $i++ }; Save $json
$site=(Config).sites | Where-Object id -eq 'qadbuser_yikai'; $url="http://127.0.0.1:$($site.httpPort)/"
[IO.File]::WriteAllText((Join-Path $site.directory 'qa-db.php'),"<?php `$c=json_decode(file_get_contents(__DIR__.'/qa-db.json'),true); try{`$p=new PDO('mysql:host=127.0.0.1;port='.`$c['port'].';dbname='.`$c['db'].';charset=utf8mb4',`$c['user'],`$c['pass']);echo 'ok:'.`$p->query('SELECT DATABASE()')->fetchColumn();}catch(Throwable `$e){echo 'fail:'.`$e->getMessage();}",$utf8)
[IO.File]::WriteAllText((Join-Path $site.directory 'qa-db.json'),(@{port=(Config).mysql80Port;db='qa_custom_db';user='qa_custom_user';pass=$password}|ConvertTo-Json),$utf8)

# 3. 启动：账号创建、权限只限本库
Panel '--start'
$log=[IO.File]::ReadAllText((Join-Path $Root 'logs\panel.log'),[Text.Encoding]::UTF8)
Check ($log.Contains('Database user ready · qa_custom_user → qa_custom_db')) 'panel created project database user'
$own=Mysql 'mysql80' 'qa_custom_user' $password 'USE qa_custom_db; CREATE TABLE IF NOT EXISTS t(id INT); SELECT COUNT(*) FROM t;'
Check ($own.Code -eq 0) 'project user can create tables in its database' $own.Err
$other=Mysql 'mysql80' 'qa_custom_user' $password 'USE qaphp_yikai; SELECT 1;'
Check ($other.Code -ne 0 -and $other.Err -match 'denied') 'project user denied on other databases' $other.Err
$grants=Mysql 'mysql80' 'qa_custom_user' $password 'SHOW GRANTS;'
Check ($grants.Out -match 'qa_custom_db' -and $grants.Out -notmatch 'ON \*\.\* TO .*WITH|ALL PRIVILEGES ON \*\.\*') 'grants limited to project database' ($grants.Out -replace "`r?`n",' | ')

# 4. PHP 用专属账号连接（MySQL 8 默认认证插件）
Check ((Body "${url}qa-db.php") -eq 'ok:qa_custom_db') 'PHP connects with project user' (Body "${url}qa-db.php")

# 5. 安装表单预填专属账号；数据库页面显示专属账号
$install=Body "${url}install/index.php?step=2"
$installInputs=([regex]::Matches($install,'<input[^>]*name="db_(user|name)"[^>]*>') | ForEach-Object { $_.Value }) -join ' | '
Check ($install -match 'name="db_user"[^>]*value="qa_custom_user"' -and $install -match 'name="db_name"[^>]*value="qa_custom_db"') 'installer prefilled with project database and user' $installInputs
$assets='D:\yikai-soft\dev\yikai-panel\src\Assets\DatabaseTools'
Check ((Get-FileHash (Join-Path $Root 'soft\db-manager\index.php')).Hash -eq (Get-FileHash "$assets\index.php").Hash -and (Get-FileHash (Join-Path $Root 'soft\db-manager\install-prefill.php')).Hash -eq (Get-FileHash "$assets\install-prefill.php").Hash) 'db tools refreshed from panel on start'
$dbPage=Body "http://127.0.0.1:$((Config).dbManagerPort)/?site=qadbuser_yikai&lang=zh"
Check ($dbPage -match 'qa_custom_user') 'db page shows project user'

# 6. 修改密码后重启 PHP：同步到 MySQL
$newPassword='Changed_2026'
$json=Config; ($json.sites | Where-Object id -eq 'qadbuser_yikai').databasePassword=$newPassword; Save $json
Panel '--service php8.2 --action restart'
Check ((Mysql 'mysql80' 'qa_custom_user' $newPassword 'SELECT 1;').Code -eq 0 -and (Mysql 'mysql80' 'qa_custom_user' $password 'SELECT 1;').Code -ne 0) 'password change synced to MySQL'

# 7. MySQL 5.7
Panel "--create-site qadb57 --template php --database mysql57 --php 8.2 --db-name qa57_db --db-user qa57_user --db-password Qa57pass"
$json=Config; ($json.sites | Where-Object id -eq 'qadb57_yikai').enabled=$true; ($json.sites | Where-Object id -eq 'qadb57_yikai').httpPort=18099; ($json.sites | Where-Object id -eq 'qadb57_yikai').fastCgiPort=19099; Save $json
Panel '--start'
$r57=Mysql 'mysql57' 'qa57_user' 'Qa57pass' 'USE qa57_db; SELECT 1;'
Check ($r57.Code -eq 0) 'MySQL 5.7 project user works' $r57.Err

# 清理
Panel '--stop'
$json=Config; $json.sites=@($json.sites | Where-Object { $cleanup -notcontains $_.id }); Save $json
foreach($n in 'qadbuser.yikai','qadb57.yikai'){ $d=Join-Path $Root "wwwroot\$n"; if(Test-Path $d){Remove-Item -Recurse -Force $d} }
$failed=@($report | Where-Object { -not $_.passed }).Count
$report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot 'verification-project-database.json') -Encoding UTF8
Write-Output "FAILED=$failed TOTAL=$($report.Count)"
