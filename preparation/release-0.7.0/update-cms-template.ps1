param(
    [string]$Source = 'D:\yikai\temp\cms120\yikaicms-v1.20.0',
    [string]$Template = 'D:\yikai\soft\packages\yikaicms'
)
# 把开发环境的 CMS 模板换成新版本。模板是“新建 YikaiCMS 项目”时复制的源，也是安装包里默认站点的源。
# 不动任何已安装的站点（wwwroot 下各项目、含已装 config.php 的默认站点）。
# 旧模板移到 backups 下留档；确认新包干净（无 config.php / installed.lock / 数据库）。
$ErrorActionPreference = 'Stop'
if (-not (Test-Path (Join-Path $Source 'config\version.php'))) { throw "源目录不像 CMS 包：$Source" }
$version = (Select-String -Path (Join-Path $Source 'config\version.php') -Pattern "CMS_VERSION',\s*'([0-9.]+)'").Matches[0].Groups[1].Value
if (-not $version) { throw '读不到 CMS_VERSION' }
foreach ($marker in @('config\config.php','installed.lock','storage\database.sqlite')) {
    if (Test-Path (Join-Path $Source $marker)) { throw "源包不干净：存在 $marker" }
}
if (-not (Test-Path $Template)) { throw "找不到现有模板：$Template" }
$oldVersion = (Select-String -Path (Join-Path $Template 'config\version.php') -Pattern "CMS_VERSION',\s*'([0-9.]+)'").Matches[0].Groups[1].Value
$backup = Join-Path 'D:\yikai\backups' ('yikaicms-template-' + $oldVersion + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path (Split-Path $backup) -Force | Out-Null
Move-Item -LiteralPath $Template -Destination $backup
robocopy $Source $Template /E /NJH /NJS /NDL /NFL /NP /R:1 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy 失败：$LASTEXITCODE" }
$newVersion = (Select-String -Path (Join-Path $Template 'config\version.php') -Pattern "CMS_VERSION',\s*'([0-9.]+)'").Matches[0].Groups[1].Value
Write-Host "模板已更新：$oldVersion → $newVersion"
Write-Host "旧模板备份：$backup"
Write-Host ("文件数：{0}" -f (Get-ChildItem $Template -Recurse -File).Count)
