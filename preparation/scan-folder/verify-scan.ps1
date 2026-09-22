param(
    [string]$Root = 'D:\yikai-scan-test',
    [string]$Exe = 'D:\yikai-soft\dev\yikai-panel\src\bin\Release\net9.0-windows\YikaiLocal.exe',
    [string]$Payload = 'D:\yikai-soft\dev\yikai-panel\preparation\release-0.7.0\build\YikaiPanel-minimal',
    [ValidateSet('run','clean')]
    [string]$Phase = 'run'
)
# 扫描目录添加项目：验收“带点的目录登记成项目、不带点的可选补 .yikai、其余跳过”，并核对登记结果与访问。
#   1) 默认只加名字带点的目录，域名就是目录名；
#   2) --include-plain 时，不带点的英文/数字/-/_ 目录补 .yikai 加入；中文、空格、以 - 开头的一律跳过；
#   3) 带点和同名不带点同时存在时，域名归带点的目录；
#   4) 登记是“就地登记”：不覆盖目录里已有的 index.php，也不塞 storage 目录；
#   5) 登记后的站点能真的跑起来（这套夹具带最小包的 soft 组件，PHP 只有 8.5）。
$ErrorActionPreference = 'Stop'
$report = [ordered]@{ phase = $Phase; root = $Root; checks = @() }
try { Start-Transcript -Path 'D:\yikai-soft\dev\yikai-panel\preparation\scan-folder\evidence\scan.log' -Force | Out-Null } catch { }
function Check([bool]$ok, [string]$text) {
    $script:report.checks += [ordered]@{ passed = $ok; check = $text }
    if ($ok) { Write-Host "PASS $text" } else { Write-Host "FAIL $text"; $script:failed = $true }
}
function Step([string]$text) { Write-Host ("STEP {0:HH:mm:ss} {1}" -f (Get-Date), $text) }
function Arguments([string[]]$arguments) {
    return ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
}
function RunPanel([string[]]$arguments, [int]$timeoutSec = 600) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new($Exe)
    $psi.Arguments = Arguments $arguments
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    if (-not $p.WaitForExit($timeoutSec * 1000)) { $p.Kill(); throw "命令超时：$($arguments -join ' ')" }
    return $p.ExitCode
}
# 只为 --scan-sites 之类的纯命令用：它们不会启动 nginx/mysqld，不会有人继承管道。
# （--start 之后绝不能用它读 stdout：子进程继承管道会让读取永远不返回。）
function RunPanelCapture([string[]]$arguments, [int]$timeoutSec = 600) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new($Exe)
    $psi.Arguments = Arguments $arguments
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true; $psi.RedirectStandardOutput = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $text = $p.StandardOutput.ReadToEnd()
    if (-not $p.WaitForExit($timeoutSec * 1000)) { $p.Kill(); throw "命令超时：$($arguments -join ' ')" }
    $script:lastOutput = $text
    return $p.ExitCode
}
function CopyTree([string]$from, [string]$to) {
    New-Item -ItemType Directory -Path $to -Force | Out-Null
    robocopy $from $to /E /NFL /NDL /NJH /NJS /NP /MT:16 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "复制失败：$from → $to（robocopy $LASTEXITCODE）" }
}
if ($Phase -eq 'clean') {
    Get-CimInstance Win32_Process -Filter "Name='nginx.exe' or Name='php-cgi.exe' or Name='mysqld.exe' or Name='YikaiLocal.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -like "$Root*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
    Write-Host "removed $Root"
    try { Stop-Transcript | Out-Null } catch { }
    return
}
if (Test-Path $Root) { throw "测试目录已存在：$Root（先 -Phase clean）" }

Step 'fixture: 组件目录（最小包负载）+ wwwroot 混合目录'
if (-not (Test-Path (Join-Path $Payload 'soft\nginx'))) { throw "缺少已组装的负载：$Payload（先运行 assemble-minimal.ps1）" }
CopyTree (Join-Path $Payload 'soft') (Join-Path $Root 'soft')
CopyTree (Join-Path $Payload 'config') (Join-Path $Root 'config')
# 安装器用 [Dirs] 建这几个目录（负载里是空的，不随包）：nginx 不会自建 logs/temp，缺了直接启动失败
foreach ($dir in @('soft\nginx\logs','soft\nginx\temp','config\ssl','logs','temp','data\mysql57','data\mysql80','backups')) {
    New-Item -ItemType Directory -Path (Join-Path $Root $dir) -Force | Out-Null
}
$php = '8.5'   # 最小包只带 PHP 8.5；面板自身也自愈到这个版本
$www = Join-Path $Root 'wwwroot'
# 1) 普通 PHP 站点（带点）
$plain = Join-Path $www 'plain.yikai'
New-Item -ItemType Directory -Path $plain -Force | Out-Null
Set-Content (Join-Path $plain 'index.php') -Value '<?php echo "plain-ok";' -Encoding UTF8
# 2) 假 CMS（带点，有 config/version.php）
$cms = Join-Path $www 'cmsdemo.yikai'
New-Item -ItemType Directory -Path (Join-Path $cms 'config') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $cms 'includes') -Force | Out-Null
Set-Content (Join-Path $cms 'config\version.php') -Value "<?php define('CMS_VERSION','1.20.1');" -Encoding UTF8
Set-Content (Join-Path $cms 'includes\init.php') -Value '<?php // fixture' -Encoding UTF8
Set-Content (Join-Path $cms 'config\config.php') -Value "<?php define('DB_NAME','cmsdemo_db'); define('DB_USER','root');" -Encoding UTF8
# 3) SQLite 站点（带点）
$sqlite = Join-Path $www 'sqlitesite.yikai'
New-Item -ItemType Directory -Path (Join-Path $sqlite 'storage') -Force | Out-Null
Set-Content (Join-Path $sqlite 'storage\database.sqlite') -Value 'SQLite format 3' -NoNewline -Encoding ASCII
Set-Content (Join-Path $sqlite 'index.php') -Value '<?php echo "sqlite-ok";' -Encoding UTF8
# 4) 英文名字带点（对照 yikaiflow：域名归它）
$flowDotted = Join-Path $www 'yikaiflow.yikai'
New-Item -ItemType Directory -Path $flowDotted -Force | Out-Null
Set-Content (Join-Path $flowDotted 'index.php') -Value '<?php echo "flow-dotted-ok";' -Encoding UTF8
# 5) 不带点：纯英文（勾选后补 .yikai；勾选前跳过）
$nodot = Join-Path $www 'nodot'
New-Item -ItemType Directory -Path $nodot -Force | Out-Null
Set-Content (Join-Path $nodot 'index.php') -Value '<?php echo "nodot-ok";' -Encoding UTF8
# 6) 不带点：英文 + 连字符/下划线/数字（允许）
$flow = Join-Path $www 'yikaiflow'
New-Item -ItemType Directory -Path $flow -Force | Out-Null
$mixed = Join-Path $www 'my_shop-2'
New-Item -ItemType Directory -Path $mixed -Force | Out-Null
# 7) 不带点但不合法：中文、空格、以 - 开头（勾选了也要跳过）
New-Item -ItemType Directory -Path (Join-Path $www '中文目录') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $www 'my site') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $www '-badname') -Force | Out-Null
# 8) 带点但不是域名：中文、空格
New-Item -ItemType Directory -Path (Join-Path $www '中文站.yikai') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $www 'my site.yikai') -Force | Out-Null
# 9) 隐藏目录（跳过）
New-Item -ItemType Directory -Path (Join-Path $www '.hidden.yikai') -Force | Out-Null
Check (Test-Path $plain) '夹具已建立'

Step '第一次扫描：只加名字带点的目录'
$out1 = Join-Path $Root 'scan-1.txt'
$code = RunPanelCapture @('--root', $Root, '--scan-sites', '--directory', $www, '--php', $php, '--out', $out1)
Check ($code -eq 0) "扫描命令退出码 0（实际 $code）"
$sites = @((Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json).sites)
Check ($sites.Count -eq 4) "登记了 4 个带点的目录（实际 $($sites.Count)）"
$domains = ($sites | ForEach-Object { $_.domain } | Sort-Object) -join ','
Check ($domains -eq 'cmsdemo.yikai,plain.yikai,sqlitesite.yikai,yikaiflow.yikai') "域名来自目录名：$domains"
$cmsSite = $sites | Where-Object { $_.domain -eq 'cmsdemo.yikai' }
Check ($cmsSite.template -eq 'yikaicms') "CMS 目录识别为 yikaicms（实际 $($cmsSite.template)）"
Check ($cmsSite.databaseName -eq 'cmsdemo_db') "数据库名从项目配置读出（实际 $($cmsSite.databaseName)）"
$sqliteSite = $sites | Where-Object { $_.domain -eq 'sqlitesite.yikai' }
Check ($sqliteSite.database -eq 'sqlite') "有 storage/database.sqlite 的识别为 SQLite（实际 $($sqliteSite.database)）"
$plainSite = $sites | Where-Object { $_.domain -eq 'plain.yikai' }
Check ($plainSite.database -eq 'mysql80') "普通目录默认 MySQL 8.0（实际 $($plainSite.database)）"
Check ((($sites | ForEach-Object { $_.httpPort } | Sort-Object -Unique).Count) -eq 4) '四个项目端口不重复'
Check (@($sites | Where-Object { $_.enabled -eq $false }).Count -eq 4) '添加后都是停止状态'
$notExpected = @('nodot','nodot.yikai','yikaiflow','my_shop-2','my-shop-2.yikai','中文目录','my site','-badname','中文站.yikai','my site.yikai','.hidden.yikai')
Check ((@($sites | Where-Object { $_.domain -in $notExpected })).Count -eq 0) '不该加的目录（不带点 / 隐藏 / 非域名）都没有被登记'
Check ((@($sites | Where-Object { $_.php -ne $php })).Count -eq 0) "登记的项目都用指定的 PHP（$php）"
# 控制台输出按行匹配（stdout 是 \r\n，所以不用 $ 结尾，用“名字 + 空格 + 标识”前缀判断）
$skips = $script:lastOutput
Check ($skips -match '(?m)^skipped nodot plain-name\s') '不带点的英文目录标记为 plain-name 跳过'
Check ($skips -match '(?m)^skipped yikaiflow plain-name\s') '不带点的 yikaiflow 同样跳过'
Check ($skips -match '(?m)^skipped 中文目录 plain-name\s') '不带点的中文目录也先按 plain-name 跳过'
Check ($skips -match '(?m)^skipped 中文站\.yikai not-a-domain\s') '中文带点目录标记为 not-a-domain'
Check ($skips -match '(?m)^skipped my site\.yikai not-a-domain\s') '带空格的目录标记为 not-a-domain'
Check ($skips -match '(?m)^skipped \.hidden\.yikai hidden\s') '隐藏目录标记为 hidden'
$report1 = Get-Content $out1 -Raw -Encoding UTF8
Check ($report1 -match 'nodot（名字不带点') '报告里给出了“名字不带点”的中文原因'
Check ($report1 -match '中文站\.yikai（不是可用域名') '报告里给出了“不是可用域名”的中文原因'
Check (Test-Path (Join-Path $plain 'index.php')) '登记没有复制模板文件（原目录内容不变）'
Check (-not (Test-Path (Join-Path $plain 'config\version.php'))) '普通目录没有被塞进 CMS 文件'
Check (-not (Test-Path (Join-Path $plain 'storage'))) '普通目录没有被塞进 storage 目录'

Step '第二次扫描：同样的目录不会重复登记'
$code = RunPanelCapture @('--root', $Root, '--scan-sites', '--directory', $www, '--php', $php)
$sites2 = @((Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json).sites)
Check ($code -eq 0 -and $sites2.Count -eq 4) "重复扫描不会重复登记（仍为 $($sites2.Count) 个）"
Check ($script:lastOutput -match '(?m)^skipped plain\.yikai domain-taken\s') '已登记目录的域名被占用（domain-taken）'

Step '第三次扫描：--include-plain 给不带点的英文目录补 .yikai'
$out3 = Join-Path $Root 'scan-3.txt'
$code = RunPanelCapture @('--root', $Root, '--scan-sites', '--directory', $www, '--php', $php, '--include-plain', '--out', $out3)
Check ($code -eq 0) "带 --include-plain 的扫描退出码 0（实际 $code）"
$sites3 = @((Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json).sites)
Check ($sites3.Count -eq 6) "补登记了 nodot.yikai 和 my-shop-2.yikai（共 $($sites3.Count) 个）"
$domains3 = ($sites3 | ForEach-Object { $_.domain } | Sort-Object) -join ','
Check ($domains3 -eq 'cmsdemo.yikai,my-shop-2.yikai,nodot.yikai,plain.yikai,sqlitesite.yikai,yikaiflow.yikai') "补出来的域名是“目录名 + .yikai”，下划线换算成 -：$domains3"
Check ($script:lastOutput -match '(?m)^skipped yikaiflow domain-taken\s') '不带点的 yikaiflow 让出已被带点目录占用的域名'
Check ($script:lastOutput -match '(?m)^skipped 中文目录 plain-name-invalid\s') '中文的无点目录即使在 include-plain 下也跳过'
Check ($script:lastOutput -match '(?m)^skipped my site plain-name-invalid\s') '带空格的无点目录标记 plain-name-invalid'
Check ($script:lastOutput -match '(?m)^skipped -badname plain-name-invalid\s') '以 - 开头的无点目录标记 plain-name-invalid'
$report3 = Get-Content $out3 -Raw -Encoding UTF8
Check ($report3 -match 'added my-shop-2\.yikai folder=my_shop-2') '报告里能对上“目录 my_shop-2 → 域名 my-shop-2.yikai”'
Check ($report3 -match '中文目录（名字只能用英文字母、数字、- 和 _') '无点非法名字的原因写明了字符限制'
Check (-not (Test-Path (Join-Path $nodot 'index.php.orig'))) '补登记没有动过原来目录'
Check (-not (Test-Path (Join-Path $nodot 'storage'))) '补登记没有往原目录塞 storage'

Step '第四次扫描：全新目录里排查“带点与不带点同名”和大小写/下划线换算'
$www2 = Join-Path $Root 'wwwroot2'
New-Item -ItemType Directory -Path (Join-Path $www2 'collide.yikai') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $www2 'collide') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $www2 'MyShop') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $www2 'My_Shop') -Force | Out-Null
$out4 = Join-Path $Root 'scan-4.txt'
$code = RunPanelCapture @('--root', $Root, '--scan-sites', '--directory', $www2, '--php', $php, '--include-plain', '--out', $out4)
Check ($code -eq 0) "扫描退出码 0（实际 $code）"
$sites4 = @((Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json).sites)
$domains4 = @($sites4 | ForEach-Object { $_.domain })
Check ($sites4.Count -eq 9) "共 9 个项目（实际 $($sites4.Count)）"
Check ($domains4 -contains 'collide.yikai') '同名的两个目录里，带点的 collide.yikai 拿到域名'
Check ($domains4 -contains 'myshop.yikai') 'MyShop 转成小写域名 myshop.yikai'
Check ($domains4 -contains 'my-shop.yikai') 'My_Shop 转成 my-shop.yikai（下划线换成 -）'
Check ($script:lastOutput -match '(?m)^skipped collide name-conflict\s') '不带点的 collide 标记 name-conflict 并让出域名'
$report4 = Get-Content $out4 -Raw -Encoding UTF8
Check ($report4 -match 'collide（同名的 collide\.yikai 目录已存在，域名归它）') '冲突原因写明了域名归带点目录'

Step '把夹具挪到高位端口（不和本机另外一套面板抢 80xx / 8878 / 3306 / 3309）'
# 夹具是独立环境，但端口是本机的：默认它也用 8081+、8878、3309，撞上正在运行的正式面板时
# 会把对方的运行环境顶掉（实测过一次）。这里先把端口固定到高位，再启动。
$code = RunPanelCapture @('--root', $Root, '--db-port', 'mysql80', '3319')
Check ($code -eq 0) "MySQL 8.0 端口固定为 3319（exit $code）"
$cfgPath = Join-Path $Root 'config\panel.json'
$cfg = Get-Content $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
$cfg.dbManagerPort = 18778      # 面板自带的数据库页面端口
$cfg.dbFastCgiPort = 19184      # 数据库页面的 PHP-CGI（要避开下面站点的 19080+）
$index = 0
foreach ($site in $cfg.sites) { $site.httpPort = 18080 + $index; $site.fastCgiPort = 19080 + $index; $site.portPinned = $true; $index++ }
$cfg | ConvertTo-Json -Depth 10 | Set-Content $cfgPath -Encoding UTF8
$ported = Get-Content $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
Check ((@($ported.sites | Where-Object { $_.httpPort -ge 18080 }).Count) -eq @($ported.sites).Count) "夹具项目全部改用 18080+ 端口（共 $(@($ported.sites).Count) 个）"
Check ($ported.dbManagerPort -eq 18778) '数据库页面端口改成 18778'

Step '登记后的站点能真的跑起来'
# 扫描进来的项目是停止状态（和界面一致）：用 --start-site 启用并启动，等同于点界面上的“启动”。
# 注意这里用 RunPanel 而不是 RunPanelCapture：启动起来的 nginx / php-cgi / mysqld 会继承 stdout 管道，
# 读 stdout 会一直不返回（实测卡死过一次）。
$code = RunPanel @('--root', $Root, '--start-site', 'plain.yikai')
Check ($code -eq 0) "启动单个项目成功（exit $code）"
# 端口要重新读：启动时若端口被别的程序占着，面板会自动换到空闲端口
$live = @((Get-Content (Join-Path $Root 'config\panel.json') -Raw -Encoding UTF8 | ConvertFrom-Json).sites)
$livePlain = $live | Where-Object { $_.domain -eq 'plain.yikai' }
Check ($null -ne $livePlain) '启动后配置里仍然有 plain.yikai'
Check ($livePlain.enabled -eq $true) '启动后项目是启用状态'
$port = $livePlain.httpPort
if ($port -ne $plainSite.httpPort) { Write-Host "NOTE 端口从 $($plainSite.httpPort) 自动避让到 $port" }
$fcgi = $livePlain.fastCgiPort
$listening = $false
for ($i = 0; $i -lt 60 -and -not $listening; $i++) {
    Start-Sleep -Milliseconds 500
    $listening = $null -ne (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}
Check $listening "登记的站点在 $port 监听"
$fcgiReady = $false
for ($i = 0; $i -lt 60 -and -not $fcgiReady; $i++) {
    Start-Sleep -Milliseconds 500
    $fcgiReady = $null -ne (Get-NetTCPConnection -LocalPort $fcgi -State Listen -ErrorAction SilentlyContinue)
}
Check $fcgiReady "站点的 PHP-CGI（$fcgi）已就绪"
$text = ''
# 端口开了不等于 PHP-CGI 已经能应答，前几次可能 503；重试到出结果
for ($i = 0; $i -lt 40; $i++) {
    try {
        $c = [System.Net.Sockets.TcpClient]::new(); $c.Connect('127.0.0.1', $port)
        $s = $c.GetStream()
        # Host 指向该站点自己的域名：面板也支持用 Host 区分站点，这里按真实访问方式请求
        $b = [Text.Encoding]::ASCII.GetBytes("GET / HTTP/1.0`r`nHost: plain.yikai`r`n`r`n"); $s.Write($b, 0, $b.Length)
        $buf = New-Object byte[] 4096; $n = $s.Read($buf, 0, $buf.Length); $c.Close()
        $text = [Text.Encoding]::ASCII.GetString($buf, 0, $n)
    } catch { $text = '' }
    if ($text -match '^HTTP/\S+\s+200' -and $text -match 'plain-ok') { break }
    Start-Sleep -Milliseconds 750
}
Check ($text -match '^HTTP/\S+\s+200' -and $text -match 'plain-ok') "站点内容可访问（plain-ok）：$($text.Split("`r`n")[0])"
RunPanel @('--root', $Root, '--stop') | Out-Null

Step '收尾：夹具的进程一个都不能留（端口被夹具占着会顶掉本机正式环境）'
$leftover = @(Get-CimInstance Win32_Process -Filter "Name='nginx.exe' or Name='php-cgi.exe' or Name='mysqld.exe' or Name='YikaiLocal.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -like "$Root*" })
foreach ($process in $leftover) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue }
Check ($leftover.Count -eq 0) "没有残留进程（清掉了 $($leftover.Count) 个）"

try { Stop-Transcript | Out-Null } catch { }
$report.failed = [bool]$script:failed
New-Item -ItemType Directory -Path 'D:\yikai-soft\dev\yikai-panel\preparation\scan-folder' -Force | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content 'D:\yikai-soft\dev\yikai-panel\preparation\scan-folder\verification-scan.json' -Encoding UTF8
if ($script:failed) { Write-Host 'FAILED'; exit 1 }
Write-Host ('all passed · ' + $report.checks.Count + ' 项')
