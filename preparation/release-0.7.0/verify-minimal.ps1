param(
    [string]$Root = 'D:\yikai-min-test',
    [string]$Installer = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build\out-minimal\YikaiPanel-0.7.0-minimal-setup-x64.exe',
    [ValidateSet('run','clean')]
    [string]$Phase = 'run'
)
# 最小包验收：Nginx + PHP 8.5 + MySQL 8.0，不含 Apache / MySQL 5.7 / PHP 8.0 / 8.2 / 24 MB 运行库安装器。
# 关键验证点：
#   1) 装出来的目录里确实没有多余组件；
#   2) 面板在只有 PHP 8.5 的环境里能自愈默认版本（phpDefault、默认站点、数据库页面都用 8.5）；
#   3) VC 运行库由随包的 3 个 DLL 提供，且确实从程序目录加载（不是 System32）。
$ErrorActionPreference = 'Stop'
$report = [ordered]@{ phase = $Phase; root = $Root; checks = @() }
try { Start-Transcript -Path 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence\minimal.log' -Force | Out-Null } catch { }
function Check([bool]$ok, [string]$text) {
    $script:report.checks += [ordered]@{ passed = $ok; check = $text }
    if ($ok) { Write-Host "PASS $text" } else { Write-Host "FAIL $text"; $script:failed = $true }
}
function Step([string]$text) { Write-Host ("STEP {0:HH:mm:ss} {1}" -f (Get-Date), $text) }
function RunExe([string]$exe, [string[]]$arguments, [int]$timeoutSec = 600) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new($exe)
    $psi.Arguments = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    if (-not $p.WaitForExit($timeoutSec * 1000)) { $p.Kill(); throw "命令超时：$($arguments -join ' ')" }
    return $p.ExitCode
}
# 沙盒要用固定端口（站点 8081、数据库页面 8878；MySQL 端口由面板自己挑，被占用会自动避开）。
# 本机可能同时跑着正式环境：被占着就直接停下说明白，别让两边互相顶掉——真实发生过一次，
# 沙盒的 MySQL 先占了端口，正式环境启动失败。
function Assert-FreePorts([int[]]$ports, [string]$why) {
    foreach ($port in $ports) {
        $holder = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($holder) {
            $proc = Get-Process -Id $holder.OwningProcess -ErrorAction SilentlyContinue
            throw "端口 $port 被占用（$($proc.ProcessName) PID $($holder.OwningProcess)），$why 需要它。先停掉那个程序再跑。"
        }
    }
}
function Clear-SandboxProcesses {
    # 只清这个测试根目录里的进程（按路径和命令行匹配，不碰本机其它环境）
    $left = @(Get-CimInstance Win32_Process -Filter "Name='nginx.exe' or Name='httpd.exe' or Name='php-cgi.exe' or Name='mysqld.exe' or Name='YikaiLocal.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -like "$Root*" -or $_.CommandLine -like "*$Root*" })
    foreach ($p in $left) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    return $left.Count
}
function HttpStatus([string]$url, [int]$timeoutMs = 15000) {

    try {
        $uri = [Uri]$url; $client = [System.Net.Sockets.TcpClient]::new()
        if (-not $client.ConnectAsync($uri.Host, $uri.Port).Wait($timeoutMs)) { $client.Close(); return 0 }
        $stream = $client.GetStream(); $stream.ReadTimeout = $timeoutMs; $stream.WriteTimeout = $timeoutMs
        $path = if ([string]::IsNullOrEmpty($uri.PathAndQuery)) { '/' } else { $uri.PathAndQuery }
        $bytes = [Text.Encoding]::ASCII.GetBytes("GET $path HTTP/1.0`r`nHost: $($uri.Host)`r`n`r`n")
        $stream.Write($bytes, 0, $bytes.Length)
        $buffer = New-Object byte[] 4096; $read = $stream.Read($buffer, 0, $buffer.Length); $client.Close()
        if ($read -le 0) { return 0 }
        $text = [Text.Encoding]::ASCII.GetString($buffer, 0, $read)
        if ($text -match '^HTTP/\S+\s+(\d+)') { return [int]$Matches[1] }
        return 0
    } catch { return 0 }
}

if ($Phase -eq 'clean') {
    # 只结束测试目录里的进程：按进程名全杀会误伤本机其它环境（例如 PHPStudy 的 MySQL）
    Get-CimInstance Win32_Process -Filter "Name='nginx.exe' or Name='php-cgi.exe' or Name='mysqld.exe' or Name='YikaiLocal.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -like "$Root*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
    Write-Host "removed $Root"
    try { Stop-Transcript | Out-Null } catch { }
    return
}

$panel = Join-Path $Root 'soft\panel\YikaiLocal.exe'
if (Test-Path $Root) { throw "测试目录已存在：$Root（先 -Phase clean）" }
# 端口要在安装/启动之前先检查：占着就直接停下说明白，别让沙盒和本机正式环境互相顶掉
Assert-FreePorts @(8878,8081) '最小包沙盒启动'
# 中途任何终止性错误也要清掉沙盒进程（端口被它占着会顶掉本机正式环境）
trap { $e = $_; $n = Clear-SandboxProcesses; if ($n -gt 0) { Write-Host "NOTE 异常退出，清掉 $n 个沙盒进程" }; throw $e }

Step 'silent install'
$code = RunExe $Installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=$Root",'/MERGETASKS=!vcredist',"/LOG=D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence\minimal-install.log")
Check ($code -eq 0) "安装退出码 0（实际 $code）"

Step 'installed components'
Check (Test-Path $panel) '面板程序已安装'
Check (Test-Path (Join-Path $Root 'soft\php\8.5\php-cgi.exe')) 'PHP 8.5 已安装'
Check (Test-Path (Join-Path $Root 'soft\mysql\8.0\bin\mysqld.exe')) 'MySQL 8.0 已安装'
Check (Test-Path (Join-Path $Root 'soft\nginx\nginx.exe')) 'Nginx 已安装'
foreach ($absent in @('soft\apache','soft\php\8.0','soft\php\8.2','soft\mysql\5.7','soft\phpmyadmin','soft\prerequisites')) {
    Check (-not (Test-Path (Join-Path $Root $absent))) "不含 $absent"
}
Check (-not (Get-ChildItem $Root -Recurse -Filter 'vc_redist*.exe' -ErrorAction SilentlyContinue)) '不含 24 MB 的 VC++ 运行库安装器'
Check (Test-Path (Join-Path $Root 'soft\nginx\logs')) 'nginx logs 目录已建立'

Step 'bundled VC runtime DLLs'
foreach ($dll in @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll')) {
    Check (Test-Path (Join-Path $Root "soft\php\8.5\$dll")) "PHP 目录随包 $dll"
}
$dbIni = Get-Content (Join-Path $Root 'config\phpmyadmin-php.ini') -Raw
Check ($dbIni -match 'soft/php/8\.5/ext') '数据库页面 php.ini 指向 PHP 8.5'

Step 'PHP loads extensions with bundled runtime'
$php = Join-Path $Root 'soft\php\8.5\php.exe'
# php.exe 在 stderr 上的提示会被 $ErrorActionPreference=Stop 变成终止错误，这里单独放宽
$previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
$phpOut = (& $php -c (Join-Path $Root 'soft\php\8.5\php.ini') -m 2>&1 | Out-String)
$ErrorActionPreference = $previous
Check ($phpOut -notmatch 'is not compatible with this PHP build') 'PHP 未抱怨运行库版本不匹配（说明随包 DLL 版本够新）'
$loaded = ($phpOut -split "`r?`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
foreach ($ext in @('mysqli','pdo_mysql','intl','mbstring','curl','openssl','zip')) {
    Check ($loaded -contains $ext) "PHP 扩展 $ext 已加载"
}

Step 'runtime DLLs come from the program folder, not System32'
$psi = [System.Diagnostics.ProcessStartInfo]::new($php)
$psi.Arguments = '-c "' + (Join-Path $Root 'soft\php\8.5\php.ini') + '" -r "sleep(6);"'
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$proc = [System.Diagnostics.Process]::Start($psi)
Start-Sleep -Milliseconds 1500
$loaded = (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue).Modules |
    Where-Object { $_.ModuleName -match '^(vcruntime140|vcruntime140_1|msvcp140)\.dll$' } |
    Select-Object ModuleName, FileName
$proc.WaitForExit(15000) | Out-Null
foreach ($module in $loaded) {
    $expected = Join-Path $Root 'soft\php\8.5'
    Check ($module.FileName -like "$expected*") ("$($module.ModuleName) 从程序目录加载：" + $module.FileName)
}

Step 'panel starts with only PHP 8.5'
$code = RunExe $panel @('--root',$Root,'--start') 600
Check ($code -eq 0) "面板 --start 退出码 0（实际 $code）"
$cfg = Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($cfg.phpDefault -eq '8.5') "默认 PHP 版本自愈为 8.5（实际 $($cfg.phpDefault)）"
# 最小包不带默认站点：第一次启动后面板里应该是空的，项目由用户自己新建（新建 YikaiCMS 时在线取模板）
Check (@($cfg.sites).Count -eq 0) "最小包不带默认站点（启动后项目数 $(@($cfg.sites).Count)）"
$listen = (Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue).LocalPort
Check ($listen -contains $cfg.mysql80Port) "MySQL 8.0 在 $($cfg.mysql80Port) 监听"
Check ($listen -contains $cfg.dbManagerPort) "数据库页面在 $($cfg.dbManagerPort) 监听"
Check ($listen -contains $cfg.dbFastCgiPort) "数据库页面的 PHP-CGI 在 $($cfg.dbFastCgiPort) 监听"
Check ((Test-Path (Join-Path $Root 'data\mysql80\mysql'))) 'MySQL 8.0 首次启动完成初始化'
# PHP-CGI 刚起来时前几次请求可能还没应答，重试到出结果
$dbStatus = 0
for ($i = 0; $i -lt 30 -and $dbStatus -ne 200; $i++) {
    $dbStatus = HttpStatus "http://127.0.0.1:$($cfg.dbManagerPort)/"
    if ($dbStatus -ne 200) { Start-Sleep -Milliseconds 700 }
}
Check ($dbStatus -eq 200) "数据库页面返回 200（由 PHP 8.5 提供；实际 $dbStatus）"
# 没有项目时数据库页面也要给出说明页，不能 500（最小包首次安装就是这个状态）
Check (Test-Path (Join-Path $Root 'temp\sessions-phpmyadmin')) 'session 目录已创建（数据库页面能存住会话）'
$dbPage = Invoke-WebRequest -Uri "http://127.0.0.1:$($cfg.dbManagerPort)/" -UseBasicParsing -TimeoutSec 20
Check ($dbPage.Content -match '还没有项目|No projects yet|プロジェクトがありません') '数据库页面显示“还没有项目”的说明而不是报错页'

$code = RunExe $panel @('--root',$Root,'--stop') 300
Check ($code -eq 0) '面板 --stop 退出码 0'
Start-Sleep -Seconds 2
$after = (Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue).LocalPort
Check ($after -notcontains $cfg.dbManagerPort -and $after -notcontains $cfg.mysql80Port) '停止后端口已释放'

$stray = Clear-SandboxProcesses
if ($stray -gt 0) { Write-Host "NOTE 收尾清掉 $stray 个沙盒进程" }
try { Stop-Transcript | Out-Null } catch { }
$report.failed = [bool]$script:failed
New-Item -ItemType Directory -Path 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0' -Force | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\verification-minimal.json' -Encoding UTF8
if ($script:failed) { Write-Host 'FAILED'; exit 1 }
Write-Host ('all passed · ' + $report.checks.Count + ' 项')
