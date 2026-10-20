param([string]$Version = '1.1.4', [string]$InstallerCompiler)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号格式应为 1.0.0。' }
$project_root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $project_root
$env:DOTNET_CLI_HOME = Join-Path $project_root '.local\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
$env:NUGET_PACKAGES = Join-Path $project_root '.local\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $project_root '.local\nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $project_root '.local\nuget-plugins'
$env:TEMP = Join-Path $project_root '.local\temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
$release_root = Join-Path $project_root ("artifacts\v" + $Version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$package_name = "HardLinkAutoBackup-$Version-Portable-x64"
$publish = Join-Path $release_root $package_name
if (Test-Path -LiteralPath $release_root) { throw '发布目录已存在，请稍后重试；不会覆盖旧包及用户 data。' }
New-Item -ItemType Directory -Path $release_root | Out-Null

# 仅向项目 .local 下载官方 .NET 运行时包，不执行系统安装。
dotnet restore HardLinkAutoBackup.csproj -r win-x64 --source https://api.nuget.org/v3/index.json --disable-parallel -p:NuGetAudit=false -p:SelfContained=true
if ($LASTEXITCODE -ne 0) { throw '发布还原失败。' }
dotnet publish HardLinkAutoBackup.csproj --no-restore -c Release -r win-x64 --self-contained true -o $publish --disable-build-servers -p:UseSharedCompilation=false -p:EnableDiagnostics=false -p:Version=$Version -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw '发布编译失败。' }
$unexpected_resources = Get-ChildItem -LiteralPath $publish -File -Recurse -Filter '*.resources.dll' | Where-Object { $_.Directory.Name -notin @('zh-Hans', 'zh-CN') }
if ($unexpected_resources) { throw '发布包包含非简体中文卫星资源，已停止打包。' }
Copy-Item -LiteralPath (Join-Path $project_root 'docs\user-guide.md') -Destination (Join-Path $publish '使用说明.md')
Copy-Item -LiteralPath (Join-Path $project_root 'CHANGELOG.md') -Destination $publish
Copy-Item -LiteralPath (Join-Path $project_root 'docs\release-validation.md') -Destination (Join-Path $publish '验证记录.md')
Copy-Item -LiteralPath (Join-Path $project_root 'LICENSE.txt') -Destination $publish
$runtime_config = Get-Content -LiteralPath (Join-Path $publish 'HardLinkAutoBackup.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtime_config.runtimeOptions.includedFrameworks) {
	$package_id = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
	$package_dir = Join-Path $env:NUGET_PACKAGES ($package_id + '\' + $framework.version)
	$license_dir = Join-Path $publish ('licenses\' + $framework.name)
	New-Item -ItemType Directory -Path $license_dir | Out-Null
	$licenses = Get-ChildItem -LiteralPath $package_dir -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' }
	if (!$licenses) { throw "运行时包缺少许可证：$package_id" }
	foreach ($license in $licenses) { Copy-Item -LiteralPath $license.FullName -Destination $license_dir }
}
$manifest = Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object FullName | ForEach-Object {
	$relative = $_.FullName.Substring($publish.Length + 1)
	((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) + '  ' + $relative
}
$manifest | Set-Content -LiteralPath (Join-Path $publish 'SHA256SUMS.txt') -Encoding UTF8
$process = Start-Process -FilePath (Join-Path $publish 'HardLinkAutoBackup.exe') -ArgumentList '--version' -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw '自包含运行时启动检查失败。' }
if (Test-Path -LiteralPath (Join-Path $publish 'data')) { throw '发布包意外含用户数据，已停止打包。' }
$archive = Join-Path $release_root ($package_name + '.zip')
Compress-Archive -LiteralPath $publish -DestinationPath $archive -CompressionLevel Optimal
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
($hash.Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $archive)) | Set-Content -LiteralPath ($archive + '.sha256') -Encoding UTF8
$assets = @($archive)
if ($InstallerCompiler) {
	if (!(Test-Path -LiteralPath $InstallerCompiler -PathType Leaf)) { throw '找不到指定的 Inno Setup 编译器。' }
	& $InstallerCompiler "/DAppVersion=$Version" "/DSourceDir=$publish" "/DReleaseDir=$release_root" (Join-Path $project_root 'installer\HardLinkAutoBackup.iss')
	if ($LASTEXITCODE -ne 0) { throw '安装包编译失败。' }
	$assets += Join-Path $release_root "HardLinkAutoBackup-$Version-Setup-x64.exe"
}
$assets | ForEach-Object {
	((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()) + '  ' + (Split-Path -Leaf $_)
} | Set-Content -LiteralPath (Join-Path $release_root 'SHA256SUMS.txt') -Encoding UTF8
if ($env:GITHUB_OUTPUT) {
	"release_root=$release_root" >> $env:GITHUB_OUTPUT
	"version=$Version" >> $env:GITHUB_OUTPUT
}
Write-Output "发布目录：$publish"
Write-Output "交付压缩包：$archive"
Write-Output "SHA256：$($hash.Hash)"
