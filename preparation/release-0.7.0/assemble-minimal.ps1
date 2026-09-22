param(
    [string]$Build = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build\YikaiPanel-minimal',
    [string]$Source = 'D:\yikai',
    [switch]$Force
)
# 最小包负载：Nginx + PHP 8.5 + MySQL 8.0 + 面板 + 默认站点。
# 与完整包的区别：
#   · 不带 Apache、MySQL 5.7、PHP 8.0/8.2（完整包与最小包都改为随包带运行库 DLL，不再带 24 MB 安装器）；
#   · PHP 目录里随包放 3 个 VC 运行库 DLL（vcruntime140 / vcruntime140_1 / msvcp140，约 1 MB），
#     MySQL 8.0 自带这几个 DLL，因此两者都不再依赖系统是否装过 VC++ 运行库；
#   · 数据库页面的 php.ini 由 PHP 8.5 的配置派生（模块内 PHP 版本由面板自动解析）。
$ErrorActionPreference = 'Stop'
if (Test-Path $Build) {
    if (-not $Force) { throw "负载树已存在：$Build（加 -Force 重来）" }
    Remove-Item $Build -Recurse -Force
}
$payload = Join-Path $Build 'soft'
foreach ($dir in @('soft','config','wwwroot')) { New-Item -ItemType Directory -Path (Join-Path $Build $dir) -Force | Out-Null }
# wwwroot 需要一个占位文件，否则 Inno 的 [Files] 找不到匹配项
Set-Content (Join-Path $Build 'wwwroot\.gitkeep') '' -NoNewline

function Copy-Tree([string]$from, [string]$to, [string[]]$excludeDirs = @()) {
    $args = @($from, $to, '/E', '/NJH', '/NJS', '/NDL', '/NFL', '/NP', '/R:1', '/W:1')
    if ($excludeDirs.Count -gt 0) { $args += '/XD'; $args += $excludeDirs }
    robocopy @args | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy 失败（$LASTEXITCODE）：$from" }
}

# 面板：用已发布的自包含单文件
$publish = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build\panel-publish\YikaiLocal.exe'
if (-not (Test-Path $publish)) { throw "缺少发布产物：$publish（先 dotnet publish）" }
New-Item -ItemType Directory -Path (Join-Path $payload 'panel') -Force | Out-Null
Copy-Item $publish (Join-Path $payload 'panel\YikaiLocal.exe') -Force

# Web 服务器与运行环境：只带 Nginx、PHP 8.5、MySQL 8.0
Copy-Tree (Join-Path $Source 'soft\nginx') (Join-Path $payload 'nginx') -excludeDirs @('logs','temp')
Copy-Tree (Join-Path $Source 'soft\php\8.5') (Join-Path $payload 'php\8.5')
Copy-Tree (Join-Path $Source 'soft\mysql\8.0') (Join-Path $payload 'mysql\8.0')
Copy-Tree (Join-Path $Source 'soft\db-manager') (Join-Path $payload 'db-manager')
# 不随包带 CMS 模板与默认站点：新建 YikaiCMS 项目时从官网下载（见 ProjectSources.MirrorCmsAsync）

# VC 运行库：随 PHP 目录放（MySQL 8.0 自带）。必须选**版本最新**的一份：
# 包内 Apache/MySQL 自带的副本是 14.16，PHP 8.5 按 14.44 构建，旧副本会让 PHP 拒绝启动
# （PHP Warning: ... is not compatible with this PHP build linked with 14.44）。
$vcDlls = @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll')
$phpDir = Join-Path $payload 'php\8.5'
$picked = @()
foreach ($dll in $vcDlls) {
    $candidates = @(
        (Join-Path $Source ('soft\apache\2.4.39\bin\' + $dll)),
        (Join-Path $Source ('soft\mysql\8.0\bin\' + $dll)),
        (Join-Path $env:SystemRoot ('System32\' + $dll))
    ) | Where-Object { Test-Path $_ }
    if (-not $candidates) { throw "找不到 $dll" }
    $best = $candidates |
        Sort-Object { [Version]([System.Diagnostics.FileVersionInfo]::GetVersionInfo($_).FileVersion -replace '[^0-9.].*$','') } -Descending |
        Select-Object -First 1
    Copy-Item $best (Join-Path $phpDir $dll) -Force
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($best).FileVersion
    $picked += ('{0} {1}' -f $dll, $version)
}
Write-Host ('PHP 目录随包运行库：' + ($picked -join '、'))

# 随包配置：数据库页面的 php.ini 由 PHP 8.5 的配置派生（日志与会话名区分开）
foreach ($file in @('cacert.pem','mime.types','fastcgi_params','yikaicms-rewrite.conf','panel.defaults.json','panel.schema.json')) {
    Copy-Item (Join-Path $Source "config\$file") (Join-Path $Build "config\$file")
}
$dbIni = (Get-Content (Join-Path $phpDir 'php.ini') -Raw).Replace('sessions-8.5','sessions-phpmyadmin').Replace('php-8.5.log','phpmyadmin-php.log')
[IO.File]::WriteAllText((Join-Path $Build 'config\phpmyadmin-php.ini'), $dbIni, [Text.UTF8Encoding]::new($false))
if ($dbIni -notmatch 'soft/php/8\.5/ext') { throw '数据库页面 php.ini 未指向 PHP 8.5 的扩展目录' }

# wwwroot 留空：新建项目时由面板创建目录并获取 CMS

# 自检：不该出现的东西一个都不能有
$forbidden = @('soft\apache','soft\php\8.0','soft\php\8.2','soft\mysql\5.7','soft\phpmyadmin','soft\prerequisites','soft\panel\YikaiLocal-')
$leaks = $forbidden | Where-Object { Test-Path (Join-Path $Build $_) }
if (Test-Path (Join-Path $Build 'soft\packages\yikaicms')) { throw '最小包不应包含 CMS 模板' }
if (Test-Path (Join-Path $Build 'wwwroot\yikaicms.yikai')) { throw '最小包不应包含默认站点' }
if ($leaks) { throw ('负载里出现了不该有的组件：' + ($leaks -join '、')) }
$devState = Get-ChildItem $Build -Recurse -Force -Include 'panel.json','installed.lock','config.php' -File |
    Where-Object { $_.FullName -notlike '*\packages\yikaicms\*' }
if ($devState) { throw ('负载里出现了开发状态文件：' + (($devState | ForEach-Object { $_.Name }) -join '、')) }

$files = Get-ChildItem $Build -Recurse -Force -File
$size = ($files | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("最小包负载：{0:N0} MB，{1:N0} 个文件" -f $size, $files.Count)
Write-Host ('组件：panel、nginx、php\8.5、mysql\8.0、db-manager、config（CMS 模板与默认站点按需下载）')

# 组装自检：用随包的 PHP 跑一次，确认运行库版本合适且扩展都能加载
# （旧运行库会让 PHP 直接拒绝启动，这一条能在打包前抓到）
$php = Join-Path $phpDir 'php.exe'
$phpIni = Join-Path $phpDir 'php.ini'
$check = & $php -c $phpIni -d display_errors=stderr -m 2>&1 | Out-String
if ($check -match 'is not compatible with this PHP build') { throw '随包运行库版本过旧，PHP 拒绝启动' }
foreach ($ext in @('mysqli','pdo_mysql','intl','mbstring','curl','openssl','zip')) {
    if ($check -notmatch ('(?m)^\s*' + [regex]::Escape($ext) + '\s*$')) { throw "随包 PHP 未能加载扩展：$ext" }
}
Write-Host '自检通过：随包 PHP 能启动且所需扩展全部加载'
