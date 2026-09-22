param(
    [string]$Root = 'D:\yikai\temp\port-qa\runtime-root',
    [string]$Exe = 'D:\yikai-soft\dev\yikai-panel\src\bin\Release\net9.0-windows\YikaiLocal.exe',
    [ValidateSet('setup','a','b','clean','all')]
    [string]$Phase = 'all'
)
# 端口占用运行时验证（隔离根目录，不动 D:\yikai 开发环境）：
#   setup  复制真实组件（PHP 8.2 / MySQL 8.0 / Nginx / db-manager），php.ini 路径改写到隔离根
#   a      外部进程占住 3308 后首次启动：MySQL 应自动换端口并完成初始化，整套服务可访问
#   b      数据已初始化后占住新端口再启动：应失败，报错指名占用进程并带 mysqld 日志尾部
#   clean  删除隔离根目录
$ErrorActionPreference = 'Stop'
$rootSlash = $Root.Replace('\','/')
$report = [ordered]@{ phase = $Phase; root = $Root; checks = @() }
Start-Transcript -Path "D:\yikai\temp\port-qa\transcript-$Phase.log" -Force | Out-Null
function Check([bool]$ok, [string]$text) {
    $script:report.checks += [ordered]@{ passed = $ok; check = $text }
    if ($ok) { Write-Host "PASS $text" } else { Write-Host "FAIL $text"; $script:failed = $true }
}
function Step([string]$text) { Write-Host ("STEP {0:HH:mm:ss} {1}" -f (Get-Date), $text) }
function Hold([int]$port) {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
    $listener.Start(); return $listener
}
function RunPanel([string[]]$arguments, [int]$timeoutSec = 300) {
    # 不用 Start-Process -Wait：在本机会偶发不返回；原生 WaitForExit 带超时，卡住时能报出是哪条命令。
    $info = [System.Diagnostics.ProcessStartInfo]::new($Exe)
    $info.Arguments = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($info)
    Step ("panel " + ($arguments -join ' '))
    if (-not $p.WaitForExit($timeoutSec * 1000)) { $p.Kill(); throw "panel 命令超时（${timeoutSec}s）：$($arguments -join ' ')" }
    Step ("panel exit " + $p.ExitCode)
    return $p.ExitCode
}
# 直接走 TcpClient 读 HTTP 状态行：Invoke-WebRequest 在本机 PS 5.1 上会经系统代理并可能挂起。
function Get-HttpStatus([string]$url, [int]$timeoutMs = 15000) {
    try {
        $uri = [Uri]$url
        $client = [System.Net.Sockets.TcpClient]::new()
        if (-not $client.ConnectAsync($uri.Host, $uri.Port).Wait($timeoutMs)) { $client.Close(); return $null }
        $stream = $client.GetStream(); $stream.ReadTimeout = $timeoutMs; $stream.WriteTimeout = $timeoutMs
        $path = if ([string]::IsNullOrEmpty($uri.PathAndQuery)) { '/' } else { $uri.PathAndQuery }
        $bytes = [Text.Encoding]::ASCII.GetBytes("GET $path HTTP/1.0`r`nHost: $($uri.Host)`r`n`r`n")
        $stream.Write($bytes, 0, $bytes.Length)
        $buffer = New-Object byte[] 4096
        $read = $stream.Read($buffer, 0, $buffer.Length)
        $client.Close()
        if ($read -le 0) { return $null }
        return [Text.Encoding]::ASCII.GetString($buffer, 0, $read)
    } catch { return $null }
}

if ($Phase -in 'setup','all') {
    if (Test-Path $Root) { throw "隔离根目录已存在：$Root（先运行 -Phase clean）" }
    foreach ($dir in @('soft','config\ssl','wwwroot\yikaicms.yikai','logs','temp')) { New-Item -ItemType Directory -Path (Join-Path $Root $dir) -Force | Out-Null }
    # nginx 不自建 logs/temp 目录（本机组件从发行包解压时自带）：隔离环境需手动补齐，安装包同理。
    foreach ($dir in @('soft\nginx\logs','soft\nginx\temp')) { New-Item -ItemType Directory -Path (Join-Path $Root $dir) -Force | Out-Null }
    foreach ($copy in @(@('soft\php\8.2', @('/E','/XD','dev')), @('soft\mysql\8.0', @('/E')), @('soft\nginx', @('/E','/XD','logs','temp')), @('soft\db-manager', @('/E')))) {
        robocopy "D:\yikai\$($copy[0])" (Join-Path $Root $copy[0]) @($copy[1]) /NJH /NJS /NDL /NFL | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy 失败：$($copy[0]) 代码 $LASTEXITCODE" }
    }
    foreach ($file in @('cacert.pem','mime.types','fastcgi_params','yikaicms-rewrite.conf')) { Copy-Item "D:\yikai\config\$file" (Join-Path $Root "config\$file") }
    # php.ini 与数据库页面 php.ini 是随包文件：验证“路径改写”与真实组件配合工作
    (Get-Content 'D:\yikai\soft\php\8.2\php.ini' -Raw).Replace('D:/yikai', $rootSlash) | Set-Content (Join-Path $Root 'soft\php\8.2\php.ini') -NoNewline
    (Get-Content 'D:\yikai\config\phpmyadmin-php.ini' -Raw).Replace('D:/yikai', $rootSlash) | Set-Content (Join-Path $Root 'config\phpmyadmin-php.ini') -NoNewline
    Check (-not (Test-Path (Join-Path $Root 'config\panel.json'))) 'setup: fresh root has no panel.json'
    Check ((Get-Content (Join-Path $Root 'soft\php\8.2\php.ini') -Raw).Contains($rootSlash)) 'setup: php.ini rewritten to the isolated root'
}

if ($Phase -in 'a','all') {
    $listener = Hold 3308
    try {
        $code = RunPanel(@('--root', $Root, '--start'))
        Check ($code -eq 0) "a: start succeeds while 3308 is held (exit $code)"
        $panel = Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
        Check ($panel.mysql80Port -ne 3308) "a: mysql80 moved off the occupied port (now $($panel.mysql80Port))"
        Check ($panel.mysql80Port -in 3309..3312) "a: mysql80 moved to the next free port ($($panel.mysql80Port))"
        Check ($panel.mysql57Port -eq 3307) "a: idle mysql57 keeps its default port"
        $mysql = Get-NetTCPConnection -LocalPort $panel.mysql80Port -State Listen -ErrorAction SilentlyContinue
        Check ($null -ne $mysql -and ($mysql | Select-Object -First 1).OwningProcess -in (Get-Process mysqld -ErrorAction SilentlyContinue).Id) 'a: the moved port is held by mysqld'
        Check (Test-Path (Join-Path $Root 'data\mysql80\mysql')) 'a: database initialized on first start'
        $page = Get-HttpStatus "http://127.0.0.1:$($panel.dbManagerPort)/"
        Check ($null -ne $page -and $page -match '^HTTP/\S+\s+200') "a: database page answers 200 on $($panel.dbManagerPort)"
        $site = Get-HttpStatus "http://127.0.0.1:$($panel.sites[0].httpPort)/"
        Check ($null -ne $site -and $site -match '^HTTP/') "a: site port answers on $($panel.sites[0].httpPort)"
        RunPanel(@('--root', $Root, '--ports', '--out', (Join-Path $Root 'temp\report-running.txt'))) | Out-Null
        $running = Get-Content (Join-Path $Root 'temp\report-running.txt') -Raw
        Check ($running -match "mysql80\s+$($panel.mysql80Port)\s+panel PID") 'a: --ports reports panel-owned mysql80'
    }
    finally { $listener.Stop() }
    $code = RunPanel(@('--root', $Root, '--stop'))
    Check ($code -eq 0) "a: stop exits cleanly (exit $code)"
    Start-Sleep -Seconds 2
    Check ($null -eq (Get-NetTCPConnection -LocalPort 3309 -State Listen -ErrorAction SilentlyContinue)) 'a: services released after stop'
}

if ($Phase -in 'b','all') {
    if (-not (Test-Path (Join-Path $Root 'data\mysql80\mysql'))) { throw '先运行 -Phase a 完成初始化' }
    $panel = Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
    $port = $panel.mysql80Port
    $listener = Hold $port
    try {
        $code = RunPanel(@('--root', $Root, '--start'))
        Check ($code -ne 0) "b: start fails when the initialized port $port is held (exit $code)"
        $error1 = Get-Content (Join-Path $Root 'logs\panel-last-error.txt') -Raw -ErrorAction SilentlyContinue
        Check ($null -ne $error1 -and $error1.Contains("Port $port is in use by")) "b: error names the occupied port ($port)"
        Check ($null -ne $error1 -and $error1.Contains('powershell.exe') -and $error1.Contains("PID $PID")) 'b: error names the holding process'
        $mysqlLog = Get-Content (Join-Path $Root 'logs\panel-mysql80.log') -Raw -ErrorAction SilentlyContinue
        Check ($null -ne $mysqlLog -and $mysqlLog.Length -gt 0) 'b: mysqld wrote its own log'
        Check ($null -ne $error1 -and $error1.Contains('Do you already have another mysqld server') ) "b: error carries the mysqld log tail"
    }
    finally { $listener.Stop() }
    RunPanel(@('--root', $Root, '--stop')) | Out-Null
    $panelAfter = Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
    Check ($panelAfter.mysql80Port -eq $port) "b: initialized port stays fixed ($($panelAfter.mysql80Port))"
}

if ($Phase -eq 'clean') {
    if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
    Write-Host "removed $Root"
}

if ($Phase -ne 'clean') {
    $out = 'D:\yikai-soft\dev\yikai-panel\preparation\port-qa\verification-runtime.json'
    $report.failed = [bool]$script:failed
    $report | ConvertTo-Json -Depth 6 | Set-Content $out
    if (Test-Path (Join-Path $Root 'logs\panel-last-error.txt')) { Copy-Item (Join-Path $Root 'logs\panel-last-error.txt') 'D:\yikai-soft\dev\yikai-panel\preparation\port-qa\evidence\panel-last-error-b.txt' -Force }
    Stop-Transcript | Out-Null
    if ($script:failed) { Write-Host 'FAILED'; exit 1 }
    Write-Host 'all passed'
}
else { Stop-Transcript | Out-Null }
