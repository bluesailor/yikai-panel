param(
    [int]$SitePort = 0,
    [string]$Root = 'D:\yikai',
    [string]$Out = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\verification-admin-patch.json'
)
# 验证面板侧 /admin/ 补丁：带后台 cookie（yk_admin=1）访问目录形式的 /admin/ 时，
# 请求必须交给后台应用，而不是被 CMS 前台规则（location / 里的 cookie 重写）接管。
# 用开发环境里已安装的 yikaicms.yikai 站点实测（代码是 1.19.9，规则是 1.20.0）。
# 端口不写死：从开发环境的 panel.json 里读该站点当前端口（面板可能因为占用自动换过端口）。
$ErrorActionPreference = 'Stop'
if ($SitePort -le 0) {
    $configFile = Join-Path $Root 'config\panel.json'
    if (-not (Test-Path $configFile)) { throw "找不到开发环境配置：$configFile" }
    $site = @((Get-Content $configFile -Raw -Encoding UTF8 | ConvertFrom-Json).sites) | Where-Object { $_.domain -eq 'yikaicms.yikai' } | Select-Object -First 1
    if (-not $site) { throw '开发环境的项目列表里没有 yikaicms.yikai' }
    if (-not $site.enabled) { throw '开发环境里 yikaicms.yikai 没有启动（先在面板里启动它）' }
    $SitePort = $site.httpPort
}
if (-not (Get-NetTCPConnection -LocalPort $SitePort -State Listen -ErrorAction SilentlyContinue)) {
    throw "127.0.0.1:$SitePort 没有在监听：先在面板里启动 yikaicms.yikai"
}
$report = [ordered]@{ site = "127.0.0.1:$SitePort"; checks = @() }
function Check([bool]$ok, [string]$text) {
    $script:report.checks += [ordered]@{ passed = $ok; check = $text }
    if ($ok) { Write-Host "PASS $text" } else { Write-Host "FAIL $text"; $script:failed = $true }
}
# 返回 @{ Status = 200; Headers = '...'; Body = '...' }
function Fetch([string]$path, [string]$cookie = '') {
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        if (-not $client.ConnectAsync('127.0.0.1', $SitePort).Wait(10000)) { $client.Close(); return $null }
        $stream = $client.GetStream(); $stream.ReadTimeout = 15000; $stream.WriteTimeout = 15000
        $req = "GET $path HTTP/1.1`r`nHost: 127.0.0.1`r`nConnection: close`r`n"
        if ($cookie) { $req += "Cookie: $cookie`r`n" }
        $req += "`r`n"
        $bytes = [Text.Encoding]::ASCII.GetBytes($req)
        $stream.Write($bytes, 0, $bytes.Length)
        $buffer = New-Object byte[] 65536
        $total = 0
        while (($read = $stream.Read($buffer, $total, $buffer.Length - $total)) -gt 0 -and $total -lt $buffer.Length) { $total += $read }
        $client.Close()
        $text = [Text.Encoding]::UTF8.GetString($buffer, 0, $total)
        $split = $text.IndexOf("`r`n`r`n")
        $headers = if ($split -ge 0) { $text.Substring(0, $split) } else { $text }
        $body = if ($split -ge 0) { $text.Substring($split + 4) } else { '' }
        $status = if ($headers -match '^HTTP/\S+\s+(\d+)') { [int]$Matches[1] } else { 0 }
        return @{ Status = $status; Headers = $headers; Body = $body }
    } catch { return $null }
}

$front = Fetch '/'
Check ($null -ne $front -and $front.Status -eq 200) '前台首页仍返回 200'
Check ($null -ne $front -and $front.Body -notmatch '<\?php') '前台首页未外泄 PHP 源码'

$plain = Fetch '/admin/'
Check ($null -ne $plain -and $plain.Status -in @(200,301,302)) "未登录访问 /admin/ 有响应（HTTP $($plain.Status)）"

# 关键断言：带后台 cookie 时，/admin/ 必须由后台应用处理。
# 未登录态下后台会 302 到自己的登录页（Location 仍以 /admin/ 开头）；
# 若被前台规则接管，Location 会指向前台（/ 或 /index.php）或直接返回首页内容。
$withCookie = Fetch '/admin/' 'yk_admin=1'
Check ($null -ne $withCookie -and $withCookie.Status -eq 302) "带 yk_admin cookie 访问 /admin/ 由后台处理（HTTP $($withCookie.Status)）"
Check ($null -ne $withCookie -and $withCookie.Headers -match "(?i)Location:\s*/admin/") '跳转目标仍在后台目录内（未被前台接管）'
Check ($null -ne $withCookie -and $null -ne $front -and $withCookie.Body -ne $front.Body) '带 cookie 的 /admin/ 不是前台首页'

# 跟进到后台登录页：必须真的能打开
$login = Fetch '/admin/login.php' 'yk_admin=1'
Check ($null -ne $login -and $login.Status -eq 200) "后台登录页返回 200（HTTP $($login.Status)）"
Check ($null -ne $login -and $login.Body -notmatch '<\?php') '后台登录页执行了 PHP（未外泄源码）'
Check ($null -ne $login -and $login.Body -match '(?i)login|登录|ログイン|password|密码') '后台登录页是真实内容'

$entry = Fetch '/admin/index.php' 'yk_admin=1'
Check ($null -ne $entry -and $entry.Status -in @(200,302)) "带 cookie 访问 /admin/index.php 有响应（HTTP $($entry.Status)）"
Check ($null -ne $entry -and $entry.Body -notmatch '<\?php') '/admin/index.php 执行了 PHP（未外泄源码）'

$report.failed = [bool]$script:failed
$report | ConvertTo-Json -Depth 6 | Set-Content $Out -Encoding UTF8
if ($script:failed) { Write-Host 'FAILED'; exit 1 }
Write-Host 'all passed'
