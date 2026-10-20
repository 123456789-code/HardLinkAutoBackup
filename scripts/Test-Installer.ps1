param([Parameter(Mandatory)][string]$ReleaseRoot)

$ErrorActionPreference = 'Stop'
# 此脚本会实际安装/卸载，只允许在 GitHub 托管的临时 Windows 环境运行。
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
	throw '安装测试仅允许在 GitHub 托管的临时构建环境执行。'
}
$installer = @(Get-ChildItem -LiteralPath $ReleaseRoot -Filter '*-Setup-x64.exe')
if ($installer.Count -ne 1) { throw '需要且只能有一个安装包。' }
$install_dir = Join-Path $env:RUNNER_TEMP 'HardLinkAutoBackup-install-check'
$log_dir = Join-Path $ReleaseRoot 'installer-test'
New-Item -ItemType Directory -Force -Path $log_dir | Out-Null
function Install-Package([string]$stage) {
	$arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $install_dir + '"'),('/LOG="' + (Join-Path $log_dir ($stage + '.log')) + '"'))
	$process = Start-Process -FilePath $installer[0].FullName -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
	if ($process.ExitCode -ne 0) { throw "安装失败：$stage，退出码 $($process.ExitCode)" }
}
Install-Package 'install'
$exe = Join-Path $install_dir 'HardLinkAutoBackup.exe'
$process = Start-Process -FilePath $exe -ArgumentList '--version' -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw '安装后程序启动失败。' }
$data_dir = Join-Path $install_dir 'data'
New-Item -ItemType Directory -Path $data_dir | Out-Null
$sentinel = Join-Path $data_dir 'settings.json'
'{"Version":1,"HideUsageReminder":true,"Tasks":[]}' | Set-Content -LiteralPath $sentinel -Encoding UTF8
$before = (Get-FileHash -LiteralPath $sentinel).Hash
Install-Package 'upgrade'
if ((Get-FileHash -LiteralPath $sentinel).Hash -ne $before) { throw '覆盖升级修改了用户配置。' }
$uninstaller = Join-Path $install_dir 'unins000.exe'
$process = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $log_dir 'uninstall.log') + '"') -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw '卸载失败。' }
if (Test-Path -LiteralPath $exe) { throw '卸载后仍残留主程序。' }
if ((Get-FileHash -LiteralPath $sentinel).Hash -ne $before) { throw '卸载修改或删除了用户配置。' }
'PASS 安装后启动；覆盖升级保留配置；卸载移除程序并保留配置。' | Set-Content -LiteralPath (Join-Path $log_dir 'results.txt') -Encoding UTF8
