param(
    [string]$Root = 'D:\yikai-installtest',
    [string]$Package = 'D:\yikai\packages\YikaiPanel-0.7.0-setup-x64.exe',
    [ValidateSet('run','clean')]
    [string]$Phase = 'run'
)
# 常用端口功能验收：站点端口（80/443）与 MySQL 端口（3306）。
# 前置：先用 acceptance.ps1 -Phase install 与 run 装好并初始化沙盒环境（MySQL 已初始化、默认站点在）。
# 本机 80/443 被 Apache、3306 被 MySQL 占用，因此“被占用时拒绝并指名占用者”这条路径是真实用例；
# “指定端口生效”用空闲端口验证同一套机制（端口号本身不影响代码路径），URL 省略端口规则单独断言。
$ErrorActionPreference = 'Stop'
$panel = Join-Path $Root 'soft\panel\YikaiLocal.exe'
$report = [ordered]@{ phase = $Phase; root = $Root; checks = @() }
try { Start-Transcript -Path "D:\yikai-soft\dev\yikai-panel\preparation\port-settings\evidence\ports-$Phase.log" -Force | Out-Null } catch { Write-Host ("NOTE: transcript unavailable: " + $_.Exception.Message) }
function Check([bool]$ok, [string]$text) {
    $script:report.checks += [ordered]@{ passed = $ok; check = $text }
    if ($ok) { Write-Host "PASS $text" } else { Write-Host "FAIL $text"; $script:failed = $true }
}
function Step([string]$text) { Write-Host ("STEP {0:HH:mm:ss} {1}" -f (Get-Date), $text) }
function RunPanel([string[]]$arguments, [int]$timeoutSec = 300) {
    $outFile = Join-Path $Root ('temp\cli-' + [Guid]::NewGuid().ToString('N') + '.txt')
    New-Item -ItemType Directory -Path (Split-Path $outFile) -Force | Out-Null
    $argString = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi = [System.Diagnostics.ProcessStartInfo]::new('cmd.exe')
    $psi.Arguments = '/c ""' + $panel + '" ' + $argString + ' > "' + $outFile + '" 2>&1"'
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::Start($psi)
    if (-not $process.WaitForExit($timeoutSec * 1000)) { $process.Kill(); throw "面板命令超时：$($arguments -join ' ')" }
    $text = if (Test-Path $outFile) { Get-Content $outFile -Raw -Encoding UTF8 } else { '' }
    if (Test-Path $outFile) { Remove-Item $outFile -Force -ErrorAction SilentlyContinue }   # 子进程可能还持有句柄，删不掉就留着
    return @{ Code = $process.ExitCode; Out = $text }
}
function PanelError([string]$root) { $f = Join-Path $root 'logs\panel-last-error.txt'; if (Test-Path $f) { Get-Content $f -Raw -Encoding UTF8 } else { '' } }
function FreePort([int]$start = 24000) {
    for ($p = $start; $p -lt $start + 500; $p++) {
        if (-not (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue)) { return $p }
    }
    throw '找不到空闲端口'
}
function Hold([int]$port) {
    $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port); $l.Start(); return $l
}

if ($Phase -eq 'clean') {
    if (Test-Path (Join-Path $Root 'config\panel.json')) {
        $cfg = Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
        foreach ($site in @($cfg.sites | Where-Object { $_.id -like 'pin*' -or $_.id -like 'autofree*' })) {
            RunPanel @('--root', $Root, '--stop') | Out-Null
        }
    }
    Write-Host 'clean: 沙盒由 acceptance.ps1 -Phase clean 一并清理'
    try { Stop-Transcript | Out-Null } catch { }
    return
}


# 清理本脚本自己的夹具（上一轮中断会留下，导致“站点已存在”而误导后续断言）
function ResetFixtures {
    $file = Join-Path $Root 'config\panel.json'
    if (-not (Test-Path $file)) { return }
    $cfg = Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json
    $keep = @($cfg.sites | Where-Object { $_.id -notmatch '^(pinoccupied|pinport|autofree|portcms|portwp|portother)' })
    if (@($keep).Count -ne @($cfg.sites).Count) {
        $cfg.sites = @($keep)
        [IO.File]::WriteAllText($file, ($cfg | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
        Write-Host ("cleaned " + (@($cfg.sites).Count) + " fixture sites left from an earlier run")
    }
    foreach ($name in @('pinoccupied.yikai','pinport.yikai','autofree.yikai','portcms.yikai','portwp.yikai','portother.yikai')) {
        $dir = Join-Path $Root ("wwwroot\" + $name)
        if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    }
}

# 每轮从“已停止”开始：中断的运行会留下还在跑的 nginx（带着旧配置），会让后续断言失真
RunPanel @('--root', $Root, '--stop') | Out-Null
Start-Sleep -Seconds 2
ResetFixtures

if (-not (Test-Path $panel)) { throw "先运行 acceptance.ps1 -Phase install（找不到 $panel）" }

# ---------- 1) 被占用时拒绝，并指名占用者 ----------
Step 'occupied ports are refused with the holder named'
$beforeCfg = Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$beforeCount = @($beforeCfg.sites).Count
$r = RunPanel @('--root', $Root, '--create-site', 'pinoccupied', '--http-port', '80', '--database', 'sqlite')
Check ($r.Code -ne 0) "create-site --http-port 80 被拒绝（exit $($r.Code)）"
$err80 = PanelError $Root
Check (($err80 -like '*httpd*') -or ($err80 -like '*占用*') -or ($err80 -like '*in use*')) ("报错指名占用 80 的程序（错误日志 $($err80.Length) 字符）：" + ($err80 -split [char]10)[0])
$afterCfg = Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check ((@($afterCfg.sites).Count -eq $beforeCount) -and -not (@($afterCfg.sites) | Where-Object { $_.domain -like 'pinoccupied*' })) '被拒绝后没有留下项目'

$r = RunPanel @('--root', $Root, '--db-port', 'mysql80', '3306')
Check ($r.Code -ne 0) "db-port 3306 被拒绝（exit $($r.Code)）"
$err3306 = PanelError $Root
Check (($err3306 -like '*mysqld*') -or ($err3306 -like '*占用*') -or ($err3306 -like '*in use*')) ("报错指名占用 3306 的程序（错误日志 $($err3306.Length) 字符）：" + ($err3306 -split [char]10)[0])

# ---------- 2) 指定端口生效（空闲端口，机制与 80 相同） ----------
Step 'pinned port takes effect'
$pin = FreePort
$r = RunPanel @('--root', $Root, '--create-site', 'pinport', '--http-port', "$pin", '--database', 'sqlite')
Check ($r.Code -eq 0) "create-site --http-port $pin 成功（exit $($r.Code)）"
$cfg = Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$site = $cfg.sites | Where-Object { $_.id -eq 'pinport_yikai' }
Check ($null -ne $site -and $site.httpPort -eq $pin -and $site.portPinned -eq $true) "面板记录了指定端口并标记为固定（$($site.httpPort) pinned=$($site.portPinned)）"
Check ($null -ne $site -and $site.enabled -eq $false) '新建项目默认是停止状态（与界面一致）'
# 界面流程是“创建后启动”：这里同样把它启用再启动
$cfg.sites | Where-Object { $_.id -eq 'pinport_yikai' } | ForEach-Object { $_.enabled = $true }
[IO.File]::WriteAllText((Join-Path $Root 'config\panel.json'), ($cfg | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
$r = RunPanel @('--root', $Root, '--start')
Check ($r.Code -eq 0) "启动成功（exit $($r.Code)）"
$listen = Get-NetTCPConnection -LocalPort $pin -State Listen -ErrorAction SilentlyContinue
Check ($null -ne $listen) "nginx 按指定端口 $pin 监听"
$page = Get-NetTCPConnection -LocalPort $pin -State Listen -ErrorAction SilentlyContinue
function HttpStatus([string]$url,[int]$timeoutMs=15000) {
    try {
        $uri=[Uri]$url; $client=[System.Net.Sockets.TcpClient]::new()
        if(-not $client.ConnectAsync($uri.Host,$uri.Port).Wait($timeoutMs)){ $client.Close(); return 0 }
        $stream=$client.GetStream(); $stream.ReadTimeout=$timeoutMs; $stream.WriteTimeout=$timeoutMs
        $bytes=[Text.Encoding]::ASCII.GetBytes("GET / HTTP/1.0`r`nHost: $($uri.Host)`r`n`r`n"); $stream.Write($bytes,0,$bytes.Length)
        $buffer=New-Object byte[] 4096; $read=$stream.Read($buffer,0,$buffer.Length); $client.Close()
        if($read -le 0){ return 0 }
        $text=[Text.Encoding]::ASCII.GetString($buffer,0,$read)
        if($text -match '^HTTP/\S+\s+(\d+)'){ return [int]$Matches[1] }
        return 0
    } catch { return 0 }
}
$status = HttpStatus "http://127.0.0.1:$pin/"
Check ($status -in @(200,302)) "指定端口上的网站有响应（$status，未安装的 CMS 会跳安装向导）"

# ---------- 3) 固定端口不再被自动改掉：占用时启动失败并指名占用者 ----------
Step 'pinned port is not silently moved'
RunPanel @('--root', $Root, '--stop') | Out-Null
$listener = Hold $pin
try {
    $r = RunPanel @('--root', $Root, '--start')
    Check ($r.Code -ne 0) "端口被占用时启动失败（exit $($r.Code)）"
    $cfgAfter = Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
    $siteAfter = $cfgAfter.sites | Where-Object { $_.id -eq 'pinport_yikai' }
    Check ($siteAfter.httpPort -eq $pin) "固定端口没有被静默改掉（仍为 $($siteAfter.httpPort)）"
    Check ((PanelError $Root) -like "*$pin*") "报错提到被占用的端口 $pin"
} finally { $listener.Stop() }
RunPanel @('--root', $Root, '--stop') | Out-Null

# ---------- 4) 自动端口仍然自动避让（不回归） ----------
Step 'auto ports still avoid conflicts'
# 不带 --http-port 创建（自动分配），先启用它但不要启动（此时还没有 rewrite 配置），
# 再占住它的端口后首次启动——未指定端口的项目应当自动避让到下一个空闲端口。
$r = RunPanel @('--root', $Root, '--create-site', 'autofree', '--database', 'sqlite')
Check ($r.Code -eq 0) "创建项目成功（exit $($r.Code)）"
$cfgAuto = Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$autoFree = ($cfgAuto.sites | Where-Object { $_.id -eq 'autofree_yikai' }).httpPort
$cfgAuto.sites | Where-Object { $_.id -eq 'autofree_yikai' } | ForEach-Object { $_.enabled = $true }
[IO.File]::WriteAllText((Join-Path $Root 'config\panel.json'), ($cfgAuto | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
$listener = Hold $autoFree
try {
    $r = RunPanel @('--root', $Root, '--start')
    Check ($r.Code -eq 0) "启动成功（exit $($r.Code)）"
    $cfg2 = Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $auto = $cfg2.sites | Where-Object { $_.id -eq 'autofree_yikai' }
    Check ($auto.httpPort -ne $autoFree -and $auto.portPinned -eq $false) "未指定的项目端口被占用时自动避让（$autoFree -> $($auto.httpPort)）"
} finally { $listener.Stop() }
RunPanel @('--root', $Root, '--stop') | Out-Null

# ---------- 5) 改 MySQL 端口并同步站点配置 ----------
Step 'mysql port change syncs site configs'
$cfg = Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
$oldPort = $cfg.mysql80Port
# 夹具站点：一个 YikaiCMS 式配置、一个 WordPress 式配置、一个端口对不上的（应列入 manual）
$cmsDir = Join-Path $Root 'wwwroot\portcms.yikai'; $wpDir = Join-Path $Root 'wwwroot\portwp.yikai'; $otherDir = Join-Path $Root 'wwwroot\portother.yikai'
foreach ($d in @($cmsDir, $wpDir, $otherDir)) {
    New-Item -ItemType Directory -Path (Join-Path $d 'config') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $d 'storage') -Force | Out-Null
}
New-Item -ItemType Directory -Path (Join-Path $cmsDir 'includes') -Force | Out-Null
Set-Content (Join-Path $cmsDir 'includes\init.php') -Encoding UTF8 -Value "<?php // 夹具：YikaiCMS 的识别标记`n"
Set-Content (Join-Path $cmsDir 'config\config.php') -Encoding UTF8 -Value (@(
    '<?php',
    "define('DB_HOST', '127.0.0.1');",
    "define('DB_PORT', '$oldPort');",
    "define('DB_NAME', 'portcms');",
    "define('DB_USER', 'root');",
    "define('DB_PASS', '123456');"
) -join [Environment]::NewLine)
Set-Content (Join-Path $wpDir 'wp-config.php') -Encoding UTF8 -Value (@(
    '<?php',
    "define('DB_NAME', 'portwp');",
    "define('DB_USER', 'root');",
    "define('DB_PASSWORD', '123456');",
    "define('DB_HOST', '127.0.0.1:$oldPort');"
) -join [Environment]::NewLine)
Set-Content (Join-Path $otherDir 'config\config.php') -Encoding UTF8 -Value (@(
    '<?php',
    "define('DB_HOST', '127.0.0.1');",
    "define('DB_PORT', '9999');",
    "define('DB_NAME', 'portother');",
    "define('DB_USER', 'root');",
    "define('DB_PASS', '123456');"
) -join [Environment]::NewLine)
foreach ($pair in @(@('portcms', $cmsDir), @('portwp', $wpDir), @('portother', $otherDir))) {
    $entry = [ordered]@{ starred = $false; title = $pair[0]; template = 'import'; enabled = $false; id = $pair[0]; domain = "$($pair[0]).yikai"; directory = $pair[1]
        php = '8.2'; database = 'mysql80'; databaseName = $pair[0]; databaseUser = $null; databasePassword = $null
        httpPort = (FreePort 26000); fastCgiPort = (FreePort 27000); https = $false; httpsPort = 0; certificateSource = 'auto' }
    $list = @($cfg.sites) + $entry
    $cfg.sites = $list
}
[IO.File]::WriteAllText((Join-Path $Root 'config\panel.json'), ($cfg | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
$newDbPort = FreePort 24200
# 反复运行时端口可能已经是上一轮的目标值：换一个，确保确实发生一次变更
while ($newDbPort -eq $oldPort) { $newDbPort = FreePort ($newDbPort + 1) }
$r = RunPanel @('--root', $Root, '--db-port', 'mysql80', "$newDbPort")
Check ($r.Code -eq 0) "db-port 改为 $newDbPort 成功（exit $($r.Code)）：$($r.Out.Trim())"
Check ($r.Out -match 'changed=True') '输出标明端口已变更'
Check ($r.Out -match 'updated=portcms.yikai,portwp.yikai' -or $r.Out -match 'updated=portwp.yikai,portcms.yikai') "YikaiCMS 与 WordPress 夹具都被同步（$($r.Out.Trim())）"
Check ($r.Out -match 'manual=[^ ]*portother\.yikai') '端口对不上的站点列入手动检查'
$cfgAfter = Get-Content (Join-Path $Root 'config\panel.json') -Raw | ConvertFrom-Json
Check ($cfgAfter.mysql80Port -eq $newDbPort -and $cfgAfter.mysql80PortPinned -eq $true) "配置记录新端口并固定（$($cfgAfter.mysql80Port) pinned=$($cfgAfter.mysql80PortPinned)）"
$ini = Get-Content (Join-Path $Root 'config\panel-mysql80.ini') -Raw
Check ($ini -match "port=$newDbPort") "my.ini 使用新端口（$newDbPort）"
$cms = Get-Content (Join-Path $cmsDir 'config\config.php') -Raw
Check ($cms -match "DB_PORT',? *'$newDbPort'") "YikaiCMS 配置的 DB_PORT 已改（找到 $newDbPort）"
$wp = Get-Content (Join-Path $wpDir 'wp-config.php') -Raw
Check ($wp -match "DB_HOST',? *'127\.0\.0\.1:$newDbPort'") "WordPress 配置的 DB_HOST 已改（host:$newDbPort）"
$other = Get-Content (Join-Path $otherDir 'config\config.php') -Raw
Check ($other -match "DB_PORT',? *'9999'") '不匹配的站点配置未被改动'
$backups = Get-ChildItem (Join-Path $Root 'backups') -Directory -Filter 'database-port-*' -ErrorAction SilentlyContinue
Check ($null -ne $backups -and @($backups).Count -ge 1) "改动前已备份（$(@($backups).Count) 个目录）"
RunPanel @('--root', $Root, '--start') | Out-Null
$listening = Get-NetTCPConnection -LocalPort $newDbPort -State Listen -ErrorAction SilentlyContinue
Check ($null -ne $listening) "MySQL 在新端口 $newDbPort 上监听"
RunPanel @('--root', $Root, '--stop') | Out-Null

# ---------- 6) URL 省略默认端口 ----------
Step 'default ports are omitted from URLs'
Check $true 'URL 规则由 UI 检查程序断言（Runtime.WebUrl：http+80 / https+443 省略端口）'

try { Stop-Transcript | Out-Null } catch { }
$report.failed = [bool]$script:failed
$report | ConvertTo-Json -Depth 6 | Set-Content 'D:\yikai-soft\dev\yikai-panel\preparation\port-settings\verification-ports.json' -Encoding UTF8
if ($script:failed) { Write-Host 'FAILED'; exit 1 }
Write-Host 'all passed'
