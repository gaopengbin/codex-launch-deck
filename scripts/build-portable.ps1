param(
 [Parameter(Mandatory=$true)][string]$OutputDirectory,
 [Parameter(Mandatory=$true)][string]$NativePackage,
 [Parameter(Mandatory=$true)][string]$NativeArtifactsSource,
 [string]$Revision='portable-20261015-1'
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh portable output directory.'}
if($Revision -notmatch '^[a-z0-9-]{1,48}$'){throw 'Invalid portable revision.'}
[IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
$source=Get-Content -LiteralPath $NativeArtifactsSource -Raw
$nativeId=[regex]::Match($source,'BundleId="([a-z0-9-]+)"').Groups[1].Value
if(!$nativeId){throw 'Missing native bundle identity.'}
$bundleId=$nativeId+'-'+$Revision
$generated=Join-Path $OutputDirectory 'NativeArtifacts.generated.cs'
[IO.File]::WriteAllText($generated,$source.Replace('BundleId="'+$nativeId+'"','BundleId="'+$bundleId+'"'),[Text.UTF8Encoding]::new($true))
$payload=Join-Path $OutputDirectory 'payload'
& "$repo\build.ps1" -OutputDirectory $payload -NativeArtifactsSource $generated
if($LASTEXITCODE -ne 0){throw 'Portable launcher compilation failed.'}
foreach($name in @('process-proxy-native','LICENSES','SOURCE')){
 Copy-Item -LiteralPath (Join-Path $NativePackage $name) -Destination $payload -Recurse
}
Copy-Item -LiteralPath "$repo\docs\process-proxy.md" -Destination (Join-Path $payload 'README.md')
Copy-Item -LiteralPath "$repo\docs\portable.md" -Destination (Join-Path $payload 'PORTABLE.md')
$files=[ordered]@{}
Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object{
 $files[$_.FullName.Substring($payload.Length+1).Replace('\','/')]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
$manifest=Join-Path $payload 'bundle-manifest.json'
[IO.File]::WriteAllText($manifest,(@{BundleId=$bundleId;Files=$files}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($true))
$files['bundle-manifest.json']=(Get-FileHash -LiteralPath $manifest).Hash
$embeddedManifest=Join-Path $OutputDirectory 'embedded-manifest.json'
[IO.File]::WriteAllText($embeddedManifest,(@{BundleId=$bundleId;Files=$files}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($true))
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath=Join-Path $OutputDirectory 'payload.zip'
$zip=[IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($entry in $files.GetEnumerator()){
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $payload $entry.Key),$entry.Key,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
 }
}finally{$zip.Dispose()}
$compiler=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $OutputDirectory 'LaunchDeck.exe'
$common=@('/nologo','/platform:x64','/optimize+','/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',
 "/resource:$zipPath,payload.zip","/resource:$embeddedManifest,manifest.json",
 "$repo\src\PortableBootstrap.cs","$repo\src\ProcessProxy.cs","$repo\src\ProcessProxySecurity.cs","$repo\src\ProcessProxyDiagnostic.cs",$generated)
& $compiler /target:winexe "/win32icon:$repo\assets\app-icon.ico" "/out:$exe" @common
if($LASTEXITCODE -ne 0){throw 'Portable bootstrap compilation failed.'}
$tests=Join-Path $OutputDirectory 'PortableTests.exe'
& $compiler /target:exe /main:PortableTests "/out:$tests" @common "$repo\tests\PortableTests.cs"
if($LASTEXITCODE -ne 0){throw 'Portable tests compilation failed.'}
& $tests
if($LASTEXITCODE -ne 0){throw 'Portable verification failed.'}
((Get-FileHash -LiteralPath $exe).Hash+'  LaunchDeck.exe')|Set-Content -LiteralPath ($exe+'.sha256') -Encoding ASCII
Write-Host "Portable single EXE built and verified: $exe"
