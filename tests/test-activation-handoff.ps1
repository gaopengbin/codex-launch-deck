$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$taskOutput=Join-Path ([IO.Path]::GetTempPath()) ('launchdeck-activation-tests-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput)|Out-Null
$compiler=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo "/out:$taskOutput\ActivationTests.exe" "$repo\src\ActivationProcessResolver.cs" "$PSScriptRoot\ActivationProcessResolverTests.cs"
if($LASTEXITCODE -ne 0){throw 'Activation tests compilation failed.'}
& "$taskOutput\ActivationTests.exe"
if($LASTEXITCODE -ne 0){throw 'Activation handoff tests failed.'}
