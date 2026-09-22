# 参与易开面板开发

感谢你改进易开面板。提交 Issue 或 Pull Request 前，请先确认问题能够在当前代码上复现，并避免附带真实站点、数据库、密码、私钥或其他个人数据。

## 开发环境

- Windows 10 或 Windows 11 x64
- .NET 9 SDK
- PowerShell 7 或 Windows PowerShell 5.1

基础构建：

```powershell
dotnet build src\YikaiPHP.csproj -c Release
```

自包含单文件发布：

```powershell
dotnet publish src\YikaiPHP.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None
```

## 提交前检查

1. 运行与改动相关的 `preparation/` 验收脚本。
2. 界面改动需检查中文、英文、日文以及不同字号和显示缩放。
3. 运行公开仓库安全检查：

```powershell
powershell -ExecutionPolicy Bypass -File preparation\github\check-publish.ps1
```

4. 不要提交 `soft/`、`wwwroot/`、`config/`、`data/`、`logs/`、`temp/`、`backups/`、安装包、证书、数据库或构建产物。

## 许可

除非另有明确说明，向本仓库提交并被接收的贡献依照 Apache License 2.0 第 5 条授权。项目名称和 Logo 的使用另见 [TRADEMARKS.md](TRADEMARKS.md)。
