# 易开面板安装包 OSS 发布与官网切换

本文供接手的 AI / 维护者执行。不要把 FTP、OSS 凭据写入仓库或对话输出。只有用户明确要求发布时才执行上传和官网部署；“打包 / 准备发版”只进行本地准备和检查。

## 当前发布位置（2026-09-25）

- 本机交付目录：`D:\yikai\packages`。每版必须有两个 EXE 和各自的 `.sha256`。
- OSS 目录：`oss://yikai-downloads/soft/panel/`。
- 公开直链前缀：`https://down.yikai.cn/soft/panel/`。
- 官网本地源码：`D:\yikai\wwwroot\panel.yikai`。下载配置在 `config.php`，三语说明在 `content.php`。
- 正式官网：`https://panel.yikai.cn/`。GitHub Release 仍用于发布说明和源码链接，不再作为官网安装包直链。
- FTP 凭据位置：`D:\phpstudy_pro\docs\panel.yikai\server.txt`（仓库外；严禁打印或复制到文档）。本站目前仅支持普通 FTP，未能建立 FTPS；应规划改用加密传输。仓库脚本 `preparation\oss\deploy-panel-site-ftp.ps1` 只部署 `config.php` 和 `content.php`，先备份远端，再上传和回读 SHA-256；若上传或校验失败，会尝试回滚。

## 0.7.4 已发布对象

| 文件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| `YikaiPanel-0.7.4-setup-x64.exe` | 172253162 | `73EDFE87C9505F6B437C3891898C07BC40CA288FAEB8397420B5B9402A370AD5` |
| `YikaiPanel-0.7.4-minimal-setup-x64.exe` | 84285613 | `B2D527FE162748C0E77E03C0EE8A01E68CA922B9DDCF3A665F1FAEF1DCB12C77` |

同名 `.sha256` 文件也在同一 OSS 目录。2026-09-25 已从 OSS 下载回本机逐一比对四个文件的 SHA-256；两个 EXE 和两份 `.sha256` 的公开 HTTPS 地址均返回 200，Content-Length 与本机一致。官网实际页面的按钮及校验文件链接已核对为 OSS 地址。

## 下一版的操作顺序

1. 根据源码仓库的构建和验收流程产出正式安装包，不把“仅编译成功”当作发布验收。检查两个 EXE 的文件版本、文件名、大小、SHA-256，与两份 `.sha256` 内容一致。不要碰 `D:\yikai\data`、`wwwroot` 下其他项目或已有用户数据。
2. 确认 OSS 中没有同名对象，或同名对象经下载回读后与本地完全一致。**版本文件名视为不可变**；同名但内容不同应停下排查，不要覆盖。
3. 从 `D:\yikai\packages` 上传四个文件。当前机器已安装 `ossutil.exe`，使用现有 OSS 凭据环境，切勿把密钥写进命令或文档。例如：

   ```powershell
   $version = '0.7.4' # 换成经批准发布的新版本
   $names = @(
     "YikaiPanel-$version-setup-x64.exe",
     "YikaiPanel-$version-minimal-setup-x64.exe",
     "YikaiPanel-$version-setup-x64.exe.sha256",
     "YikaiPanel-$version-minimal-setup-x64.exe.sha256"
   )
   ossutil.exe ls 'oss://yikai-downloads/soft/panel/'
   foreach ($name in $names) {
     $source = Join-Path 'D:\yikai\packages' $name
     if (-not (Test-Path -LiteralPath $source)) { throw "Missing $source" }
     $contentType = if ($name.EndsWith('.sha256')) { 'text/plain; charset=utf-8' } else { 'application/octet-stream' }
     ossutil.exe cp $source "oss://yikai-downloads/soft/panel/$name" --content-type $contentType --cache-control 'public, max-age=31536000, immutable' --force
     if ($LASTEXITCODE -ne 0) { throw "OSS upload failed: $name" }
   }
   ```

   上述 `--force` **仅用于已事先确认同名对象不存在** 的首次上传；若对象已存在，先回读比较，不得盲目重传。

4. 把 OSS 的四个对象下载到新建临时目录，用 `Get-FileHash -Algorithm SHA256` 与本机原件逐一比较。还要用公开域名对四个 URL 发起 HTTPS HEAD，确认返回 200、Content-Length 与本机相等；至少 GET 一份 `.sha256` 核对内容。OSS CLI 成功不等于 CDN 域名可访问。示例：

   ```powershell
   $verifyDir = Join-Path $env:TEMP ('yikai-panel-oss-' + [guid]::NewGuid().ToString('N'))
   New-Item -ItemType Directory -Path $verifyDir | Out-Null
   foreach ($name in $names) {
     $download = Join-Path $verifyDir $name
     ossutil.exe cp "oss://yikai-downloads/soft/panel/$name" $download
     if ($LASTEXITCODE -ne 0) { throw "OSS readback failed: $name" }
     $expected = (Get-FileHash -LiteralPath (Join-Path 'D:\yikai\packages' $name) -Algorithm SHA256).Hash
     $actual = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
     if ($expected -ne $actual) { throw "OSS hash mismatch: $name" }
   }
   ```

5. 在本地官网的 `config.php` 设置 `version`、`releasedAt`、两个包的 `file`、`size`、`sha256`；保持 `downloadBase = https://down.yikai.cn/soft/panel`，`releaseUrl` 指向对应 GitHub Release。需要时同步修改 `content.php` 的中/英/日文案。先运行 `php.exe -l` 检查两个 PHP 文件，再预览页面。
6. 确认公开直链已可用后，执行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\preparation\oss\deploy-panel-site-ftp.ps1`。该脚本从私有文档读取 FTP 用户和密码，不在输出中显示；它依赖当前 `server.txt` 的第 5、7 行分别为账号和密码，文件格式变化时先修脚本。远端备份保存在 `preparation\oss\backups\<时间>-before-oss\`（被 Git 忽略）。FTP 不加密，应避免在不可信网络上执行，并尽快迁移到 SFTP/FTPS。
7. 用带随机查询参数的 `https://panel.yikai.cn/?verify=<时间>` 检查线上首页，分别检查中文、英文、日文的两个下载按钮及两份校验文件链接。确认页面显示新版本、链接指向 OSS，且公开对象仍可访问。保留验收结果和哈希记录，不在仓库记录凭据。

## 故障与回滚

- 只上传了 OSS、尚未修改官网：旧官网链接不受影响。不要删除旧版 OSS 对象。
- 官网部署中断：查看脚本输出；脚本会尝试回滚已上传文件。无论脚本是否提示成功，重新通过 FTP 读取 `config.php`、`content.php` 并比对。
- 线上链接错误但页面可用：从脚本打印的远端备份目录恢复 `config.php` 和 `content.php`，再做 FTP 回读和页面验证；不要为了回滚清空 OSS 目录。
- CDN 公开 URL 不可达：暂缓官网切换，先核查域名、OSS 对象及权限。不要把“`ossutil cp` 成功”写成公网已可下载。
