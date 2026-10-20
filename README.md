# HardLinkAutoBackup

Windows GUI / 托盘硬链接防误删工具。C# · .NET 10 · WinForms。

## 使用

当前发布版本：1.1.1（简体中文）。

提供 Setup 安装版和 Portable 绿色版。安装版默认安装到当前用户目录；绿色版发布包解压后运行 `HardLinkAutoBackup.exe`，保留同目录全部文件。无需安装 .NET。
源目录与备份目录必须在同一 NTFS 卷，且互不嵌套。支持最多 100 个任务、文件事件增量扫描、周期完整扫描、登录自启和托盘驻留。

**硬链接共享内容：原地修改、加密和磁盘损坏会影响所有链接。它不是独立副本，也不提供历史版本。**

主界面与任务设置使用滑块控制自动备份；底部使用提醒可选择“不再提示”，并可在程序设置中恢复。

详见 [使用说明](docs/user-guide.md)、[交付验证](docs/release-validation.md)、[技术设计](docs/technical-design.md) 和 [变更记录](CHANGELOG.md)。

## 开发与验证

使用已安装的 .NET 10 SDK 或 Visual Studio 打开 `HardLinkAutoBackup.slnx`。

```powershell
.\scripts\Build.ps1
.\scripts\Test.ps1
.\scripts\Publish-Release.ps1
```

构建与发布脚本将缓存和临时文件限制在项目 `.local`，禁用 CLI 的开发证书生成、PATH 配置和遥测。
发布脚本从官方 NuGet 下载自包含运行时到项目内，不安装全局组件。
普通 VS 构建不含诊断入口；测试脚本构建启用 `EnableDiagnostics=true`。

运行数据位于可执行文件目录的 `data`。开发输出位于 `bin/obj`，验证数据位于 `.checks`，交付包位于 `artifacts`，均不纳入 Git。

## 发布维护

见 [双产物发布指南](docs/publishing.md)。GitHub Actions 自动验证并生成安装包、绿色版及 SHA256 清单。
