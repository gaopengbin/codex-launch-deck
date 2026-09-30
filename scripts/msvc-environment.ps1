$ErrorActionPreference='Stop'
if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    $locator=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $locator)) { throw 'Install the Microsoft C++ x64 build tools before building.' }
    $installation=(& $locator -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    if (-not $installation) { throw 'Microsoft C++ x64 toolchain not found.' }
    $batch=Join-Path $installation 'VC\Auxiliary\Build\vcvars64.bat'
    $nativePathImported=$false
    & $env:ComSpec /d /c ('call "'+$batch+'" >nul && set') | ForEach-Object {
        if ($_ -match '^([^=]+)=(.*)$') {
            if ($matches[1] -ceq 'PATH') { $nativePathImported=$true }
            if ($matches[1] -ceq 'Path' -and $nativePathImported) { return }
            [Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process')
        }
    }
    if ($LASTEXITCODE -ne 0) { throw 'C++ environment setup failed.' }
}
