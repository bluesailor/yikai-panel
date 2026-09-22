param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Exe)
# 项目 HTTPS：本地根证书签发、Nginx / Apache、PHP 识别 HTTPS、自定义证书导入与校验、关闭、改域名重签。只用 $Root 的独立端口，不改系统信任列表。
$ErrorActionPreference='Stop'
$report=[System.Collections.Generic.List[object]]::new()
$utf8=New-Object Text.UTF8Encoding $false
function Check([bool]$ok,[string]$name,$detail=''){ $report.Add([ordered]@{check=$name;passed=$ok;detail="$detail"}); Write-Output ("{0} {1} {2}" -f ($(if($ok){'PASS'}else{'FAIL'}),$name,$detail)) }
function Panel([string]$arguments,[int]$expect=0){
    $err=Join-Path $Root 'logs\panel-last-error.txt'; if(Test-Path $err){Remove-Item $err}
    $p=[Diagnostics.Process]::Start($Exe,("--root `"$Root`" "+$arguments));$p.WaitForExit()
    $script:lastError=if(Test-Path $err){[IO.File]::ReadAllText($err)}else{''}
    Check ($p.ExitCode -eq $expect) ("panel "+$arguments) (($script:lastError -split "`n")[0])
}
function Config(){ Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
function Save($json){ $json | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Root 'config\panel.json') -Encoding UTF8 }
# curl 严格校验：只信任给定的 CA 文件，并按域名解析到 127.0.0.1
function Https([string]$domain,[int]$port,[string]$path,[string]$ca){
    $psi=New-Object Diagnostics.ProcessStartInfo 'curl.exe'; $psi.Arguments="-sS --max-time 20 --ssl-no-revoke --cacert `"$ca`" --resolve ${domain}:${port}:127.0.0.1 https://${domain}:${port}$path"; $psi.UseShellExecute=$false; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    $p=[Diagnostics.Process]::Start($psi); $o=$p.StandardOutput.ReadToEndAsync(); $e=$p.StandardError.ReadToEndAsync(); $p.WaitForExit()
    return [pscustomobject]@{Code=$p.ExitCode;Body=$o.Result.Trim();Err=$e.Result.Trim()}
}
function Listening([int]$port){ @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue).Count -gt 0 }

$ca=Join-Path $Root 'config\ssl\ca\yikai-local-root.crt'
Panel '--stop'
$json=Config; $json.webServer='nginx'; foreach($s in $json.sites){$s.enabled=$true;$s.https=$false;$s.certificateSource='auto'}; Save $json
$site=(Config).sites | Where-Object id -eq 'qaphp_yikai'
[IO.File]::WriteAllText((Join-Path $site.directory 'qa-https.php'),"<?php echo 'https=',(`$_SERVER['HTTPS']??'off'),';port=',`$_SERVER['SERVER_PORT']??'';",$utf8)

# 1. 开启 HTTPS（自动签发）后启动 Nginx
Panel '--https qaphp_yikai on'
$site=(Config).sites | Where-Object id -eq 'qaphp_yikai'
Check ($site.https -and $site.httpsPort -ge 8443 -and (Test-Path $ca)) 'https enabled with port and local root created' "port=$($site.httpsPort)"
Panel '--start'
$r=Https 'qaphp.yikai' $site.httpsPort '/qa-https.php' $ca
Check ($r.Code -eq 0 -and $r.Body -match 'https=on') 'nginx https verified against local root, PHP sees HTTPS' "$($r.Body) $($r.Err)"
$r=Https 'qaphp.yikai' $site.httpsPort '/' $ca
Check ($r.Body -match 'PHP project is ready') 'nginx https serves project'
$plain=try{(Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$($site.httpPort)/" -TimeoutSec 10).StatusCode}catch{-1}
Check ($plain -eq 200) 'plain http still works alongside https'
$cert=New-Object Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $Root 'config\ssl\sites\qaphp_yikai.crt')
Check ($cert.Subject -match 'qaphp.yikai' -and ($cert.NotAfter - (Get-Date)).TotalDays -gt 700 -and ($cert.NotAfter - (Get-Date)).TotalDays -le 825) 'site certificate subject and validity' "$($cert.Subject) until $($cert.NotAfter)"

# 2. Apache
Panel '--web-server apache'
$r=Https 'qaphp.yikai' $site.httpsPort '/qa-https.php' $ca
Check ($r.Code -eq 0 -and $r.Body -match 'https=on') 'apache https verified, PHP sees HTTPS' "$($r.Body) $($r.Err)"
Panel '--web-server nginx'

# 3. 自定义证书：用 openssl 生成自签证书导入；证书与私钥不匹配时拒绝
$openssl=Join-Path $Root 'soft\apache\2.4.39\bin\openssl.exe'; $work=Join-Path $Root 'temp\qa-custom-cert'; New-Item -ItemType Directory -Force $work | Out-Null
$env:OPENSSL_CONF=Join-Path $Root 'soft\apache\2.4.39\conf\openssl.cnf'
function OpenSsl([string]$arguments){ $psi=New-Object Diagnostics.ProcessStartInfo $openssl; $psi.Arguments=$arguments; $psi.UseShellExecute=$false; $psi.RedirectStandardError=$true; $psi.RedirectStandardOutput=$true; $p=[Diagnostics.Process]::Start($psi); $null=$p.StandardError.ReadToEndAsync(); $null=$p.StandardOutput.ReadToEndAsync(); $p.WaitForExit() }
OpenSsl "req -x509 -newkey rsa:2048 -nodes -keyout `"$work\custom.key`" -out `"$work\custom.crt`" -days 30 -subj /CN=qacms.yikai -addext subjectAltName=DNS:qacms.yikai"
OpenSsl "req -x509 -newkey rsa:2048 -nodes -keyout `"$work\other.key`" -out `"$work\other.crt`" -days 30 -subj /CN=other.yikai"
Check ((Test-Path "$work\custom.crt") -and (Test-Path "$work\other.key")) 'openssl generated test certificates'
Panel "--https qacms_yikai on --cert `"$work\custom.crt`" --key `"$work\other.key`"" 1
Check ($script:lastError -match 'match|不匹配|一致' -and !((Config).sites | Where-Object id -eq 'qacms_yikai').https) 'mismatched key rejected, https stays off' (($script:lastError -split "`n")[0])
Panel "--https qacms_yikai on --cert `"$work\custom.crt`" --key `"$work\custom.key`""
$cms=(Config).sites | Where-Object id -eq 'qacms_yikai'
Check ($cms.https -and $cms.certificateSource -eq 'custom' -and $cms.httpsPort -ne $site.httpsPort) 'custom certificate enabled on own port' "port=$($cms.httpsPort)"
$r=Https 'qacms.yikai' $cms.httpsPort '/' "$work\custom.crt"
Check ($r.Code -eq 0) 'custom certificate served (verified with custom cert)' $r.Err

# 4. 关闭 HTTPS：端口关闭，http 继续
Panel '--https qacms_yikai off'
Start-Sleep 2
Check (!(Listening $cms.httpsPort)) 'https port closed after disable'

# 5. 改域名后自动重签
$json=Config; ($json.sites | Where-Object id -eq 'qaphp_yikai').domain='qaphp2.yikai'; Save $json
Panel '--service web --action restart'
$cert2=New-Object Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $Root 'config\ssl\sites\qaphp_yikai.crt')
$r=Https 'qaphp2.yikai' $site.httpsPort '/' $ca
Check ($cert2.Subject -match 'qaphp2.yikai' -and $r.Code -eq 0) 'certificate re-issued for new domain' "$($cert2.Subject) $($r.Err)"

# 清理
Panel '--https qaphp_yikai off'
$json=Config; ($json.sites | Where-Object id -eq 'qaphp_yikai').domain='qaphp.yikai'; Save $json
Panel '--stop'
Remove-Item (Join-Path $site.directory 'qa-https.php'); Remove-Item -Recurse -Force $work
$failed=@($report | Where-Object { -not $_.passed }).Count
$report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot 'verification-https.json') -Encoding UTF8
Write-Output "FAILED=$failed TOTAL=$($report.Count)"
