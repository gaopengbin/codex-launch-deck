$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
. "$repo\scripts\msvc-environment.ps1"
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('launchdeck-native-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $fixture | Out-Null
python "$PSScriptRoot\native-process-proxy\generate-native-tests.py" $fixture
if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed.' }
Push-Location $fixture
try {
 & cl.exe /nologo /utf-8 /MT /GS /W3 "/I$fixture" "$fixture\LifecycleFixture.c" /LD /link ws2_32.lib "/OUT:$fixture\LifecycleFixture.dll"
 if ($LASTEXITCODE -ne 0) { throw 'Native lifecycle fixture compile failed.' }
 & cl.exe /nologo /utf-8 /MT /GS /W3 "/I$fixture" "$fixture\NativeSafetyTests.c" "/Fe:$fixture\NativeSafetyTests.exe"
 if ($LASTEXITCODE -ne 0) { throw 'Native safety fixture compile failed.' }
 & "$fixture\NativeSafetyTests.exe"
 if ($LASTEXITCODE -ne 0) { throw 'Native isolated safety tests failed.' }
} finally { Pop-Location }
