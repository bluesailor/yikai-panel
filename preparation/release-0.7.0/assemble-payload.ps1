param(
    [string]$Build = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build\YikaiPanel',
    [switch]$Force
)
# 组装 0.7.0 完整环境包的负载树。只放客户需要的东西：
#   排除开发状态（panel.json、生成的 panel-*.conf/ini、data、logs、temp、backups、SSL 证书）、
#   旧构建 EXE、以及面板已不再使用的 phpMyAdmin（数据库页面改用 db-manager + Adminer）。
$ErrorActionPreference = 'Stop'
if (Test-Path $Build) {
    if (-not $Force) { throw "负载树已存在：$Build（加 -Force 重来）" }
    Remove-Item $Build -Recurse -Force
}
$payload = Join-Path $Build 'soft'
foreach ($dir in @('soft','config','wwwroot')) { New-Item -ItemType Directory -Path (Join-Path $Build $dir) -Force | Out-Null }

function Copy-Tree([string]$source, [string]$target, [string[]]$excludeDirs = @(), [string[]]$excludeFiles = @()) {
    $args = @($source, $target, '/E', '/NJH', '/NJS', '/NDL', '/NFL', '/NP', '/R:1', '/W:1')
    if ($excludeDirs.Count -gt 0) { $args += '/XD'; $args += $excludeDirs }
    if ($excludeFiles.Count -gt 0) { $args += '/XF'; $args += $excludeFiles }
    robocopy @args | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy 失败（$LASTEXITCODE）：$source" }
}

# 发布产物放在独立的 panel-publish 目录：负载树每次整棵重建时不会连带删掉发布结果。
$publish = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build\panel-publish'
if (-not (Test-Path (Join-Path $publish 'YikaiLocal.exe'))) { throw "缺少 $publish\YikaiLocal.exe（先运行 dotnet publish -o ...）" }
New-Item -ItemType Directory -Path (Join-Path $Build 'soft\panel') -Force | Out-Null
Copy-Item (Join-Path $publish 'YikaiLocal.exe') (Join-Path $Build 'soft\panel\YikaiLocal.exe') -Force

# nginx：面板运行时会写 logs 和 temp，开发机上的这两个目录已带日志，必须排除；安装包用 [Dirs] 建空目录
Copy-Tree 'D:\yikai\soft\nginx' (Join-Path $payload 'nginx') -excludeDirs @('logs','temp')
foreach ($version in @('8.0','8.2','8.5')) { Copy-Tree "D:\yikai\soft\php\$version" (Join-Path $payload "php\$version") }
foreach ($version in @('5.7','8.0')) { Copy-Tree "D:\yikai\soft\mysql\$version" (Join-Path $payload "mysql\$version") }
Copy-Tree 'D:\yikai\soft\apache\2.4.39' (Join-Path $payload 'apache\2.4.39')
Copy-Tree 'D:\yikai\soft\db-manager' (Join-Path $payload 'db-manager')
Copy-Tree 'D:\yikai\soft\packages\yikaicms' (Join-Path $payload 'packages\yikaicms')
# 不再随包带 24 MB 的 VC++ 运行库安装器：改为往每个 PHP 版本目录里放运行库 DLL（约 750 KB），
# 与最小包同一做法；MySQL 8.0 与 Apache 目录里本来就自带这几个 DLL。
# 必须选版本最新的一份：包内 Apache/MySQL 自带的副本是 14.16，而 PHP 8.2 / 8.5 按 14.44 构建，
# 旧副本会让 PHP 拒绝启动（is not compatible with this PHP build）。
$vcDlls = @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll')
foreach ($version in @('8.0','8.2','8.5')) {
    $phpDir = Join-Path $payload "php\$version"
    foreach ($dll in $vcDlls) {
        $candidates = @(
            (Join-Path 'D:\yikai\soft\apache\2.4.39\bin' $dll),
            (Join-Path 'D:\yikai\soft\mysql\8.0\bin' $dll),
            (Join-Path $env:SystemRoot ('System32\' + $dll))
        ) | Where-Object { Test-Path $_ }
        if (-not $candidates) { throw "找不到 $dll" }
        $best = $candidates |
            Sort-Object { [Version]([System.Diagnostics.FileVersionInfo]::GetVersionInfo($_).FileVersion -replace '[^0-9.].*$','') } -Descending |
            Select-Object -First 1
        Copy-Item $best (Join-Path $phpDir $dll) -Force
    }
}
Write-Host ('PHP 8.0 / 8.2 / 8.5 目录内已放运行库：' + ($vcDlls -join '、'))

# 随包配置文件：只放面板不会重新生成的。其余（panel-*.conf/ini、panel.json）都是运行时产物。
foreach ($file in @('cacert.pem','mime.types','fastcgi_params','yikaicms-rewrite.conf','phpmyadmin-php.ini','panel.defaults.json','panel.schema.json')) {
    Copy-Item "D:\yikai\config\$file" (Join-Path $Build "config\$file")
}

# 默认站点：干净的 CMS 模板副本（开发目录里的已安装站点带 config.php、installed.lock 和客户数据，不能打包）
Copy-Tree 'D:\yikai\soft\packages\yikaicms' (Join-Path $Build 'wwwroot\yikaicms.yikai')

# Apache 组件缺 LICENSE/NOTICE（源目录没有），从发布源补进负载；放在发布源里才能每次组装都带上
foreach ($pair in @(@('apache-LICENSE.txt','LICENSE'), @('apache-NOTICE.txt','NOTICE'))) {
    Copy-Item "D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\license\$($pair[0])" (Join-Path $payload "apache\2.4.39\$($pair[1])") -Force
}

# 组装后自检：负载里不能出现开发状态或绝对路径残留（php.ini 的路径由安装器按目标根改写）
$forbidden = Get-ChildItem $Build -Recurse -Force -Include 'panel.json','panel-nginx.conf','panel-apache.conf','panel-mysql*.ini','installed.lock','config.php','*.log','*.pid' -File |
    Where-Object { $_.FullName -notlike '*\packages\yikaicms\*' -and $_.Name -ne 'config.php.example' }
$stray = Get-ChildItem $Build -Recurse -Force -File -Filter 'YikaiLocal-*.exe'
Write-Host ("forbidden files: " + ($forbidden | ForEach-Object { $_.FullName.Replace($Build,'') }) -join ', ')
Write-Host ("stray build exes: " + ($stray | ForEach-Object { $_.Name }) -join ', ')
$size = (Get-ChildItem $Build -Recurse -Force -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("payload: {0:N0} MB in {1:N0} files" -f $size, (Get-ChildItem $Build -Recurse -Force -File).Count)
