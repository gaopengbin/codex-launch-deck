param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) {$OutputDirectory=Join-Path ([IO.Path]::GetTempPath()) ('ld-cua-'+[Guid]::NewGuid().ToString('N').Substring(0,12))}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /reference:System.Web.Extensions.dll "/out:$OutputDirectory\CuaProxyForwardingTests.exe" "$repo\src\CuaProxyForwardingMaintenance.cs" "$repo\tests\CuaProxyForwardingTests.cs"
if ($LASTEXITCODE -ne 0) {throw 'CUA maintenance test compilation failed.'}
& "$OutputDirectory\CuaProxyForwardingTests.exe" (Join-Path $OutputDirectory 'f')
if ($LASTEXITCODE -ne 0) {throw 'CUA maintenance tests failed.'}
