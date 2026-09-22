# 易开面板配置文件说明

## 主配置

`D:\yikai\config\panel.json` 是面板唯一的用户配置来源。面板启动时读取它，必要时补齐缺失字段并写回；旧版本配置会按 `schemaVersion` 自动补齐，不需要用户手动重建。

主要字段：

| 字段 | 用途 | 默认值 |
| --- | --- | --- |
| `schemaVersion` | 配置格式版本 | `2` |
| `language` | 界面语言 | `zh` |
| `webServer` | Web 服务器：`nginx` 或 `apache`（Apache 2.4.39），二者共用项目端口，同一时间只运行一个 | `nginx` |
| `phpDefault` | 新项目默认 PHP，可在“工具 → 新项目默认 PHP 版本”修改；选 8.0 时新建 YikaiCMS 仍用 8.2 | `8.2` |
| `mysqlActive` | “全部启动”时一定启动的 MySQL；项目用到的另一版本也会一起启动 | `mysql80` |
| `autoStart` | 打开面板是否启动环境 | `true` |
| `minimizeToTray` | 关闭窗口是否缩到托盘 | `true` |
| `lanAccess` | 项目网站端口是否监听 `0.0.0.0`（局域网可访问）；数据库页面、MySQL、FastCGI 不受影响 | `false` |
| `theme` | 界面主题：`light` / `dark` / `system`（跟随 Windows 应用模式） | `light` |
| `fontSize` | 界面字号（磅），面板内的行高和最小窗口尺寸按它等比缩放；范围 9–13 | `10` |
| `codeFont` | 编辑框使用的等宽字体名；本机没装时自动回退 | `Consolas` |
| `codeFontSize` | 编辑框字号（磅），范围 8–18 | `10` |
| `sidebarWidth` | 左侧项目栏宽度 | `360` |
| `windowWidth` / `windowHeight` | 上次关闭时的窗口大小（96 DPI 逻辑像素）；为空时按屏幕可用区域决定，最大 1600×1040 | 空 |
| `windowMaximized` | 上次关闭时是否最大化 | `false` |
| `*Port` | 服务端口 | 按本机可用端口调整 |
| `sites` | 项目列表及每个项目的连接设置 | `[]` |

密码目前按本机开发环境约定保存在本地配置中。后续如要面向多人或发布到公共电脑，再评估使用 Windows DPAPI 加密；不改变配置字段名称，避免迁移困难。

## 默认值和 schema

- `panel.defaults.json`：安装器和新配置使用的出厂默认值，不覆盖已有用户设置。
- `panel.schema.json`：编辑器和诊断工具使用的字段约束，不是运行时配置来源。

## 自动生成文件

`panel-nginx.conf`、`panel-apache.conf`、`panel-rewrite-*.conf`、`panel-php-*.ini`、`panel-mysql*.ini` 都由面板根据主配置生成。手动修改会在下一次项目、端口或服务操作时被覆盖；需要永久修改时应从面板设置入口修改源配置。

## 修改流程

1. 关闭正在进行的启动、停止、迁移或升级操作。
2. 备份 `panel.json`。
3. 只修改 schema 中允许的字段，端口不能重复。
4. 重新打开面板，检查状态栏、网站、后台和数据库连接。
5. 发现异常时恢复备份，查看 `D:\yikai\logs\panel.log` 和 `panel-last-error.txt`。

面板命令行参数可以临时覆盖行为，例如 `--no-autostart` 只对本次启动生效，不会改写 `autoStart`。

服务命令行（与界面操作共用同一个运行锁）：

| 命令 | 作用 |
| --- | --- |
| `--start` / `--stop` | 全部启动 / 全部停止 |
| `--reload` | 重新生成并应用当前 Web 服务器配置 |
| `--web-server nginx\|apache` | 切换 Web 服务器并写入 `webServer`；环境运行中会停止旧服务器、启动新服务器，失败时回退 |
| `--service web\|php\|php8.0\|php8.2\|php8.5\|mysql80\|mysql57\|dbpage --action start\|stop\|restart` | 单独启动、停止、重启一个服务；`php` 为全部项目的 PHP，`php8.2` 等只作用于使用该版本的已启用项目。MySQL 未运行时单独启动 PHP 不报错，项目建库推迟到该 MySQL 启动时补做 |

## Apache 2.4.39

- 组件目录：`soft\apache\2.4.39`（Apache Haus 64 位构建，来自 PHPStudy 扩展目录，不含旧日志）。
- 配置由面板生成到 `config\panel-apache.conf`；日志为 `logs\panel-apache-error.log`、`logs\panel-apache-access.log`。
- PHP 与 Nginx 模式相同，使用各项目独立的 php-cgi（`mod_proxy_fcgi`）；Windows 盘符路径会被 `mod_proxy_fcgi` 写成 `proxy:fcgi://…/D:/…`，配置中用 `ProxyFCGISetEnvIf` 修正 `SCRIPT_FILENAME` 并清除 `PATH_TRANSLATED`。
- 项目目录 `AllowOverride All`：伪静态以项目 `.htaccess` 为准（YikaiCMS 自带）。面板“伪静态配置”编辑的是 Nginx 规则，Apache 模式下只保存、不生效。
- 非 YikaiCMS 项目使用 `FallbackResource /index.php`，与 Nginx 通用模板一致；停用的项目返回 503；`/.` 开头的隐藏路径和 `/storage/` 拒绝访问。
- Windows 控制台模式的 Apache 不支持 `-k restart`，重载配置即先检查 `httpd -t`，再重启父子进程（有短暂中断）。
- 已知限制：`/x.php/附加路径` 这类 PATH_INFO 地址下，php-cgi 计算的 `PHP_SELF` 只含附加路径（`SCRIPT_NAME`、`PATH_INFO` 正确）。

## 在面板中编辑配置文件

入口：“工具 → 配置文件（php.ini 等）”，或状态栏服务菜单里的“编辑 php.ini / 编辑配置 / 编辑 my.ini”。

| 列表项 | 实际文件 | 保存时 |
| --- | --- | --- |
| PHP 8.0 / 8.2 / 8.5 · php.ini | `soft/php/<版本>/php.ini`（源文件） | 用该版本 php.exe 检查语法；重启正在运行的该版本 PHP |
| 数据库页面 · php.ini | `config/phpmyadmin-php.ini` | 检查语法；重启数据库页面 |
| Nginx · 自定义配置 | `config/custom-nginx.conf`，在生成的 http 块内 include | `nginx -t`；运行中重载。写了 `client_max_body_size` / `fastcgi_read_timeout` 时替换面板默认值 |
| Apache · 自定义配置 | `config/custom-apache.conf`，全局 IncludeOptional（VirtualHost 之前） | `httpd -t`；运行中重启 |
| MySQL 8.0 / 5.7 · 自定义配置 | `config/custom-mysql80.ini` / `custom-mysql57.ini`，追加到生成的 my.ini 之后 | 检查格式；运行中重启，启动失败自动恢复原配置并重新启动，报错显示 MySQL 日志中的 `[ERROR]` 行 |

- 保存前原文件备份到 `backups/config/`；检查或应用失败时不保留新内容。文件在面板外被改过时会先确认再覆盖。
- MySQL 5.7 / 8.0.12 没有可靠的离线参数校验（`--help --verbose` 对未知参数也成功，`--validate-config` 需 8.0.16+），所以参数错误要到重启时才会发现，面板会自动回滚。
- 生成文件（`panel-nginx.conf`、`panel-apache.conf`、`panel-mysql*.ini`、`panel-php-*.ini`）仍不要直接修改。
- 命令行：`--config-check <key> --input <文件>`、`--config-save <key> --input <文件>`，key 为 `php8.2`、`dbpage`、`nginx`、`apache`、`mysql80` 等。
- 验证：`preparation/apache-qa/verification-config-files.json`（38 项），界面截图 `preparation/ui-config-editor/`。

## 项目数据库字段

| 字段（`sites[]`） | 用途 |
| --- | --- |
| `databaseName` | 项目数据库名，1–64 位字母 / 数字 / 下划线，同一 MySQL 版本内不重复 |
| `databaseUser` | 项目专属 MySQL 用户（1–32 位，不能是 root）；为空时项目使用 root |
| `databasePassword` | 专属用户密码；修改后下次启动该项目 PHP 时同步到 MySQL |

面板在启动项目 PHP（或补建延迟的数据库）时执行：建库、`CREATE USER IF NOT EXISTS` / `ALTER USER`（localhost 与 127.0.0.1）、`GRANT ALL ON 库.*`，再用专属账号登录验证。root 密码写入临时客户端配置文件、SQL 走标准输入，不出现在命令行参数中。

## 项目 HTTPS 与证书

| 字段（`sites[]`） | 用途 |
| --- | --- |
| `https` | 是否为该项目开启 HTTPS |
| `httpsPort` | 项目的 HTTPS 端口，从 8443 开始分配，与其他项目不重复 |
| `certificateSource` | `auto`（本地根证书签发）或 `custom`（导入的证书） |

- 根证书：`config/ssl/ca/yikai-local-root.crt` 和 `.key`，有效期 10 年，只在缺失或即将过期时创建，不主动轮换（重建会让已有信任失效）。
- 项目证书：自动签发在 `config/ssl/sites/<项目ID>.crt|.key`，导入的在 `config/ssl/custom/<项目ID>.crt|.key`；`.crt` 里同时包含根证书，便于服务器发送证书链。
- 信任只写入当前用户的 `CurrentUser\Root`，由 Windows 弹窗确认；面板不会静默安装根证书，也不修改本机其他证书。
- 生成的 `panel-nginx.conf` / `panel-apache.conf` 会为开启 HTTPS 的项目追加 server / VirtualHost 段（TLS 1.2、1.3）；证书不可用时跳过该段并记入日志。


## PHP 扩展

- 版本级：面板改的是 `soft/php/<版本>/php.ini`：启用写成 `extension=<名字>`，opcache / xdebug / ionCube 这类写成 `zend_extension=<名字>`，关闭则在行首加 `;`。
- 项目级：`sites[].extensionsOn` / `sites[].extensionsOff` 记录相对该 PHP 版本的增减，两个都为空表示完全跟随版本设置。启动项目 PHP 时生成 `config/panel-php-<项目ID>.ini`（版本 php.ini + 本项目增减 + 安装向导预填脚本），php-cgi 用这个文件启动。
- php.ini 里没有的扩展在启用时追加到文件末尾的 `; 易开面板 · 扩展` 小节；同一个扩展有重复行时只保留第一行，其余注释掉。
- 必需扩展（面板数据库页面、安装向导和 YikaiCMS 依赖）不能在界面或命令行里关闭：mbstring、fileinfo、curl、openssl、gd、pdo_mysql、pdo_sqlite、sqlite3、zip、mysqli。
- 保存流程与“在面板中编辑配置文件”一致：先用该版本 php.exe 启动检查（扩展加载失败会报错并拒绝）、备份到 `backups/config`、写入、重启正在运行的该版本 PHP，失败自动恢复。
- 命令行：`--php-extensions <版本> [--enable a,b] [--disable c,d]`。
