param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Exe)
# 局域网访问：项目端口在 127.0.0.1 与 0.0.0.0 之间切换，数据库管理页面始终只在本机。Nginx 与 Apache 各测一遍。
$ErrorActionPreference='Stop'
$report=[System.Collections.Generic.List[object]]::new()
function Check([bool]$ok,[string]$name,$detail=''){ $report.Add([ordered]@{check=$name;passed=$ok;detail="$detail"}); Write-Output ("{0} {1} {2}" -f ($(if($ok){'PASS'}else{'FAIL'}),$name,$detail)) }
function Panel([string]$arguments,[int]$expect=0){
    $err=Join-Path $Root 'logs\panel-last-error.txt'; if(Test-Path $err){Remove-Item $err}
    $p=[Diagnostics.Process]::Start($Exe,("--root `"$Root`" "+$arguments));$p.WaitForExit()
    $script:lastError=if(Test-Path $err){[IO.File]::ReadAllText($err,[Text.Encoding]::UTF8)}else{''}
    Check ($p.ExitCode -eq $expect) ("panel "+$arguments) (($script:lastError -split "`n")[0])
}
function Code([string]$url){
    $req=[Net.HttpWebRequest]::Create($url);$req.AllowAutoRedirect=$false;$req.Timeout=8000
    try{ $res=$req.GetResponse() }catch [Net.WebException]{ $res=$_.Exception.Response; if(!$res){ return -1 } }
    try{ return [int]$res.StatusCode }finally{ $res.Close() }
}
function Listening([int]$port){ (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -ExpandProperty LocalAddress | Sort-Object -Unique) -join ',' }
# 重载期间旧 worker 可能还占着上一个地址，等它退出再判断。
function WaitListening([int]$port,[string]$expected){ foreach($i in 1..20){ $v=Listening $port; if($v -eq $expected){ return $v }; Start-Sleep -Milliseconds 300 }; return (Listening $port) }
$cfg=Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
$sitePort=$cfg.sites[0].httpPort; $dbPort=$cfg.dbManagerPort
$ip=(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.PrefixOrigin -ne 'WellKnown' } | Select-Object -First 1).IPAddress
Check ([bool]$ip) "found a LAN address: $ip"
$nginxConf=Join-Path $Root 'config\panel-nginx.conf'; $apacheConf=Join-Path $Root 'config\panel-apache.conf'

# 1. 默认只在本机
Panel '--service web --action start'
Panel '--service php8.2 --action start'
Panel '--service dbpage --action start'
Check ([IO.File]::ReadAllText($nginxConf) -match "listen 127\.0\.0\.1:$sitePort;") 'nginx listens on 127.0.0.1 by default'
$bound=WaitListening $sitePort '127.0.0.1'; Check ($bound -eq '127.0.0.1') 'site socket bound to 127.0.0.1' $bound
Check ((Code "http://127.0.0.1:$sitePort/") -eq 200) 'site reachable locally'
Check ((Code "http://${ip}:$sitePort/") -eq -1) 'site not reachable over the network yet'

# 2. 打开局域网访问
Panel '--lan-access on'
Check ([IO.File]::ReadAllText($nginxConf) -match "listen 0\.0\.0\.0:$sitePort;") 'nginx listens on 0.0.0.0 after the switch'
$bound=WaitListening $sitePort '0.0.0.0'; Check ($bound -eq '0.0.0.0') 'site socket bound to 0.0.0.0' $bound
Check ((Code "http://127.0.0.1:$sitePort/") -eq 200) 'site still reachable locally'
Check ((Code "http://${ip}:$sitePort/") -eq 200) 'site reachable over the network'

# 3. 数据库管理页面不对外
Check ([IO.File]::ReadAllText($nginxConf) -match "listen 127\.0\.0\.1:$dbPort;") 'db manager stays on 127.0.0.1 in the config'
$bound=WaitListening $dbPort '127.0.0.1'; Check ($bound -eq '127.0.0.1') 'db manager socket stays local' $bound
Check ((Code "http://127.0.0.1:$dbPort/") -ne -1) 'db manager reachable locally'
Check ((Code "http://${ip}:$dbPort/") -eq -1) 'db manager not reachable over the network'

# 4. Apache 同样
Panel '--web-server apache'
Start-Sleep 2
Check ([IO.File]::ReadAllText($apacheConf) -match "Listen 0\.0\.0\.0:$sitePort") 'apache listens on 0.0.0.0'
Check ([IO.File]::ReadAllText($apacheConf) -match "<VirtualHost 0\.0\.0\.0:$sitePort>") 'apache vhost bound to 0.0.0.0'
Check ([IO.File]::ReadAllText($apacheConf) -match "Listen 127\.0\.0\.1:$dbPort") 'apache keeps the db manager local'
Check ((Code "http://${ip}:$sitePort/") -eq 200) 'apache serves the site over the network'
Check ((Code "http://${ip}:$dbPort/") -eq -1) 'apache db manager not reachable over the network'

# 5. 关掉之后恢复
Panel '--lan-access off'
Start-Sleep 2
Check ([IO.File]::ReadAllText($apacheConf) -match "Listen 127\.0\.0\.1:$sitePort") 'apache back to 127.0.0.1'
Check ((Code "http://127.0.0.1:$sitePort/") -eq 200) 'site still reachable locally after turning it off'
Check ((Code "http://${ip}:$sitePort/") -eq -1) 'site no longer reachable over the network'
Panel '--web-server nginx'
Start-Sleep 2
$bound=WaitListening $sitePort '127.0.0.1'; Check ($bound -eq '127.0.0.1') 'nginx back to 127.0.0.1' $bound
Panel '--stop'

$out=Join-Path $PSScriptRoot 'verification-lan-access.json'
$report | ConvertTo-Json -Depth 4 | Set-Content $out -Encoding UTF8
"{0}/{1} passed" -f (@($report | Where-Object passed).Count),$report.Count
