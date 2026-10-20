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
.\scripts\Publish-Release.ps1 -Version 1.1.3 -InstallerCompiler '你自己的安装目录\ISCC.exe'
```
脚本不自动查找、下载或安装 Inno Setup。禁止在本机运行安装测试脚本；它仅允许在 GitHub 托管临时环境执行，避免改动维护者的快捷方式或卸载注册信息。

## 安装行为

默认当前用户目录：%LOCALAPPDATA%\Programs\HardLinkAutoBackup。安装不要求管理员权限、不自动注册自启、不自动打开程序。
同一个 AppId 支持覆盖升级；用户 data 不属于安装文件清单，升级与卸载不会删除它。备份文件也不会被卸载器清理。
升级前从托盘退出程序；卸载前关闭当前安装位置的登录自启并保存，然后退出。卸载检查查询 Windows 实际计划任务；任务指向其他程序位置时不阻止当前版本卸载。
安装器不会自动结束扫描、修改目录权限、删除计划任务或删除用户配置。卸载失败时先处理提示再重试。

## 安装提示“拒绝访问”

先关闭安装程序，不要选择“跳过此文件”。从右下角托盘退出旧程序后重试。
默认安装目录位于当前用户目录，不需要管理员权限。若自选 Program Files 等受保护目录，请右键安装包，选择“以管理员身份运行”，使用同一 Windows 账户，再选择原目录升级。
安装器保留当前用户安装范围，不自动切换账户、不修改目录权限。若不希望提升权限，返回并选择默认用户目录；迁移目录需自行保留原 data，并在程序内重新同步登录自启路径。
1.1.3 会在复制程序文件前验证写入能力。即使可创建新文件，旧 unins*.exe / unins*.dat 只读或被占用也会提前拦截；提示会列出具体失败路径。已经以管理员运行仍失败时，应处理该文件的只读、占用或访问限制。
若安全软件告警，先停止操作并核查告警，不添加白名单。安装前检查通过后权限或占用仍可能变化，此时应取消安装并处理实际错误，不能跳过文件。
