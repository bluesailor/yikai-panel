# 易开面板开发文档

## 1. 技术基线

- C#、.NET 9、WinForms、Windows x64。
- 源码：`D:\yikai-soft\dev\yikai-panel\src`。
- 不增加运行期网络依赖；组件从本地 `soft` 和安装包提供。
- 当前用户界面默认中文，同时保留 English / 日本語。
- 程序内部命名空间暂保留 `YikaiLocal`，这是兼容旧资源和旧升级逻辑的技术标识，不等于对外品牌。

## 2. 主要模块

| 文件 | 职责 |
| --- | --- |
| `MainForm.cs` / `MainLayout.cs` | 主窗口、工具栏、项目工作区和状态栏 |
| `Runtime.cs` | Nginx、PHP-CGI、MySQL 进程的启动、接管、停止和日志 |
| `WebServer.cs` | Web 服务器二选一（Nginx / Apache 2.4.39）、Apache 配置生成、单个服务启动 / 停止 / 重启 |
| `ServiceMenus.cs` | 状态栏服务菜单（含 PHP 8.0 / 8.2 / 8.5 按版本启停）、Web 服务器、新项目默认 PHP 与默认 MySQL 选择 |
| `Settings.cs` | `config/panel.json`、项目、端口和数据库配置 |
| `PhpStudyImport.cs` / `PhpStudyDialog.cs` | PHPStudy 扫描、选择和导入 |
| `SiteScan.cs` / `SiteScanDialog.cs` | 扫描已有目录批量登记项目（带点直接用域名、无点可选补 `.yikai`、只登记不改动原目录内容）；跳过原因带固定英文标识（`plain-name` / `name-conflict` / `not-a-domain` …）供命令行与验收脚本判断 |
| `DatabaseScanDialog.cs` | 源码数据库配置扫描辅助 |
| `BundledDatabaseTools.cs` | 将三语数据库页面和 Adminer 写入 `soft/db-manager` |
| `PanelUpdate.cs` | 更新清单、下载校验、备份替换和失败恢复 |
| `Branding.cs` / `UiIcons.cs` | Logo、图标和品牌显示 |
| `CmsVersion.cs` / `ProjectHeadingLabel.cs` | 读取项目 YikaiCMS 版本（`config/version.php`，按修改时间缓存）并在项目标题旁绘制版本徽标 |

## 3. 运行模型

每个服务由面板启动并记录到 `temp/panel-processes.json`，记录包含逻辑键、PID、启动时间和可执行文件路径。面板重新打开时只接管仍符合三项记录的进程，不能凭进程名误认其他程序。

服务键约定：

- `nginx` / `apache`（按 `webServer` 二选一，切换时先停旧服务器）
- `php-<site-id>`
- `php-db`
- `mysql80`
- `mysql57`

单实例与“再点一次调出窗口”：同一根目录用一个命名互斥量（名字含根目录路径哈希）保证只有一个面板实例；第二次启动通过命名事件 `YikaiLocal-Show-<哈希>` 通知运行中的实例把窗口调到前台（`MainForm.AcceptShowSignal` + `ShowPanel`），然后静默退出（不再弹提示框）。换 `--root` 是另一个根目录，属于独立实例。验证脚本：`preparation/panel-single-instance/`。

MySQL 5.7 和 8.0 必须使用不同的数据目录、配置文件和端口。默认端口当前为 3307 / 3308，实际端口以 `panel.json` 为准。项目网站端口默认从 8081 起、HTTPS 从 8443 起自动分配；可在项目设置 / SSL 窗口里指定常用端口（80 / 443），MySQL 可在“配置 → 数据库端口”里指定（例如 3306）。指定过的端口记在 `sites[].portPinned` / `mysql57PortPinned` / `mysql80PortPinned`，运行期不再自动避让：被占用时启动失败并指名占用者（见 README「常用端口」）。两套实例可以同时运行，但下一阶段 UI 默认采用二选一启动，避免普通用户误以为“切换”会自动迁移数据。

## 4. 服务控制设计约束

服务动作必须经过同一个运行锁，避免面板、托盘和命令行同时改进程状态。按钮文字依据 `Runtime` 的实际状态刷新，不使用“点击后直接翻转文字”的乐观状态。

- 启动：不存在同一逻辑键的存活进程时才启动。
- 停止：只停止面板拥有且记录匹配的进程。
- 重启：停止后重新读取配置并启动，失败时保留日志和原配置。
- Nginx reload：先 `-t` 检查配置，通过后再 reload；失败恢复旧配置。
- 暂停：暂不挂起 PHP / MySQL 进程。项目维护模式应通过 Nginx 返回维护页实现。

## 5. 构建

在 `D:\yikai-soft\dev\yikai-panel\src` 执行：

```powershell
dotnet publish YikaiPHP.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None `
  -p:NuGetAudit=false -o publish
```

当前兼容入口仍输出 `YikaiLocal.exe`，直到升级器、快捷方式和安装器完成新文件名迁移。任何改动都应先在 staging 目录构建，再用带备份和 hash 检查的部署脚本更新 `D:\yikai`。

## 6. 验证层次

1. `dotnet build` / `dotnet publish`：编译和自包含发布。
2. WinForms `DrawToBitmap`：144 DPI 下检查窗口、按钮、文字和三语布局。
3. 运行时冒烟：启动 / 停止 Nginx、PHP、MySQL，确认 PID 和端口。
4. HTTP 冒烟：网站、后台、数据库页面、Adminer 返回 200。
5. 迁移回归：文件 hash、表和行数、NULL / 二进制、视图 / 触发器等。
6. 发布验收：新装、升级、失败恢复和官网链接全部指向实际文件。

测试报告放在 `D:\yikai-soft\dev\yikai-panel\preparation`；正式文档只引用可复现的报告路径，不把历史截图当作当前功能证据。

## 7. 界面配色（暖色系，2026-09-17）

颜色全部集中在 `src/Palette.cs`，参考用户提供的暖色界面：奶白主区、暖灰侧栏、白色卡片 + 细浅边框、近黑主按钮、赤陶色强调。只借鉴配色，不使用参考图的品牌元素；易开 Logo 保持不变。

| 名称 | 色值 | 用途 |
| --- | --- | --- |
| `Workspace` / `Canvas` | `#FAF9F6` | 主工作区、对话框背景、标题栏 |
| `Surface` | `#F4F2EE` | 左栏、顶部工具栏、状态栏、菜单 |
| `Card` / `Selected` / `Input` | `#FFFFFF` | 项目卡片、选中项目、输入框 |
| `Divider` / `Border` | `#E8E4DD` / `#DDD8CF` | 卡片与选中项细边框 / 按钮与输入框边框 |
| `ButtonFill` / `ButtonHover` / `ButtonPressed` | `#F7F5F1` / `#EDE9E3` / `#E4DFD7` | 普通按钮 |
| `Accent` / `AccentHover` / `OnAccent` | `#2B2A27` / `#3B3935` / `#FAF9F6` | 主按钮（新建 / 接入项目） |
| `Brand` / `Link` | `#A04830` | 网址、YikaiCMS 版本徽标、星标、输入焦点框 |
| `Text` / `Secondary` | `#1F1E1C` / `#6B665F` | 正文 / 辅助文字 |
| `Success` / `Warning` | `#2F6B4A` / `#85581A` | 运行中 / 警告状态 |

对比度（WCAG）：正文在各背景 ≥ 13.6:1；辅助文字 ≥ 4.66:1；赤陶色在白底 5.4:1、侧栏 5.4:1、徽标底 5.3:1；主按钮文字 13.6:1。新增颜色应先算对比度，辅助文字不低于 4.5:1。

截图与三语界面检查：`preparation/ui-warm-palette/`。数据库管理网页（`Assets/DatabaseTools/style.css`）尚未同步此配色。

按钮规则（src/ThemeButton.cs）：带图标的按钮图标 + 文字靠左；纯文字按钮（对话框底部等）居中；主按钮（Accent 近黑底）文字加粗、无浅色描边，禁用时保持同一形态、底色按 32% 浓度淡化；默认圆角 6，左栏“新建 / 接入项目”为 48 高、圆角 8、内容居中；键盘焦点为赤陶色圆角描边。截图：preparation/ui-dialog-buttons/。
