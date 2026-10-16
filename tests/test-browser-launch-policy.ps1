$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$folder=Join-Path ([IO.Path]::GetTempPath()) ('launchdeck-browser-policy-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($folder)|Out-Null
$compiler=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo "/out:$folder\BrowserReadLaunchPolicyTests.exe" "$repo\src\BrowserReadLaunchPolicy.cs" "$repo\tests\BrowserReadLaunchPolicyTests.cs"
if($LASTEXITCODE -ne 0){throw 'Browser launch policy compilation failed.'}
& "$folder\BrowserReadLaunchPolicyTests.exe"
if($LASTEXITCODE -ne 0){throw 'Browser launch policy tests failed.'}
