# 易开面板在线更新接口

项目官网：https://panel.yikai.cn

更新目录：https://panel.yikai.cn/update/

面板读取：https://panel.yikai.cn/update/latest.json

当前仅在用户点击“检查更新”时请求。服务器尚未配置，客户端会显示“更新服务尚未就绪，请稍后再试”。本次没有配置服务器或发布在线文件。

## 服务端最小配置

静态网站即可，无需 PHP 接口。准备最新面板 EXE 和 `latest.json`，使用 HTTPS 返回 JSON 与下载文件。

```json
{
  "version": "0.6.1",
  "downloadUrl": "https://panel.yikai.cn/update/YikaiLocal-0.6.1.exe",
  "sha256": "这里替换为 EXE 的 64 位 SHA-256 十六进制值",
  "notes": "更新内容第一行\n更新内容第二行"
}
```

以上版本和文件名是示例。`version` 必须与下载 EXE 的 Windows 文件版本一致，接受三段或四段数字；例如 0.6.1 与 0.6.1.0 等价。`notes` 是纯文本，换行使用 JSON 的 `\n`。

生成校验值：

```powershell
(Get-FileHash -LiteralPath 'YikaiLocal-0.6.1.exe' -Algorithm SHA256).Hash
```

建议先上传 EXE，再上传对应 `latest.json`，清单使用短缓存或 `Cache-Control: no-cache`。此处只定义接口，不自动上传。

## 客户端行为

1. 顶部“升级” → “检查更新”；相同或更低版本显示已是最新版本。
2. 找到新版后显示版本与更新说明，再由用户点击“下载并升级”。
3. 下载到 `D:\yikai\temp\updates`，验证 SHA-256 和 EXE 文件版本。清单最大 64 KB，面板 EXE 最大 256 MB。
4. 辅助程序等待当前面板退出，备份旧 EXE 到 `D:\yikai\backups\panel-update-*`，替换并启动新版。
5. 替换失败或新版启动后立即报错退出时，恢复旧 EXE。结果写入 `D:\yikai\temp\updates\result.json`；较晚出现的应用问题需通过备份手动回退。

此接口升级桌面面板 EXE，0.6.1 起附带必要的小型数据库连接脚本，在首次改密时校验、备份并更新。PHP、MySQL、Nginx 运行时及网站数据不包含在这一更新包中；完整环境包的发行另行处理。

## 已验证范围

使用隔离目录与测试 EXE 验证下载校验、版本匹配、成功替换、文件篡改拒绝及新版立即启动失败后的回退。在线站点尚未配置，未进行真实服务器下载联调。
