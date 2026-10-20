param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
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
$local_feed = Join-Path $project_root '.local\empty-feed'
New-Item -ItemType Directory -Force -Path $env:TEMP, $local_feed | Out-Null
dotnet restore HardLinkAutoBackup.csproj --source $local_feed --disable-parallel -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw '还原失败；需要已安装的 .NET 10 SDK 和 Windows Desktop 目标包。此脚本不会安装系统组件。' }
dotnet build HardLinkAutoBackup.csproj --no-restore -c $Configuration --disable-build-servers -p:UseSharedCompilation=false -p:EnableDiagnostics=true
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Output (Join-Path $project_root "bin\$Configuration\net10.0-windows\HardLinkAutoBackup.exe")
