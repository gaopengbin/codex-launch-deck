param([string]$OutputDirectory, [string]$NativeArtifactsSource)
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectDir 'output' }
$artifactsSource = if ($NativeArtifactsSource) { [IO.Path]::GetFullPath($NativeArtifactsSource) } else { Join-Path $projectDir 'src\ProcessProxyArtifacts.cs' }
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$packageLauncher = Join-Path $outputDir 'PackagedChatGPTLauncher.exe'
& (Join-Path $projectDir 'build-package-launcher.ps1') -OutputDirectory $outputDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$windowsDir = Split-Path -Parent ([Environment]::SystemDirectory)
& "$windowsDir\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
    /nologo /target:winexe /platform:x64 /optimize+ `
    /win32icon:"$projectDir\assets\app-icon.ico" `
    /out:"$outputDir\ChatGPTProxyLauncher.exe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
    "$projectDir\src\Program.cs" "$projectDir\src\ProcessProxy.cs" "$projectDir\src\ProcessProxyController.cs" "$projectDir\src\ProcessProxySecurity.cs" $artifactsSource "$projectDir\src\ProcessProxyDiagnostic.cs"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$trustedClientHash = (Get-FileHash -LiteralPath "$outputDir\ChatGPTProxyLauncher.exe" -Algorithm SHA256).Hash
$trustedClientSource = Join-Path ([IO.Path]::GetTempPath()) ('launchdeck-trusted-client-' + [Guid]::NewGuid().ToString('N') + '.cs')
('namespace LaunchDeck.ProcessProxy { internal static class TrustedClient { public const string Sha256="' + $trustedClientHash + '"; } }') | Set-Content -LiteralPath $trustedClientSource -Encoding UTF8
try {
    & "$windowsDir\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll "/out:$outputDir\ProcessProxyHost.exe" "$projectDir\src\ProcessProxy.cs" "$projectDir\src\ProcessProxyHost.cs" "$projectDir\src\ProcessProxyBroker.cs" "$projectDir\src\ProcessProxyApproval.cs" "$projectDir\src\ProcessProxySecurity.cs" $artifactsSource "$projectDir\src\ProcessProxyDiagnostic.cs" $trustedClientSource
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} finally { Remove-Item -LiteralPath $trustedClientSource -ErrorAction SilentlyContinue }

$themeSource = Join-Path $projectDir 'themes\miku-future-beats-1.2.3.codedrobe-theme'
if (Test-Path -LiteralPath $themeSource) {
    $themeOutput = Join-Path $outputDir 'themes'
    if (Test-Path -LiteralPath $themeOutput) {
        $resolvedTheme=[IO.Path]::GetFullPath($themeOutput)
        if ($resolvedTheme -ne ([IO.Path]::GetFullPath($outputDir).TrimEnd('\')+'\themes')) { throw 'Unsafe theme output path.' }
        Remove-Item -LiteralPath $resolvedTheme -Recurse -Force
    }
    New-Item -ItemType Directory -Path $themeOutput -Force | Out-Null
    Copy-Item -LiteralPath $themeSource -Destination $themeOutput -Force
}

Write-Host "Built: $outputDir\ChatGPTProxyLauncher.exe"
