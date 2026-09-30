param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectDir 'compat' }
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$compiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $outputDir 'PackagedChatGPTLauncher.exe'
& $compiler /nologo /target:exe /optimize+ "/out:$output" (Join-Path $projectDir 'src\PackagedChatGPTLauncher.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Built: $output"
