param(
    [string]$Cms = 'D:\yikai\temp\cms120\yikaicms-v1.20.0',
    [string]$Target = 'D:\yikai\config\yikaicms-rewrite.conf',
    [switch]$Backup
)
# 从 CMS 自带的 deploy/nginx-server.conf 生成面板随包的伪静态规则。
# 变换（与 0.7.0 生成 1.19.9 规则时一致）：
#   1) 只取文件里给 Nginx 的那一段（`# =====` 分隔线之前）；
#   2) 把 CMS 假定的 127.0.0.1:9000 换成面板占位端口 127.0.0.1:9082
#      —— 面板启动时会把它替换成该项目的实际 FastCGI 端口，并补全 fastcgi_params 的绝对路径；
#   3) 不再手工去掉 /api/v1/ 的 `^~`：1.20.0 已在块内自带 PHP 处理与兜底 deny，1.19.9 时代的补丁不再需要。
$ErrorActionPreference = 'Stop'
$source = Join-Path $Cms 'deploy\nginx-server.conf'
if (-not (Test-Path $source)) { throw "找不到 $source" }
$raw = [IO.File]::ReadAllText($source)
$parts = [regex]::Split($raw, '# ={20,}')
$section = $parts[0].TrimEnd() + "`n"
$generated = $section.Replace('127.0.0.1:9000', '127.0.0.1:9082')
if ($generated -notmatch '127\.0\.0\.1:9082') { throw '生成结果里没有占位端口 9082，说明源文件结构变了' }
if ($generated -notmatch 'include fastcgi_params;') { throw '生成结果里没有 include fastcgi_params;，面板无法补全配置路径' }
if ($generated -match '127\.0\.0\.1:9000') { throw '仍有未替换的 9000 端口' }
# 面板侧补丁（1.19.9 时代就有，1.20.0 仍需要）：
# CMS 自带的 `location /` 里有一条“带 yk_admin cookie 就 rewrite ^ /index.php last”，
# 登录后台后浏览器带着这个 cookie 访问 /admin/（目录形式）时会被前台入口接管，后台打不开。
# /admin/index.php 这类带 .php 的地址先匹配 `location ~ \.php$`，不受影响；出问题的正是目录形式。
# 这里显式声明 /admin/：让后台目录绕过前台静态页与 cookie 重写，.php 仍交给 PHP 处理。
$adminPatch = @'

# 后台目录：绕过前台静态页与后台 cookie 重写（面板补充，见 generate-rewrite.ps1）
location = /admin { return 301 /admin/; }
location /admin/ {
    try_files $uri $uri/ =404;
}
'@
if ($generated -notmatch 'location /admin/') {
    $anchor = "`n# 默认处理（首页 / 与无扩展名"
    if ($generated -notmatch [regex]::Escape($anchor)) { throw '找不到“默认处理”注释，无法确定补丁插入位置' }
    $generated = $generated.Replace($anchor, $adminPatch + $anchor)
}
if ($generated -notmatch 'location /admin/') { throw '后台目录补丁未写入' }
if ($Backup -and (Test-Path $Target)) {
    $backup = Join-Path 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\evidence' ('yikaicms-rewrite-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.conf')
    Copy-Item $Target $backup
    Write-Host "旧规则已备份：$backup"
}
[IO.File]::WriteAllText($Target, $generated, [Text.UTF8Encoding]::new($false))
Write-Host ("已生成：{0}（{1} 行，{2} 字节）" -f $Target, (($generated -split "`n").Count), (Get-Item $Target).Length)
