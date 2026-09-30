$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testOutput = Join-Path ([IO.Path]::GetTempPath()) ('launchdeck-process-proxy-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testOutput | Out-Null
$compiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll "/out:$testOutput\ProcessProxyTests.exe" "$repo\src\ProcessProxy.cs" "$repo\src\ProcessProxyController.cs" "$repo\src\ProcessProxySecurity.cs" "$repo\src\ProcessProxyBroker.cs" "$repo\src\ProcessProxyApproval.cs" "$repo\src\ProcessProxyArtifacts.cs" "$repo\src\ProcessProxyDiagnostic.cs" "$repo\tests\ProcessProxyTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
& "$testOutput\ProcessProxyTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Isolated tests failed.' }
Write-Host "Isolated outputs retained at $testOutput"
