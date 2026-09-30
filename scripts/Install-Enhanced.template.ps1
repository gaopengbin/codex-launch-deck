param([switch]$Elevated)
$ErrorActionPreference='Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell.' }
$bundleId='@@BUNDLE@@'
$expected=ConvertFrom-Json '@@FILES@@'
$root=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'LaunchDeckProcessProxy'
$destination=Join-Path $root $bundleId
$admins=[Security.Principal.SecurityIdentifier]'S-1-5-32-544'
$system=[Security.Principal.SecurityIdentifier]'S-1-5-18'
$users=[Security.Principal.SecurityIdentifier]'S-1-5-32-545'
function NoRedirect($path) {
 $current=[IO.Path]::GetFullPath($path)
 while($current) {
  if(Test-Path -LiteralPath $current) { if((Get-Item -LiteralPath $current).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse-point installation refused.' } }
  $current=Split-Path -Parent $current
 }
}
function Protect($path,$directory) {
 if($directory){$acl=New-Object Security.AccessControl.DirectorySecurity;$inherit=[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'}else{$acl=New-Object Security.AccessControl.FileSecurity;$inherit=[Security.AccessControl.InheritanceFlags]::None}
 $acl.SetOwner($admins);$acl.SetAccessRuleProtection($true,$false)
 foreach($sid in @($admins,$system)) { $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)) }
 $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($users,[Security.AccessControl.FileSystemRights]::ReadAndExecute,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
 Set-Acl -LiteralPath $path -AclObject $acl
}
function CheckProtection($path) {
 NoRedirect $path
 $acl=Get-Acl -LiteralPath $path
 if($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $admins.Value -or !$acl.AreAccessRulesProtected) { throw 'Existing installation is not protected; no changes made to it.' }
 $rules=@($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
 if($rules.Count -ne 3) { throw 'Unexpected installation ACL.' }
 foreach($rule in $rules) {
  if($rule.IdentityReference.Value -notin @($admins.Value,$system.Value,$users.Value) -or $rule.AccessControlType -ne 'Allow') { throw 'Unexpected installation principal.' }
  $rights=if($rule.IdentityReference.Value -eq $users.Value){[Security.AccessControl.FileSystemRights]::ReadAndExecute}else{[Security.AccessControl.FileSystemRights]::FullControl}
  if(($rule.FileSystemRights -band (-bnot [Security.AccessControl.FileSystemRights]::Synchronize)) -ne ($rights -band (-bnot [Security.AccessControl.FileSystemRights]::Synchronize))) { throw 'Unexpected installation rights.' }
 }
}
$locks=@{}
try {
 foreach($entry in $expected.PSObject.Properties) {
  if($entry.Name -match '(^[\\/]|\.\.|:)' ) { throw 'Unsafe component name.' }
  $path=Join-Path $PSScriptRoot $entry.Name;NoRedirect $path
  $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  $locks[$entry.Name]=$stream
  $hash=[Security.Cryptography.SHA256]::Create()
  try { $actual=([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-','') } finally { $hash.Dispose() }
  if($actual -ne $entry.Value) { throw 'Release component hash mismatch.' };$stream.Position=0
 }
 if(!$Elevated) {
  $powershell=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
  $process=Start-Process -FilePath $powershell -Verb RunAs -ArgumentList ('-NoProfile -File "'+$PSCommandPath+'" -Elevated') -WorkingDirectory ([Environment]::SystemDirectory) -WindowStyle Hidden -PassThru
  $process.WaitForExit();if($process.ExitCode -ne 0) { throw 'Installation stopped. Do not retry until the reported conflict is resolved.' }
  Write-Host ('Installed: '+$destination+'\ChatGPTProxyLauncher.exe');return
 }
 $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
 if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Normal administrator approval was not obtained.' }
 if(@(Get-CimInstance Win32_SystemDriver -Filter "Name LIKE '%WinDivert%'" -OperationTimeoutSec 20).Count -or @(Get-CimInstance Win32_Service -Filter "Name LIKE '%WinDivert%'" -OperationTimeoutSec 20).Count -or @(Get-Process ProcessProxyHost -ErrorAction SilentlyContinue).Count) { throw 'Existing WinDivert or proxy host conflict; no service or process was changed.' }
 if(@(Get-NetTCPConnection | Where-Object {$_.LocalPort -eq 34010 -or $_.RemotePort -eq 34010}).Count) { throw 'Relay port conflict; no service was changed.' }
 NoRedirect $root
 if(Test-Path -LiteralPath $destination) { throw 'This version already exists; it will not be overwritten.' }
 if(Test-Path -LiteralPath $root) {CheckProtection $root}else{New-Item -ItemType Directory -Path $root | Out-Null;Protect $root $true;CheckProtection $root}
 New-Item -ItemType Directory -Path $destination | Out-Null;Protect $destination $true;CheckProtection $destination
 foreach($entry in $expected.PSObject.Properties) {
  $path=Join-Path $destination $entry.Name;$parent=Split-Path -Parent $path
  if(!(Test-Path -LiteralPath $parent)) {New-Item -ItemType Directory -Path $parent | Out-Null;Protect $parent $true;CheckProtection $parent}
  $output=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try {$locks[$entry.Name].CopyTo($output)}finally{$output.Dispose()}
  Protect $path $false;CheckProtection $path
  if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Value) { throw 'Installed component hash mismatch; keep files for inspection.' }
 }
 $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $destination 'process-proxy-native\WinDivert64.sys')
 if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne '043589F75FCE2795E7F2CC3E526D46784D5DDAB3') { throw 'Official driver signature validation failed. Nothing was started.' }
 Write-Host 'Installation verified. No driver, proxy host, client, system proxy, TUN or autostart was started or changed.'
} finally { foreach($stream in $locks.Values) { $stream.Dispose() } }
