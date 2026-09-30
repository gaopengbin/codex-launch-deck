param([string]$OutputRoot,[string]$DependencyCache)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\msvc-environment.ps1"
if(!$OutputRoot){$OutputRoot=Join-Path $repo 'output\enhanced'}
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $OutputRoot){throw 'Enhanced output already exists; use a fresh output directory.'}
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
if(!$DependencyCache){$DependencyCache=Join-Path $OutputRoot 'dependencies'}
New-Item -ItemType Directory -Force -Path $DependencyCache | Out-Null
function Dependency($name,$url,$hash){
 $path=Join-Path $DependencyCache $name
 if(!(Test-Path -LiteralPath $path)){[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing -Headers @{'User-Agent'='LaunchDeck-release-build'}}
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash){throw "Dependency hash mismatch: $name"}
 return $path
}
$binaryZip=Dependency 'windivert.zip' 'https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip' '63CB41763BB4B20F600B6DE04E991A9C2BE73279E317D4D82F237B150C5F3F15'
$sourceZip=Dependency 'windivert-source.zip' 'https://api.github.com/repos/basil00/WinDivert/zipball/v2.2.2' '366F83F7243E3BB4972A25AF2329C6CBD761BBA215A00F9F38D9D45603D9D51D'
$unpacked=Join-Path $OutputRoot 'windivert';Expand-Archive -LiteralPath $binaryZip -DestinationPath $unpacked
$wd=Join-Path $unpacked 'WinDivert-2.2.2-A'
$compile=Join-Path $OutputRoot 'compile';New-Item -ItemType Directory $compile | Out-Null
$core=Join-Path $repo 'native\proxybridge'
$sources=@('ProxyBridge.c','pb_util.c','pb_process.c','pb_rules.c','pb_proxy.c','pb_dns.c','pb_socks5.c','pb_http.c','pb_conntrack.c','pb_relay.c') | ForEach-Object {Join-Path $core $_}
Push-Location $compile
try{
 & cl.exe /nologo /utf-8 /O2 /MT /GS /guard:cf /W3 /D_CRT_SECURE_NO_WARNINGS /D_WINSOCK_DEPRECATED_NO_WARNINGS /DPROXYBRIDGE_EXPORTS /DNDEBUG "/I$wd\include" @sources /LD /link /DYNAMICBASE /HIGHENTROPYVA /NXCOMPAT /guard:cf /CETCOMPAT "/LIBPATH:$wd\x64" WinDivert.lib ws2_32.lib iphlpapi.lib "/OUT:$compile\ProxyBridge.dll"
 if($LASTEXITCODE -ne 0){throw 'ProxyBridge compilation failed.'}
}finally{Pop-Location}
$pins=[ordered]@{ 'ProxyBridge.dll'=(Get-FileHash "$compile\ProxyBridge.dll").Hash;'WinDivert.dll'=(Get-FileHash "$wd\x64\WinDivert.dll").Hash;'WinDivert64.sys'=(Get-FileHash "$wd\x64\WinDivert64.sys").Hash }
$bundleId=$pins['ProxyBridge.dll'].Substring(0,16).ToLowerInvariant()
$generated=Join-Path $OutputRoot 'NativeArtifacts.generated.cs'
$pairs=($pins.GetEnumerator()|ForEach-Object{'{"'+$_.Key+'","'+$_.Value+'"}'}) -join ','
('namespace LaunchDeck.ProcessProxy { internal static class NativeArtifacts { public const string BundleId="'+$bundleId+'"; public const string DriverSigner="043589F75FCE2795E7F2CC3E526D46784D5DDAB3"; public static System.Collections.Generic.Dictionary<string,string> Pins(){return new System.Collections.Generic.Dictionary<string,string>{'+$pairs+'};} } }') | Set-Content -LiteralPath $generated -Encoding UTF8
$package=Join-Path $OutputRoot 'package';New-Item -ItemType Directory $package | Out-Null
& "$repo\build.ps1" -OutputDirectory $package -NativeArtifactsSource $generated
if($LASTEXITCODE -ne 0){throw 'Managed release compilation failed.'}
$native=Join-Path $package 'process-proxy-native';New-Item -ItemType Directory $native | Out-Null
Copy-Item "$compile\ProxyBridge.dll","$wd\x64\WinDivert.dll","$wd\x64\WinDivert64.sys" $native
$signature=Get-AuthenticodeSignature -LiteralPath "$native\WinDivert64.sys"
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne '043589F75FCE2795E7F2CC3E526D46784D5DDAB3'){throw 'Official driver signature failed; do not distribute.'}
$files=[ordered]@{}
Get-ChildItem $package -File -Recurse | Sort-Object FullName | ForEach-Object {$files[$_.FullName.Substring($package.Length+1)]=(Get-FileHash $_.FullName).Hash}
$manifest=Join-Path $package 'bundle-manifest.json'
@{BundleId=$bundleId;Files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding UTF8
$files['bundle-manifest.json']=(Get-FileHash $manifest).Hash
$template=Get-Content "$PSScriptRoot\Install-Enhanced.template.ps1" -Raw
$template.Replace('@@BUNDLE@@',$bundleId).Replace('@@FILES@@',($files|ConvertTo-Json -Compress)) | Set-Content "$package\Install-Enhanced.ps1" -Encoding UTF8
New-Item -ItemType Directory "$package\LICENSES","$package\SOURCE" | Out-Null
Copy-Item "$repo\LICENSE" "$package\LICENSES\LaunchDeck-MIT.txt"
Copy-Item "$core\LICENSE" "$package\LICENSES\ProxyBridge-MIT.txt"
Copy-Item "$wd\LICENSE" "$package\LICENSES\WinDivert-LICENSE.txt"
Copy-Item $sourceZip "$package\SOURCE\WinDivert-v2.2.2-source.zip"
Copy-Item $core "$package\SOURCE\proxybridge" -Recurse
Copy-Item "$repo\docs\process-proxy.md" "$package\README.md"
@{BundleId=$bundleId;DriverSignature=$signature.Status.ToString();DriverSigner=$signature.SignerCertificate.Subject;DriverThumbprint=$signature.SignerCertificate.Thumbprint;DriverSHA256=$pins['WinDivert64.sys'];ProxyBridgeUpstream='02703a0672a8b94011a4698368a392f7734c10dc';WinDivertVersion='2.2.2-A';DriverLoaded=$false} | ConvertTo-Json | Set-Content "$package\provenance.json" -Encoding UTF8
$checksums=Get-ChildItem $package -File -Recurse | Sort-Object FullName | ForEach-Object{(Get-FileHash $_.FullName).Hash+'  '+$_.FullName.Substring($package.Length+1).Replace('\','/')}
$checksums | Set-Content "$package\SHA256SUMS.txt" -Encoding ASCII
$zip=Join-Path (Split-Path -Parent $OutputRoot) 'LaunchDeck-Enhanced-win-x64.zip'
if(Test-Path $zip){throw 'Release ZIP already exists; refusing overwrite.'}
Compress-Archive -Path "$package\*" -DestinationPath $zip
((Get-FileHash $zip).Hash+'  '+[IO.Path]::GetFileName($zip)) | Set-Content ($zip+'.sha256') -Encoding ASCII
Write-Host "Enhanced release built without loading the driver. Bundle: $bundleId"
