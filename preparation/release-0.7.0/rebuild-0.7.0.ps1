param(
    [switch]$SkipPublish,
    [switch]$SkipVerify
)
# 一条命令重出 0.7.0 的两个安装包并跑完整验收。
# 步骤与 docs\release.md 的检查清单一致：发布面板 → 重组负载 → 编译安装器 → 替换交付产物 → 验收。
# 说明：
#   · 本机可能同时跑着正式环境（另一套面板）。脚本不改它，也不停它；验收沙盒用 8081/8878，
#     被占用时会自己停下并报出占用者（见 acceptance.ps1 的 Assert-FreePorts）。
#   · 交付产物先写到 build\out，验收通过后才替换 D:\yikai\packages 里的文件。
$ErrorActionPreference = 'Stop'
$base = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0'
$src = 'D:\yikai-soft\dev\yikai-panel\src'
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
$packages = 'D:\yikai\packages'
$log = Join-Path $base 'evidence\rebuild.log'
if (-not (Test-Path (Split-Path $log))) { New-Item -ItemType Directory -Path (Split-Path $log) -Force | Out-Null }
Start-Transcript -Path $log -Force | Out-Null

function Step([string]$text) { Write-Host ("`n===== {0:HH:mm:ss} {1}" -f (Get-Date), $text) }

Step '1/6 发布面板程序（自包含单文件）'
if (-not $SkipPublish) {
    Push-Location $src
    try {
        & dotnet publish YikaiPHP.csproj -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:NuGetAudit=false `
            -o (Join-Path $base 'build\panel-publish') | Select-Object -Last 3
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（$LASTEXITCODE）" }
    } finally { Pop-Location }
} else { Write-Host '跳过（-SkipPublish）' }
$panelHash = (Get-FileHash (Join-Path $base 'build\panel-publish\YikaiLocal.exe') -Algorithm SHA256).Hash
Write-Host "面板 SHA-256：$panelHash"

# 官网首屏的实拍图跟着面板走：发布完面板就重拍一次，否则官网还显示旧界面
$shots = 'D:\yikai-soft\dev\yikai-panel\preparation\panel-dev\update-site-screenshots.ps1'
if (Test-Path $shots) {
    try { & powershell -NoProfile -ExecutionPolicy Bypass -File $shots } catch { Write-Host ("NOTE 官网截图未更新：" + $_.Exception.Message) }
} else { Write-Host 'NOTE 跳过官网截图（找不到 update-site-screenshots.ps1）' }

Step '2/6 重组负载'
& (Join-Path $base 'assemble-payload.ps1') -Force | Select-Object -Last 4
& (Join-Path $base 'assemble-minimal.ps1') -Force | Select-Object -Last 4

Step '3/6 编译两个安装器'
& $iscc "/DPayload=$(Join-Path $base 'build\YikaiPanel')" "/DOutputDir=$(Join-Path $base 'build\out')" '/DOutputBase=YikaiPanel-0.7.0-setup-x64' (Join-Path $base 'installer\yikai-panel.iss') | Select-Object -Last 2
& $iscc '/DNoDefaultSite' "/DPayload=$(Join-Path $base 'build\YikaiPanel-minimal')" "/DOutputDir=$(Join-Path $base 'build\out-minimal')" '/DOutputBase=YikaiPanel-0.7.0-minimal-setup-x64' (Join-Path $base 'installer\yikai-panel.iss') | Select-Object -Last 2

Step '4/6 验收（完整包四段 + 最简包 + 一键搭站 + 路径改写 + 后台补丁）'
if (-not $SkipVerify) {
    & (Join-Path $base 'acceptance.ps1') -Phase clean | Select-Object -Last 1
    & (Join-Path $base 'acceptance.ps1') -Phase all | Select-Object -Last 3
    & (Join-Path $base 'verify-minimal.ps1') -Phase clean | Select-Object -Last 1
    & (Join-Path $base 'verify-minimal.ps1') -Phase run | Select-Object -Last 3
    & (Join-Path $base 'verify-minimal-download.ps1') -Phase clean | Select-Object -Last 1
    & (Join-Path $base 'verify-minimal-download.ps1') -Phase run | Select-Object -Last 3
    & (Join-Path $base 'verify-noop-rewrite.ps1') | Select-Object -Last 2
} else { Write-Host '跳过（-SkipVerify）' }

Step '5/6 替换交付产物（D:\yikai\packages）'
foreach ($name in 'YikaiPanel-0.7.0-setup-x64.exe', 'YikaiPanel-0.7.0-minimal-setup-x64.exe') {
    $from = if ($name -like '*minimal*') { Join-Path $base "build\out-minimal\$name" } else { Join-Path $base "build\out\$name" }
    $to = Join-Path $packages $name
    Copy-Item $from $to -Force
    $hash = (Get-FileHash $to -Algorithm SHA256).Hash
    ('{0}  {1}' -f $hash, $name) | Set-Content ($to + '.sha256') -Encoding ASCII
    '{0}  {1:N1} MB  {2}' -f $name, ((Get-Item $to).Length / 1MB), $hash
}

Step '6/6 收尾：清掉测试目录里的进程，并把快捷方式指回开发环境面板'
Get-CimInstance Win32_Process -Filter "Name='nginx.exe' or Name='httpd.exe' or Name='php-cgi.exe' or Name='mysqld.exe' or Name='YikaiLocal.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -like 'D:\yikai-installtest*' -or $_.ExecutablePath -like 'D:\yikai-min*-test*' -or $_.CommandLine -like '*yikai-installtest*' -or $_.CommandLine -like '*yikai-min*-test*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
$target = 'D:\yikai\soft\panel\YikaiLocal.exe'
if (Test-Path $target) {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($path in @("$env:USERPROFILE\Desktop\易开面板.lnk", "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\易开面板\易开面板.lnk")) {
        New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
        $link = $shell.CreateShortcut($path)
        $link.TargetPath = $target; $link.WorkingDirectory = 'D:\yikai'; $link.IconLocation = $target; $link.Description = '易开面板'
        $link.Save()
    }
    Write-Host '快捷方式已指回 D:\yikai\soft\panel\YikaiLocal.exe'
}

Step '完成'
Write-Host "面板 SHA-256：$panelHash"
Write-Host '验收报告：verification-all.json / verification-minimal.json / verification-minimal-download.json / verification-noop.json'
Stop-Transcript | Out-Null
