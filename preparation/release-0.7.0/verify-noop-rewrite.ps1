param(
    [string]$Root = 'D:\yikai-noop-test',
    # Inno Setup 默认装在当前用户的 %LOCALAPPDATA%\Programs\Inno Setup 6（winget 安装位置）
    [string]$Iscc = (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    [string]$Source = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build\YikaiPanel',
    [ValidateSet('run','clean')]
    [string]$Phase = 'run'
)
# 专项验证：装到“随包路径本身”时不误报。
#
# 背景：随包 php.ini 里写的是开发机路径 D:/yikai/。装到默认目录 D:\yikai 时路径本来就对，
# 改写是空操作——但第一版自检把“文件里存在 D:/yikai/”当成残留，于是误报“路径没有改写成功”。
# 主验收装的是 D:\yikai-installtest（需要改写），覆盖不到这条路径，所以单独做一次。
#
# 做法：造一份负载，把里面的根路径换成测试根，再用变体负载编译一个安装器（输出名/目录都独立，
# 不覆盖正式产物），装到那个测试根——此时“随包路径 == 安装路径”，正好复现误报场景。
$ErrorActionPreference = 'Stop'
$build = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build'
$variant = Join-Path $build 'payload-noop'
$out = Join-Path $build 'out-noop'
$report = [ordered]@{ phase = $Phase; root = $Root; variant = $variant; checks = @() }
function Check([bool]$ok, [string]$text) {
    $script:report.checks += [ordered]@{ passed = $ok; check = $text }
    if ($ok) { Write-Host "PASS $text" } else { Write-Host "FAIL $text"; $script:failed = $true }
}
function Step([string]$text) { Write-Host ("STEP {0:HH:mm:ss} {1}" -f (Get-Date), $text) }

if ($Phase -eq 'clean') {
    if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
    foreach ($dir in @($variant,$out)) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
    Write-Host "removed $Root, $variant, $out"
    return
}

$slashRoot = $Root.Replace('\','/')
# 1) 变体负载：只放参与路径检查的文件，ini 里的根路径换成测试根（模拟“负载就是为这个目录做的”）
foreach ($dir in @('config', 'soft\php\8.0', 'soft\php\8.2', 'soft\php\8.5', 'wwwroot\yikaicms.yikai')) {
    New-Item -ItemType Directory -Path (Join-Path $variant $dir) -Force | Out-Null
}
Set-Content -Path (Join-Path $variant 'wwwroot\yikaicms.yikai\index.php') -Value "<?php // variant payload placeholder`n" -NoNewline
foreach ($pair in @(@('config\phpmyadmin-php.ini','config\phpmyadmin-php.ini'), @('soft\php\8.0\php.ini','soft\php\8.0\php.ini'),
                    @('soft\php\8.2\php.ini','soft\php\8.2\php.ini'), @('soft\php\8.5\php.ini','soft\php\8.5\php.ini'))) {
    $text = Get-Content (Join-Path $Source $pair[0]) -Raw
    Set-Content -Path (Join-Path $variant $pair[1]) -Value ($text -replace [regex]::Escape('D:/yikai/'), ($slashRoot + '/')) -NoNewline
}
Check (Select-String -Path (Join-Path $variant 'soft\php\8.2\php.ini') -Pattern ([regex]::Escape($slashRoot + '/')) -Quiet) "setup: variant payload points at $Root"

# 2) 变体安装器：同一份脚本，换负载与输出名，不碰正式产物
Step 'compile variant installer'
& $Iscc "/DPayload=$variant" "/DOutputDir=$out" "/DOutputBase=YikaiPanel-noop-check" (Join-Path 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\installer' 'yikai-panel.iss') | Out-Null
$setup = Join-Path $out 'YikaiPanel-noop-check.exe'
Check (Test-Path $setup) 'setup: variant installer compiled'

# 3) 静默装到测试根（正是随包路径）——误报会写进安装日志
if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
$log = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence\install-noop.log'
Step 'install variant'
$info = [System.Diagnostics.ProcessStartInfo]::new($setup)
$info.Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=$Root /MERGETASKS=!vcredist /LOG=$log"
$info.UseShellExecute = $false; $info.CreateNoWindow = $true
$process = [System.Diagnostics.Process]::Start($info)
if (-not $process.WaitForExit(600000)) { $process.Kill(); throw '安装超时' }
Check ($process.ExitCode -eq 0) "install: exited 0 (got $($process.ExitCode))"
$installLog = Get-Content $log -Raw
Check ($installLog -match 'PHP 配置路径自检通过') 'install: self-check reported success in the install log'
Check ($installLog -notmatch 'PHP 配置路径自检未通过') 'install: no false “path not rewritten” warning'

# 4) php.ini 内容与随包一致，且没有被加上 BOM
$installed = Join-Path $Root 'soft\php\8.2\php.ini'
Check (-not (Compare-Object (Get-Content (Join-Path $variant 'soft\php\8.2\php.ini')) (Get-Content $installed))) 'install: php.ini unchanged (path already correct, nothing to rewrite)'
$bytes = [IO.File]::ReadAllBytes($installed)
Check (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) 'install: php.ini has no UTF-8 BOM'
Check (Select-String -Path $installed -Pattern ([regex]::Escape($slashRoot + '/')) -Quiet) 'install: php.ini points at the install root'

# 5) 还原：卸掉测试安装
$uninstaller = Join-Path $Root 'unins000.exe'
if (Test-Path $uninstaller) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $uninstaller
    $psi.Arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $u = [System.Diagnostics.Process]::Start($psi)
    $u.WaitForExit(300000) | Out-Null
}
$report.failed = [bool]$script:failed
$report | ConvertTo-Json -Depth 6 | Set-Content 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\verification-noop.json' -Encoding UTF8
if ($script:failed) { Write-Host 'FAILED'; exit 1 }
Write-Host 'all passed'
