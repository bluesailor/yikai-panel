param(
    [string]$Installer = 'D:\yikai\packages\YikaiPanel-0.8.5-setup-x64.exe',
    [string]$Root = 'D:\yikai-setup-test',
    [ValidateSet('run','clean')]
    [string]$Phase = 'run'
)
# 安装时填写“默认网站”信息的端到端验证（完整包、最简包都用它）：
#   静默安装并带 /SITENAME= /ADMINUSER= /ADMINPASS= → 安装器写 config\setup-site.txt
#   → 面板 --start 读入、启动环境、装好默认网站（最简包先联网获取 CMS）
#   → 核对 panel.json、installed.lock、数据库里的管理员密码与网站名称。
$ErrorActionPreference = 'Stop'
$siteName = '易开测试站'
$adminUser = 'tester01'
$adminPass = 'Test#2026x'
$variant = if ((Split-Path $Installer -Leaf) -like '*minimal*') { 'minimal' } else { 'full' }
$report = [ordered]@{ installer = (Split-Path $Installer -Leaf); root = $Root; checks = @() }
function Check([bool]$ok, [string]$text) {
    $script:report.checks += [ordered]@{ passed = $ok; check = $text }
    if ($ok) { Write-Host "PASS $text" } else { Write-Host "FAIL $text"; $script:failed = $true }
}
function RunExe([string]$exe, [string[]]$arguments, [int]$timeoutSec = 600) {
    # 输出写进临时文件再读：面板 --start 拉起的 mysqld / nginx 会继承输出管道，
    # 直接重定向到管道时读到 EOF 要等这些服务退出，脚本会一直卡住。
    $outFile = Join-Path $env:TEMP ('yk-setup-out-' + [guid]::NewGuid().ToString('N') + '.txt')
    $line = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $info = [System.Diagnostics.ProcessStartInfo]::new($env:ComSpec)
    $info.Arguments = '/d /s /c ""' + $exe + '" ' + $line + ' > "' + $outFile + '" 2>&1"'
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($info)
    if (-not $p.WaitForExit($timeoutSec * 1000)) { $p.Kill(); throw "超时（${timeoutSec}s）：$exe" }
    # 服务进程还拿着这个文件的写句柄：按 ReadWrite 共享方式打开才读得到
    $text = ''
    if (Test-Path $outFile) {
        $stream = [IO.FileStream]::new($outFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        try { $text = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8).ReadToEnd() } finally { $stream.Dispose() }
    }
    Remove-Item $outFile -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{ Code = $p.ExitCode; Output = $text }
}
function Clear-SandboxProcesses {
    $left = @(Get-CimInstance Win32_Process -Filter "Name='nginx.exe' or Name='httpd.exe' or Name='php-cgi.exe' or Name='mysqld.exe' or Name='YikaiLocal.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -like "$Root*" -or $_.CommandLine -like "*$Root*" })
    foreach ($p in $left) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    return $left.Count
}
if ($Phase -eq 'clean') {
    Clear-SandboxProcesses | Out-Null
    Start-Sleep -Seconds 1
    if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
    Write-Host "removed $Root"
    exit 0
}
if (Test-Path $Root) { throw "测试根目录已存在：$Root（先 -Phase clean）" }
foreach ($port in 8081, 8878) {
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw "端口 $port 被占用，沙盒需要它" }
}
$panel = Join-Path $Root 'soft\panel\YikaiLocal.exe'
try {
    $install = RunExe $Installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=$Root",'/MERGETASKS=!vcredist,!desktopicon',"/SITENAME=$siteName","/ADMINUSER=$adminUser","/ADMINPASS=$adminPass")
    Check ($install.Code -eq 0) "install: setup exited 0 (got $($install.Code))"
    $setupFile = Join-Path $Root 'config\setup-site.txt'
    Check (Test-Path $setupFile) 'install: setup-site.txt written for the first start'
    if (Test-Path $setupFile) {
        $text = [IO.File]::ReadAllText($setupFile, [Text.Encoding]::UTF8)
        Check ($text -match "siteName=$siteName" -and $text -match "adminUser=$adminUser") 'install: setup-site.txt carries the site name and admin user (UTF-8)'
    }
    $start = RunExe $panel @('--root',$Root,'--start') 900
    Write-Host ($start.Output.Trim() -split "`n" | Select-Object -Last 3 | Out-String)
    Check ($start.Code -eq 0) "start: panel --start exited 0 (got $($start.Code))"
    Check ($start.Output -match 'setup-site=ok') 'start: default site installed on first start'
    Check (-not (Test-Path $setupFile)) 'start: setup-site.txt removed after it was read'
    $config = Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Check ($config.cmsAdminUser -eq $adminUser) 'start: panel.json keeps the admin user (used by reset admin)'
    Check (-not $config.setupInstallPending) 'start: setup is not retried on the next start'
    $site = @($config.sites) | Where-Object { $_.domain -eq 'yikaicms.localhost' } | Select-Object -First 1
    Check ($null -ne $site) "start: default site yikaicms.localhost registered ($variant)"
    Check ($site.title -eq $siteName) 'start: default site title is the site name from setup'
    $siteDir = Join-Path $Root 'wwwroot\yikaicms.localhost'
    Check (Test-Path (Join-Path $siteDir 'installed.lock')) 'start: CMS installed.lock written'
    # 用沙盒自己的 PHP 直接查库：管理员密码能对上、网站名称已写进 CMS 设置
    $probe = Join-Path $Root 'temp\setup-probe.php'
    @'
<?php
define('ROOT_PATH', $argv[1]);
require $argv[1] . '/config/config.php';
$dsn = DB_DRIVER === 'sqlite' ? 'sqlite:' . DB_PATH : 'mysql:host=' . DB_HOST . ';port=' . DB_PORT . ';dbname=' . DB_NAME . ';charset=utf8mb4';
$pdo = DB_DRIVER === 'sqlite' ? new PDO($dsn) : new PDO($dsn, DB_USER, DB_PASS);
$user = $pdo->prepare('SELECT password FROM ' . DB_PREFIX . 'users WHERE username = ?');
$user->execute([$argv[2]]);
$hash = $user->fetchColumn();
$name = $pdo->query('SELECT value FROM ' . DB_PREFIX . "settings WHERE `key` = 'site_name'")->fetchColumn();
echo json_encode(['login' => $hash !== false && password_verify($argv[3], $hash), 'siteName' => $name], JSON_UNESCAPED_UNICODE);
'@ | Set-Content $probe -Encoding UTF8
    $php = Join-Path $Root "soft\php\$($site.php)\php.exe"
    # 不带 -n：php.exe 读同目录的 php.ini（安装器已改写扩展目录），pdo_mysql 等扩展照常加载
    $result = RunExe $php @($probe,$siteDir,$adminUser,$adminPass) 60
    try { $answer = $result.Output | ConvertFrom-Json } catch { Write-Host "probe output: $($result.Output)"; $answer = [pscustomobject]@{ login = $false; siteName = '' } }
    Check ($answer.login -eq $true) 'cms: admin user logs in with the password from setup'
    Check ($answer.siteName -eq $siteName) "cms: site_name setting is the setup site name (got $($answer.siteName))"
    $stop = RunExe $panel @('--root',$Root,'--stop') 300
    Check ($stop.Code -eq 0) "stop: panel --stop exited 0 (got $($stop.Code))"
    # 静默卸载：去掉这次沙盒安装写进注册表的卸载登记与开始菜单快捷方式（网站与数据库按默认保留，clean 再删目录）
    $uninstall = RunExe (Join-Path $Root 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') 300
    Check ($uninstall.Code -eq 0) "uninstall: sandbox uninstalled (got $($uninstall.Code))"
    # 卸载器把自己复制到临时目录再执行：等它真正收尾，免得 clean 和它抢着删目录
    # 删除中的文件会让 Test-Path 报“拒绝访问”：当作还在，继续等
    $uninstaller = Join-Path $Root 'unins000.exe'
    for ($i = 0; $i -lt 120; $i++) {
        $present = try { Test-Path -LiteralPath $uninstaller -ErrorAction Stop } catch { $true }
        if (-not $present) { break }
        Start-Sleep -Milliseconds 500
    }
}
finally {
    $left = Clear-SandboxProcesses
    if ($left -gt 0) { Write-Host "NOTE 清掉 $left 个沙盒进程" }
}
$report.failed = [bool]$script:failed
$report | ConvertTo-Json -Depth 6 | Set-Content "D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\verification-setup-site-$variant.json" -Encoding UTF8
if ($script:failed) { Write-Host 'FAILED'; exit 1 }
Write-Host "all passed · $($report.checks.Count) 项"
