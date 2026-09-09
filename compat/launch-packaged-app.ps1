param([Parameter(Mandatory=$true)][string]$ProxyUrl, [switch]$Probe)
$ErrorActionPreference = 'Stop'
$package = @(Get-AppxPackage -Name OpenAI.Codex; Get-AppxPackage -Name OpenAI.ChatGPT-Desktop) | Sort-Object Version -Descending | Select-Object -First 1
if (-not $package) { throw 'No registered desktop package found.' }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $package.InstallLocation 'AppxManifest.xml')
$application = @($manifest.Package.Applications.Application)[0]
$executable = Join-Path $package.InstallLocation $application.Executable
$resultPath = Join-Path ([IO.Path]::GetTempPath()) ('launchdeck-' + [guid]::NewGuid().ToString('N') + '.json')
function Literal([string]$value) { return "'" + $value.Replace("'", "''") + "'" }
# The package activation broker does not inherit the caller's environment.
$child = '$ErrorActionPreference="Stop";' + '$resultPath=' + (Literal $resultPath) + ';$executable=' + (Literal $executable) + ';$proxy=' + (Literal $ProxyUrl) + ';$probe=$' + $Probe.IsPresent.ToString().ToLowerInvariant() + ';'
$child += @'
try {
    foreach ($key in @('HTTP_PROXY','HTTPS_PROXY','http_proxy','https_proxy')) {
        [Environment]::SetEnvironmentVariable($key, $proxy, 'Process')
    }
    $env:NO_PROXY = 'localhost,127.0.0.1,::1'
    if ($probe) {
        Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class PackageLaunchProbe {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct SI { public int cb; public string r,d,t; public int x,y,w,h,xc,yc,f,flags; public short show,res; public IntPtr reserved,stdin,stdout,stderr; }
 [StructLayout(LayoutKind.Sequential)] public struct PI { public IntPtr process,thread; public int pid,tid; }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,string cmd,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref SI si,out PI pi);
 [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr p,uint code);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr p);
 public static void Test(string path) { SI s=new SI(); s.cb=Marshal.SizeOf(s); PI p; if(!CreateProcess(path,null,IntPtr.Zero,IntPtr.Zero,false,4,IntPtr.Zero,null,ref s,out p)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); try { if(!TerminateProcess(p.process,0)) throw new Exception("Probe cleanup failed"); } finally { CloseHandle(p.thread); CloseHandle(p.process); } }
}
"@
        [PackageLaunchProbe]::Test($executable)
        $result = @{ok=$true; probe=$true; proxy=$env:HTTPS_PROXY}
    } else {
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = $executable
        $info.UseShellExecute = $false
        $process = [Diagnostics.Process]::Start($info)
        $result = @{ok=$true; pid=$process.Id}
        $process.Dispose()
    }
} catch { $result = @{ok=$false; error=$_.Exception.ToString()} }
[IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Compress))
'@
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($child))
$wrapperPath = [IO.Path]::ChangeExtension($resultPath, '.vbs')
try {
    # The GUI script host creates PowerShell hidden from its first frame.
    $command = '"' + "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" + '" -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand ' + $encoded
    $wrapper = 'CreateObject("WScript.Shell").Run "' + $command.Replace('"', '""') + '", 0, True'
    [IO.File]::WriteAllText($wrapperPath, $wrapper, [Text.Encoding]::Unicode)
    Invoke-CommandInDesktopPackage -PackageFamilyName $package.PackageFamilyName -AppId $application.Id -Command "$env:SystemRoot\System32\wscript.exe" -Args "//B //Nologo `"$wrapperPath`"" -PreventBreakaway
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $result = $null
    while ($timer.Elapsed.TotalSeconds -lt 20) {
        if (Test-Path -LiteralPath $resultPath) {
            try { $result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json; if ($null -ne $result.ok) { break } } catch { }
        }
        Start-Sleep -Milliseconds 100
    }
    if ($null -eq $result) { throw 'Package launch timed out. Check whether the app started before retrying.' }
    if (-not $result.ok) { throw $result.error }
    $result | ConvertTo-Json -Compress
} finally {
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
    if (Test-Path -LiteralPath $wrapperPath) { Remove-Item -LiteralPath $wrapperPath }
}
