# 发布安装版与绿色版

## GitHub 自动发布（推荐）

工作流：`.github/workflows/release.yml`，使用 GitHub 托管 Windows 2025 环境及其预装 Inno Setup。不会在维护者电脑安装打包工具。

每次发布：
1. 修改项目 Version、Publish-Release.ps1 默认版本及用户文档中的版本号。
2. 更新 CHANGELOG.md 与 docs/release-notes.md。
3. 提交并推送 main。普通提交只生成验证记录和 Actions 构建附件。
4. 在 Actions → Build and release → Run workflow 选择 main，勾选 publish；或者创建并推送与项目版本一致的 vX.Y.Z 标签。
5. 也可使用包含 `[release]` 的 main 提交信息显式发起发布。本次初始发布采用此方式。
6. 等待 build 和 release 两个任务成功，再在 Releases 下载。已有同名 Release 不覆盖，修复后需用新版本；失败留下的草稿需先核查并手动删除后重跑。

工作流依次执行：诊断构建 → 核心与布局检查 → 自包含绿色版 → 中文安装版 → 安装/覆盖升级/卸载测试 → 哈希校验 → 上传全部文件到 Release 草稿 → 公开发布。

最终附件只有：
- HardLinkAutoBackup-X.Y.Z-Setup-x64.exe
- HardLinkAutoBackup-X.Y.Z-Portable-x64.zip
- SHA256SUMS.txt

GitHub 自动生成的 Source code 附件是源码，不是绿色版。Release 绑定触发构建的准确提交。标签版本必须与项目 Version 一致。工作流默认只读权限，仅最终发布任务有 contents: write，且只用于本仓库；无需个人访问令牌。

## 本地打包

`scripts/Publish-Release.ps1` 默认只构建绿色版。如已自行安装 Inno Setup 6.7 或更高版本，可显式传入编译器：
```powershell
.\scripts\Publish-Release.ps1 -Version 1.1.2 -InstallerCompiler '你自己的安装目录\ISCC.exe'
```
脚本不自动查找、下载或安装 Inno Setup。禁止在本机运行安装测试脚本；它仅允许在 GitHub 托管临时环境执行，避免改动维护者的快捷方式或卸载注册信息。

## 安装行为

默认当前用户目录：%LOCALAPPDATA%\Programs\HardLinkAutoBackup。安装不要求管理员权限、不自动注册自启、不自动打开程序。
同一个 AppId 支持覆盖升级；用户 data 不属于安装文件清单，升级与卸载不会删除它。备份文件也不会被卸载器清理。
升级前从托盘退出程序；卸载前关闭当前安装位置的登录自启并保存，然后退出。卸载检查查询 Windows 实际计划任务；任务指向其他程序位置时不阻止当前版本卸载。
安装器不会自动结束扫描、修改目录权限、删除计划任务或删除用户配置。卸载失败时先处理提示再重试。
