param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
. "$repo\scripts\msvc-environment.ps1"
if(!$OutputDirectory){$OutputDirectory=Join-Path ([IO.Path]::GetTempPath()) ('launchdeck-tls-tests-'+[guid]::NewGuid().ToString('N'))}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh test output directory.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
Push-Location $OutputDirectory
try {
 & cl.exe /nologo /utf-8 /MT /GS /W4 "/I$repo\native\proxybridge" "$repo\native\proxybridge\pb_tls.c" "$PSScriptRoot\TlsProxyTests.c" "/Fe:$OutputDirectory\TlsProxyTests.exe" /link ws2_32.lib
 if($LASTEXITCODE -ne 0){throw 'TLS fixture compilation failed.'}
 & "$OutputDirectory\TlsProxyTests.exe"
 if($LASTEXITCODE -ne 0){throw 'TLS/domain tunnel checks failed.'}
 Write-Host "Driver-free fixture: $OutputDirectory\TlsProxyTests.exe"
} finally {Pop-Location}
