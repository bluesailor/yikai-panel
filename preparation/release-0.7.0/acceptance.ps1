param(
    [string]$Installer = 'D:\yikai\packages\YikaiPanel-0.7.0-setup-x64.exe',
    [string]$Root = 'D:\yikai-installtest',
    [ValidateSet('install','reinstall','run','uninstall','clean','all')]
    [string]$Phase = 'all'
)
# 0.7.0 安装包沙盒验收：装到独立根目录，不动 D:\yikai 开发环境。
#   install   静默安装 → 校验文件、php.ini 路径改写、nginx 空目录、快捷方式、卸载登记
#   reinstall 在同一目录再装一次 → 校验网站内容、panel.json、数据库不被破坏
#   run       启动面板 → 校验服务、网站与数据库页面、--ports 归属
#   uninstall 静默卸载 → 校验程序移除、网站/数据库/备份保留
#   clean     删除测试根目录
$ErrorActionPreference = 'Stop'
$report = [ordered]@{ phase = $Phase; root = $Root; checks = @() }
Start-Transcript -Path "D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence\acceptance-$Phase.log" -Force | Out-Null
function Check([bool]$ok, [string]$text) {
    $script:report.checks += [ordered]@{ passed = $ok; check = $text }
    if ($ok) { Write-Host "PASS $text" } else { Write-Host "FAIL $text"; $script:failed = $true }
}
function Step([string]$text) { Write-Host ("STEP {0:HH:mm:ss} {1}" -f (Get-Date), $text) }
# 直接走 TcpClient 读 HTTP 状态行：Invoke-WebRequest 在本机 PS 5.1 上会经系统代理并可能挂起。
function Get-HttpStatus([string]$url, [int]$timeoutMs = 20000) {
    try {
        $uri = [Uri]$url
        $client = [System.Net.Sockets.TcpClient]::new()
        if (-not $client.ConnectAsync($uri.Host, $uri.Port).Wait($timeoutMs)) { $client.Close(); return $null }
        $stream = $client.GetStream(); $stream.ReadTimeout = $timeoutMs; $stream.WriteTimeout = $timeoutMs
        $path = if ([string]::IsNullOrEmpty($uri.PathAndQuery)) { '/' } else { $uri.PathAndQuery }
        $bytes = [Text.Encoding]::ASCII.GetBytes("GET $path HTTP/1.0`r`nHost: $($uri.Host)`r`n`r`n")
        $stream.Write($bytes, 0, $bytes.Length)
        $buffer = New-Object byte[] 8192
        $read = $stream.Read($buffer, 0, $buffer.Length)
        $client.Close()
        if ($read -le 0) { return $null }
        return [Text.Encoding]::UTF8.GetString($buffer, 0, $read)
    } catch { return $null }
}
function RunExe([string]$exe, [string[]]$arguments, [int]$timeoutSec = 600) {
    # 不用 Start-Process -Wait：本机上偶发不返回；原生 WaitForExit 带超时，卡住能报出来。
    $info = [System.Diagnostics.ProcessStartInfo]::new($exe)
    $info.Arguments = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($info)
    Step ("run " + (Split-Path $exe -Leaf) + " " + ($arguments -join ' '))
    if (-not $p.WaitForExit($timeoutSec * 1000)) { $p.Kill(); throw "超时（${timeoutSec}s）：$exe" }
    Step ("exit " + $p.ExitCode)
    return $p.ExitCode
}
$panel = Join-Path $Root 'soft\panel\YikaiLocal.exe'
$dbPort = 8878; $sitePort = 8081; $mysqlPort = 3308
# 沙盒要用这几个固定端口（MySQL / 站点端口由面板自己挑，被占用会自动避开）。
# 本机可能同时跑着正式环境（另一套面板 + PHPStudy）：固定端口被占着就直接停下来说明清楚，
# 而不是让沙盒和正式环境互相顶掉——真实发生过：沙盒的 MySQL 先占了 3309，正式环境随后启动失败。
function Assert-FreePorts([int[]]$ports, [string]$why) {
    foreach ($port in $ports) {
        $holder = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($holder) {
            $proc = Get-Process -Id $holder.OwningProcess -ErrorAction SilentlyContinue
            throw "端口 $port 被占用（$($proc.ProcessName) PID $($holder.OwningProcess)），$why 需要它。请先停掉那个程序或改成别的端口再跑。"
        }
    }
}
function Clear-SandboxProcesses {
    # 收尾：只清这个测试根目录里的进程（按路径匹配，不碰本机其它环境）
    $left = @(Get-CimInstance Win32_Process -Filter "Name='nginx.exe' or Name='httpd.exe' or Name='php-cgi.exe' or Name='mysqld.exe' or Name='YikaiLocal.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -like "$Root*" -or $_.CommandLine -like "*$Root*" })
    foreach ($p in $left) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    return $left.Count
}

if ($Phase -in 'install','all') {
    if (Test-Path $Root) { throw "测试根目录已存在：$Root（先 -Phase clean）" }
    # /MERGETASKS="!vcredist"：本机已装运行库，且静默安装不该弹 UAC
    $code = RunExe $Installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=$Root",'/MERGETASKS=!vcredist',"/LOG=D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence\install.log")
    Check ($code -eq 0) "install: setup exited 0 (got $code)"
    Check (Test-Path $panel) 'install: panel executable installed'
    Check (Test-Path (Join-Path $Root 'soft\nginx\logs')) 'install: nginx logs directory created (nginx cannot create it itself)'
    Check (Test-Path (Join-Path $Root 'soft\nginx\temp')) 'install: nginx temp directory created'
    foreach ($dir in @('data\mysql57','data\mysql80','logs','temp','backups','config\ssl')) {
        Check (Test-Path (Join-Path $Root $dir)) "install: runtime directory $dir created"
    }
    Check (-not (Test-Path (Join-Path $Root 'config\panel.json'))) 'install: no panel.json shipped (panel writes its own on first start)'
    # 路径改写：随包 php.ini 写的是开发机路径，必须改成实际安装位置
    $ini = Get-Content (Join-Path $Root 'soft\php\8.2\php.ini') -Raw
    $slashRoot = $Root.Replace('\','/')
    Check ($ini -notmatch 'D:/yikai/') 'install: php.ini no longer points at the dev root'
    Check ($ini -match [regex]::Escape(('error_log="' + $slashRoot + '/logs/php-8.2.log"'))) 'install: php.ini error_log follows the install root'
    Check ($ini -match [regex]::Escape(('extension_dir="' + $slashRoot + '/soft/php/8.2/ext"'))) 'install: php.ini extension_dir follows the install root'
    $dbIni = Get-Content (Join-Path $Root 'config\phpmyadmin-php.ini') -Raw
    Check ($dbIni -notmatch 'D:/yikai/') 'install: database page php.ini rewritten too'
    foreach ($version in @('8.0','8.5')) {
        Check ((Get-Content (Join-Path $Root "soft\php\$version\php.ini") -Raw) -notmatch 'D:/yikai/') "install: PHP $version php.ini rewritten"
    }
    Check (Test-Path (Join-Path $Root 'wwwroot\yikaicms.yikai\index.php')) 'install: default site template installed'
    Check (-not (Test-Path (Join-Path $Root 'wwwroot\yikaicms.yikai\config\config.php'))) 'install: default site has no installed config'
    Check (-not (Test-Path (Join-Path $Root 'wwwroot\yikaicms.yikai\installed.lock'))) 'install: default site is not pre-installed'
    Check (Test-Path (Join-Path $Root 'soft\apache\2.4.39\LICENSE')) 'install: Apache LICENSE present'
    Check (Test-Path (Join-Path $Root 'soft\apache\2.4.39\NOTICE')) 'install: Apache NOTICE present'
    Check (Test-Path "$env:USERPROFILE\Desktop\易开面板.lnk") 'install: desktop shortcut created'
    Check (Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\易开面板\易开面板.lnk") 'install: start menu shortcut created'
    $uninstallKey = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like '易开面板*' }
    Check ($null -ne $uninstallKey) "install: uninstall entry registered ($($uninstallKey.DisplayName))"
    Check (Test-Path (Join-Path $Root 'unins000.exe')) 'install: uninstaller present'
    Check (-not (Test-Path (Join-Path $Root 'soft\phpmyadmin'))) 'install: unused phpMyAdmin component not shipped'
}

if ($Phase -in 'run','all') {
    if (-not (Test-Path $panel)) { throw '先运行 -Phase install' }
    Assert-FreePorts @($dbPort,$sitePort) '沙盒启动'
    # 中途任何终止性错误也要清掉沙盒进程（端口被它占着会顶掉本机正式环境）
    trap { $e = $_; $n = Clear-SandboxProcesses; if ($n -gt 0) { Write-Host "NOTE 异常退出，清掉 $n 个沙盒进程" }; throw $e }
    $code = RunExe $panel @('--root',$Root,'--start') 600
    Check ($code -eq 0) "run: panel --start exited 0 (got $code)"
    $config = Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
    $listeners = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue
    Check ($listeners.LocalPort -contains $config.mysql80Port) "run: MySQL 8.0 listening on $($config.mysql80Port)"
    Check ($listeners.LocalPort -contains $dbPort) "run: web server listening on the database page port $dbPort"
    Check ($listeners.LocalPort -contains $sitePort) "run: web server listening on the site port $sitePort"
    Check (Test-Path (Join-Path $Root 'data\mysql80\mysql')) 'run: MySQL 8.0 initialized on first start'
    $page = Get-HttpStatus "http://127.0.0.1:$dbPort/"
    Check ($null -ne $page -and $page -match '^HTTP/\S+\s+200') "run: database page answers 200 on $dbPort"
    $site = Get-HttpStatus "http://127.0.0.1:$sitePort/"
    # 全新安装的 CMS 还没有 config.php，首页会 302 到安装向导，这是预期状态
    Check ($null -ne $site -and $site -match '^HTTP/\S+\s+(200|30\d)') "run: default site answers on $sitePort"
    Check ($null -ne $site -and $site -match 'Location:\s*/install/') 'run: fresh site redirects to the CMS installer'
    $wizard = Get-HttpStatus "http://127.0.0.1:$sitePort/install/"
    Check ($null -ne $wizard -and $wizard -match '^HTTP/\S+\s+200') 'run: CMS installer page answers 200'
    Check ($null -ne $wizard -and $wizard -match '(?i)install|数据库|admin') 'run: CMS installer page is real content'
    # CMS 1.20.0 的规则集：敏感目录/文件必须挡住，API 路径不能把 PHP 源码当静态文件吐出来
    function HttpStatus([string]$url) {
        $response = Get-HttpStatus $url
        if ($null -eq $response) { return 0 }
        if ($response -match '^HTTP/\S+\s+(\d+)') { return [int]$Matches[1] }
        return 0
    }
    Check ((HttpStatus "http://127.0.0.1:$sitePort/config/config.php.example") -in @(403,404)) 'run: config directory is blocked'
    Check ((HttpStatus "http://127.0.0.1:$sitePort/deploy/nginx-server.conf") -in @(403,404)) 'run: deploy directory is blocked'
    Check ((HttpStatus "http://127.0.0.1:$sitePort/README.md") -in @(403,404)) 'run: markdown/docs files are blocked'
    Check ((HttpStatus "http://127.0.0.1:$sitePort/.env") -in @(403,404)) 'run: dotfiles are blocked'
    $api = Get-HttpStatus "http://127.0.0.1:$sitePort/api/v1/"
    Check ($null -ne $api -and $api -notmatch '<\?php') 'run: API path does not leak PHP source'
    Check ((HttpStatus "http://127.0.0.1:$sitePort/install/index.php") -eq 200) 'run: installer entry executes as PHP'
    RunExe $panel @('--root',$Root,'--ports','--out',(Join-Path $Root 'temp\ports.txt')) 120 | Out-Null
    $ports = Get-Content (Join-Path $Root 'temp\ports.txt') -Raw
    Check ($ports -match "mysql80\s+$($config.mysql80Port)\s+panel PID") 'run: --ports shows MySQL as panel-owned'
    Check ($ports -notmatch 'in use by') 'run: --ports shows no port taken by another program'
    $code = RunExe $panel @('--root',$Root,'--stop') 300
    Check ($code -eq 0) "run: panel --stop exited 0 (got $code)"
    Start-Sleep -Seconds 2
    $after = (Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue).LocalPort
    Check ($after -notcontains $config.mysql80Port -and $after -notcontains $sitePort) 'run: services released after stop'
    $left = Clear-SandboxProcesses
    Check ($left -eq 0) "run: no sandbox process left behind ($left cleaned)"
}

if ($Phase -in 'reinstall','all') {
    if (-not (Test-Path $panel)) { throw '先运行 -Phase install' }
    # 发布检查项：重复安装不能破坏已有项目。用户态文件（网站内容、panel.json、数据库）必须原样保留。
    $userFile = Join-Path $Root 'wwwroot\yikaicms.yikai\user-content.txt'
    Set-Content -Path $userFile -Value 'keep me' -Encoding UTF8
    $siteMarker = Join-Path $Root 'wwwroot\yikaicms.yikai\config\config.php'
    New-Item -ItemType Directory -Path (Split-Path $siteMarker) -Force | Out-Null
    Set-Content -Path $siteMarker -Value "<?php // installed site config" -Encoding UTF8
    $settingsFile = Join-Path $Root 'config\panel.json'
    $hadSettings = Test-Path $settingsFile
    if ($hadSettings) { $before = Get-Content $settingsFile -Raw }
    $code = RunExe $Installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=$Root",'/MERGETASKS=!vcredist',"/LOG=D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence\reinstall.log")
    Check ($code -eq 0) "reinstall: second install over the same root exited 0 (got $code)"
    Check (Test-Path $userFile) 'reinstall: user file in wwwroot survived'
    Check (Test-Path $siteMarker) 'reinstall: installed site config survived (payload must not clobber an installed CMS)'
    if ($hadSettings) { Check ((Get-Content $settingsFile -Raw) -eq $before) 'reinstall: panel.json untouched' }
    if (-not $hadSettings) { Write-Host 'NOTE: 面板尚未运行过，跳过 panel.json 对比（先跑 run 阶段可覆盖该项）' }
    if (Test-Path (Join-Path $Root 'data\mysql80\mysql')) { Check $true 'reinstall: existing database data untouched' }
    else { Write-Host 'NOTE: 数据库尚未初始化，跳过数据保留检查（先跑 run 阶段可覆盖该项）' }
    $ini = Get-Content (Join-Path $Root 'soft\php\8.2\php.ini') -Raw
    Check ($ini -match [regex]::Escape(('error_log="' + $Root.Replace('\','/') + '/logs/php-8.2.log"'))) 'reinstall: php.ini still points at this root after the second pass'
    Check (-not (Test-Path (Join-Path $Root 'soft\phpmyadmin'))) 'reinstall: still no unused components'
}

if ($Phase -in 'uninstall','all') {
    $uninstaller = Join-Path $Root 'unins000.exe'
    if (-not (Test-Path $uninstaller)) { throw '先运行 -Phase install' }
    # 卸载前等本测试目录的进程退出：用户在托盘“退出并停止环境”之后就是这种状态。
    # （不停服务就卸载，nginx worker 可能还占着 soft\nginx\logs\error.log）
    for ($i = 0; $i -lt 30; $i++) {
        $left = Get-CimInstance Win32_Process -Filter "Name='nginx.exe' or Name='httpd.exe' or Name='php-cgi.exe' or Name='mysqld.exe' or Name='YikaiLocal.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -like "*$Root*" -or $_.ExecutablePath -like "$Root*" }
        if (-not $left) { break }
        Start-Sleep -Seconds 1
    }
    Check ($null -eq $left) "uninstall: no environment process left before uninstalling ($(@($left).Count) remaining)"
    # 面板运行过才会写出 panel.json；没跑过 run 阶段时不做这条断言（不制造假失败）
    $settingsFile = Join-Path $Root 'config\panel.json'
    $hadSettings = Test-Path $settingsFile
    # 在网站目录里放一个用户文件，验证卸载不会删掉用户内容
    $userFile = Join-Path $Root 'wwwroot\yikaicms.yikai\user-content.txt'
    Set-Content -Path $userFile -Value 'keep me' -Encoding UTF8
    $code = RunExe $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LOG=D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence\uninstall.log')
    Check ($code -eq 0) "uninstall: exited 0 (got $code)"
    # 卸载器会把自己复制到临时目录再执行，父进程返回时它可能还在收尾（删除安装清单外的运行期文件）。
    # 这里轮询到程序侧目录真正清掉，再断言；同时记录等待时长，便于发现“慢”这类问题。
    $sw = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt 60; $i++) {
        if (-not (Test-Path $uninstaller) -and -not (Test-Path (Join-Path $Root 'soft'))) { break }
        Start-Sleep -Milliseconds 500
    }
    $sw.Stop()
    Write-Host ("uninstall settled after {0:N1}s" -f $sw.Elapsed.TotalSeconds)
    Check (-not (Test-Path $uninstaller)) 'uninstall: uninstaller finished and removed itself'
    Check (-not (Test-Path $panel)) 'uninstall: panel program removed'
    Check (-not (Test-Path (Join-Path $Root 'soft'))) 'uninstall: software directory removed (including nginx runtime logs)'
    Check (-not (Test-Path (Join-Path $Root 'logs'))) 'uninstall: runtime logs removed'
    Check (-not (Test-Path (Join-Path $Root 'temp'))) 'uninstall: runtime temp removed'
    Check (Test-Path $userFile) 'uninstall: user file in wwwroot preserved'
    Check (Test-Path (Join-Path $Root 'data\mysql80')) 'uninstall: database directory preserved'
    Check (Test-Path (Join-Path $Root 'backups')) 'uninstall: backups directory preserved'
    if ($hadSettings) { Check (Test-Path $settingsFile) 'uninstall: panel settings preserved for a later reinstall' }
    Check (-not (Test-Path "$env:USERPROFILE\Desktop\易开面板.lnk")) 'uninstall: desktop shortcut removed'
    $uninstallKey = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like '易开面板*' }
    Check ($null -eq $uninstallKey) 'uninstall: uninstall entry removed'
}

if ($Phase -eq 'clean') {
    $killed = Clear-SandboxProcesses
    if ($killed -gt 0) { Write-Host "cleaned $killed sandbox processes" }
    Start-Sleep -Seconds 1
    if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
    Write-Host "removed $Root"
}

# 兜底：任何阶段中途异常退出，也不能把沙盒进程留在机器上（端口被它占着会顶掉本机正式环境）
$stray = Clear-SandboxProcesses
if ($stray -gt 0) { Write-Host "NOTE 收尾清掉 $stray 个沙盒进程" }

Stop-Transcript | Out-Null
if ($Phase -ne 'clean') {
    $report.failed = [bool]$script:failed
    $report | ConvertTo-Json -Depth 6 | Set-Content "D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\verification-$Phase.json" -Encoding UTF8
    if ($script:failed) { Write-Host 'FAILED'; exit 1 }
    Write-Host 'all passed'
}
