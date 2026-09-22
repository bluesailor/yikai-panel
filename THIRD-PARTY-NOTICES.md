# 第三方组件说明 / Third-party notices

本仓库（易开面板源码）包含或引用下列第三方内容。安装包会另外分发 Adminer、PHP、MySQL、
Nginx、Apache HTTP Server 等运行组件；这些组件的上游源码或二进制通常不在本仓库内，
其许可证、来源和校验信息见安装包中的说明文件与 `preparation/component-manifest.json`。

## 本仓库内包含的第三方文件

### Inno Setup 简体中文语言文件

- 文件：`preparation/release-0.7.0/installer/ChineseSimplified.isl`
- 来源：Inno Setup 源码仓库（<https://github.com/jrsoftware/issrc>，`Files/Languages/ChineseSimplified.isl`）
- 许可：随 Inno Setup 分发（Inno Setup License）；仅用于编译安装器界面，未修改内容。

## 安装包分发、但不在本仓库内的组件

Release 里的安装包（`YikaiPanel-<版本>-setup-x64.exe`）会把这些组件装到用户机器上。
它们的许可证原文随组件一起安装（例如 `soft/mysql/8.0/LICENSE`、`soft/mysql/5.7/COPYING`、
`soft/apache/2.4.39/LICENSE`、`soft/apache/2.4.39/NOTICE`），来源与校验值记录在
`preparation/component-manifest.json`。

| 组件 | 版本 | 许可 |
| --- | --- | --- |
| Adminer | 6.1.0 | Apache License 2.0 或 GPL 2（二选一；本项目按 Apache License 2.0 使用） |
| PHP | 8.0.2 / 8.2.33 / 8.5.9 | PHP License 3.01 |
| MySQL Community Server | 5.7.26 / 8.0.12 | GPL v2 |
| Nginx | 1.30.4 | BSD 2-Clause |
| Apache HTTP Server（Apache Haus 构建） | 2.4.39 | Apache License 2.0 |
| HeidiSQL（便携版，桌面数据库客户端） | 12.21 | GPL v2（`soft/heidisql/gpl.txt`、`license.txt`；源码 <https://github.com/HeidiSQL/HeidiSQL>） |
| Visual C++ 运行库安装器 | 14.44.x | Microsoft 可再分发条款 |
| YikaiCMS 安装模板 | 1.20.0 | 《YikaiCMS 软件许可协议》（商业许可，非本仓库内容） |

Adminer 的未修改上游文件随安装包放在 `soft/db-manager/vendor/adminer.php`，来源为
<https://github.com/vrana/adminer/releases/download/v6.1.0/adminer-6.1.0.php>，SHA-256 为
`95bf24b510b41904446f720f4f1212c9e28b1d523f3df44259480d7a12ea181e`。
仓库里的 `src/Assets/DatabaseTools/adminer.php` 与 `src/db-manager/adminer.php` 是易开面板编写的
集成包装脚本，并不是该上游 Adminer 文件。

## 品牌与商标

“易开面板 / Yikai Panel”“YikaiCMS”名称与图标属于 Yikai。Apache License 2.0 不授予这些
名称与标识的商标使用权；合理署名和第三方修改版的品牌规则见 `TRADEMARKS.md`。
