param(
    [string]$Root = 'D:\yikai-min2-test',
    [string]$Installer = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build\out-minimal\YikaiPanel-0.7.0-minimal-setup-x64.exe',
    # 验收时用实际存在的版本化地址；正式的 -latest 别名由官网部署（见备注）
    [string]$CmsUrl = 'https://down.yikai.cn/soft/yikaicms/yikaicms-v1.20.0.zip',
    [ValidateSet('run','clean')]
    [string]$Phase = 'run'
)
# 最小包「一键搭好」验收：不带 CMS 模板与默认站点，
# 新建 YikaiCMS 项目时从配置的地址下载、解压并缓存，随后站点可访问。
$ErrorActionPreference = 'Stop'
$report = [ordered]@{ phase = $Phase; root = $Root; checks = @() }
try { Start-Transcript -Path 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence\minimal2.log' -Force | Out-Null } catch { }
function Check([bool]$ok, [string]$text) {
    $script:report.checks += [ordered]@{ passed = $ok; check = $text }
    if ($ok) { Write-Host "PASS $text" } else { Write-Host "FAIL $text"; $script:failed = $true }
}
function Step([string]$text) { Write-Host ("STEP {0:HH:mm:ss} {1}" -f (Get-Date), $text) }
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
function RunPanel([string[]]$arguments, [int]$timeoutSec = 900) {

    $psi = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $Root 'soft\panel\YikaiLocal.exe'))
    $psi.Arguments = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    # 不要重定向面板的输出：面板启动的 nginx / mysqld 会继承 stdout 句柄，
    # 用管道读取会一直等不到 EOF（脚本卡死的根因）；这里只取退出码。
    if (-not (Test-Path $psi.FileName)) { throw "找不到面板程序：$($psi.FileName)" }
    $p = [System.Diagnostics.Process]::Start($psi)
    if (-not $p.WaitForExit($timeoutSec * 1000)) { $p.Kill(); throw "命令超时：$($arguments -join ' ')" }
    return @{ Code = $p.ExitCode; Out = '' }
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
        if ([Text.Encoding]::ASCII.GetString($buffer, 0, $read) -match '^HTTP/\S+\s+(\d+)') { return [int]$Matches[1] }
        return 0
    } catch { return 0 }
}

if ($Phase -eq 'clean') {
    Get-CimInstance Win32_Process -Filter "Name='nginx.exe' or Name='php-cgi.exe' or Name='mysqld.exe' or Name='YikaiLocal.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -like "$Root*" -or $_.CommandLine -like "*$Root*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
    Write-Host "removed $Root"
    try { Stop-Transcript | Out-Null } catch { }
    return
}
if (Test-Path $Root) { throw "测试目录已存在：$Root（先 -Phase clean）" }
# 建项目那一步就会启动整套服务，所以端口要在安装之前先占住检查（早先放在启动前，结果服务已经起来了才报错，留下进程）
Assert-FreePorts @(8878,8081) '最小包沙盒启动'
# 中途任何终止性错误也要清掉沙盒进程：端口被它占着会顶掉本机正式环境
trap { $e = $_; $n = Clear-SandboxProcesses; if ($n -gt 0) { Write-Host "NOTE 异常退出，清掉 $n 个沙盒进程" }; throw $e }

Step 'silent install'
$psi = [System.Diagnostics.ProcessStartInfo]::new($Installer)
$psi.Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=$Root /LOG=D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence\minimal2-install.log"
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$proc = [System.Diagnostics.Process]::Start($psi)
$proc.WaitForExit(600000) | Out-Null
Check ($proc.ExitCode -eq 0) "安装退出码 0（实际 $($proc.ExitCode)）"

Step 'no bundled CMS, no default site'
Check (-not (Test-Path (Join-Path $Root 'soft\packages\yikaicms'))) '不含 CMS 模板'
Check (-not (Test-Path (Join-Path $Root 'wwwroot\yikaicms.yikai'))) '不含默认站点'
Check (Test-Path (Join-Path $Root 'wwwroot')) 'wwwroot 目录已建立'
Check (Test-Path (Join-Path $Root 'soft\php\8.5\php-cgi.exe')) 'PHP 8.5 已安装'
Check (Test-Path (Join-Path $Root 'soft\mysql\8.0\bin\mysqld.exe')) 'MySQL 8.0 已安装'

Step 'empty project list on first start'
$cfgFile = Join-Path $Root 'config\panel.json'
if (Test-Path $cfgFile) { Remove-Item $cfgFile -Force }
$result = RunPanel @('--root', $Root, '--ports')
$cfg = Get-Content $cfgFile -Raw -Encoding UTF8 | ConvertFrom-Json
Check (@($cfg.sites).Count -eq 0) "首次启动项目列表为空（实际 $(@($cfg.sites).Count) 个）"
Check ($cfg.phpDefault -eq '8.5') "默认 PHP 版本为 8.5（实际 $($cfg.phpDefault)）"

Step 'create a YikaiCMS project (downloads the CMS)'
$cfg.cmsPackageUrl = $CmsUrl
[IO.File]::WriteAllText($cfgFile, ($cfg | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
$result = RunPanel @('--root', $Root, '--create-site', 'demo', '--php', '8.5', '--database', 'mysql80', '--template', 'yikaicms', '--title', 'demo')
Check ($result.Code -eq 0) "创建项目成功（exit $($result.Code)：$($result.Out.Trim().Substring(0,[Math]::Min(120,$result.Out.Trim().Length)))）"
$siteDir = Join-Path $Root 'wwwroot\demo.yikai'
Check (Test-Path (Join-Path $siteDir 'config\version.php')) '项目目录里有 CMS（config/version.php 存在）'
Check (Test-Path (Join-Path $siteDir 'install\index.php')) 'CMS 安装向导存在'
$version = (Get-Content (Join-Path $siteDir 'config\version.php') -Raw) -replace '(?s).*CMS_VERSION''\s*,\s*''([0-9.]+)''.*', '$1'
Check ($version -match '^\d+\.\d+') "CMS 版本号可读（$version）"
$cached = Get-ChildItem (Join-Path $Root 'soft\cache') -Directory -ErrorAction SilentlyContinue
Check ($null -ne $cached -and @($cached).Count -ge 1) "CMS 已缓存（$(@($cached).Count) 个缓存目录）"
Check (-not (Test-Path (Join-Path $Root 'soft\packages\yikaicms'))) '没有把下载的 CMS 写回模板目录（保持最小包形态）'

Step 'site serves the CMS installer'
$result = RunPanel @('--root', $Root, '--start')
Check ($result.Code -eq 0) "启动成功（exit $($result.Code)）"
$cfg = Get-Content $cfgFile -Raw -Encoding UTF8 | ConvertFrom-Json
$site = @($cfg.sites)[0]
Check ($site.php -eq '8.5') "项目使用 PHP 8.5（实际 $($site.php)）"
$listening = $false
for ($i = 0; $i -lt 40 -and -not $listening; $i++) {
    Start-Sleep -Milliseconds 500
    $listening = $null -ne (Get-NetTCPConnection -LocalPort $site.httpPort -State Listen -ErrorAction SilentlyContinue)
}
Check $listening "网站在 $($site.httpPort) 监听"
Check (Test-Path (Join-Path $siteDir 'config\config.php')) '自动安装已写出 config/config.php'
Check (Test-Path (Join-Path $siteDir 'installed.lock')) '自动安装已写出 installed.lock'
$status = HttpStatus "http://127.0.0.1:$($site.httpPort)/"
Check ($status -eq 200) "网站首页直接返回 200（已安装，不再跳安装向导；实际 $status）"
$admin = HttpStatus "http://127.0.0.1:$($site.httpPort)/admin/"
Check ($admin -in @(200,302)) "后台入口有响应（$admin）"
Check ((HttpStatus "http://127.0.0.1:$($site.httpPort)/admin/login.php") -eq 200) '后台登录页返回 200'
Check ((HttpStatus "http://127.0.0.1:$($cfg.dbManagerPort)/") -eq 200) "数据库页面返回 200（$($cfg.dbManagerPort)）"
# session 目录 + CSRF：随包 php.ini 把 session.save_path 指到 <root>/temp/sessions-phpmyadmin，
# 目录不存在时 session 起不来、CSRF 每请求都变，备份/恢复会被挡成 403（曾经的真实故障）
Check (Test-Path (Join-Path $Root 'temp\sessions-phpmyadmin')) 'session 目录已创建（数据库页面能存住会话）'
$webSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$dbPage = Invoke-WebRequest -Uri "http://127.0.0.1:$($cfg.dbManagerPort)/" -UseBasicParsing -WebSession $webSession -TimeoutSec 20
$token = [regex]::Match($dbPage.Content, 'name="csrf" value="([0-9a-f]{48})"').Groups[1].Value
Check ($token.Length -eq 48) '数据库页面给出了 CSRF 令牌'
$backup = Invoke-WebRequest -Uri "http://127.0.0.1:$($cfg.dbManagerPort)/action.php?site=$($site.id)" -Method POST -UseBasicParsing -WebSession $webSession -Body @{ csrf = $token; action = 'backup' } -TimeoutSec 120
Check ($backup.Content -match '"ok":true') "数据库页面能真正执行备份（CSRF 与连接都正常）：$($backup.Content)"
$backupFiles = @(Get-ChildItem (Join-Path $Root "backups\databases\$($site.id)") -File -ErrorAction SilentlyContinue)
Check ($backupFiles.Count -ge 1) "备份文件已生成（$($backupFiles.Count) 个）"
RunPanel @('--root', $Root, '--stop') | Out-Null


    $stray = Clear-SandboxProcesses
    if ($stray -gt 0) { Write-Host "NOTE 收尾清掉 $stray 个沙盒进程" }
try { Stop-Transcript | Out-Null } catch { }
$report.failed = [bool]$script:failed
$report | ConvertTo-Json -Depth 6 | Set-Content 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\verification-minimal-download.json' -Encoding UTF8
if ($script:failed) { Write-Host 'FAILED'; exit 1 }
Write-Host ('all passed · ' + $report.checks.Count + ' 项')
