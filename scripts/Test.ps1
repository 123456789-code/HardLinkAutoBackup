param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$project_root = Split-Path -Parent $PSScriptRoot
$program = Join-Path $project_root "bin\$Configuration\net10.0-windows\HardLinkAutoBackup.exe"
if (!(Test-Path -LiteralPath $program)) { throw '请先执行 Build.ps1。' }
$output = Join-Path $project_root '.checks'
$env:TEMP = Join-Path $project_root '.local\temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP, $output | Out-Null
$process = Start-Process -FilePath $program -ArgumentList '--self-test', ('"' + $output + '"') -Wait -PassThru -WindowStyle Hidden
Get-Content -LiteralPath (Join-Path $output 'latest-results.txt')
if ($process.ExitCode -ne 0) { throw '核心测试失败。' }
$preview = Join-Path $output 'preview'
$process = Start-Process -FilePath $program -ArgumentList '--render-preview', ('"' + $preview + '"') -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw '界面渲染失败，参见 preview/render-error.txt。' }
Write-Output "界面预览：$preview"
