param([string]$NsisPath, [string]$DriverSource)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$version = '1.5.13'
$buildRoot = Join-Path $repoRoot ('.artifacts/package-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $buildRoot 'payload'
$outputDir = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Force $payload,$outputDir | Out-Null
if (!$NsisPath) {
    $found = Get-Command makensis.exe -ErrorAction SilentlyContinue
    if ($found) { $NsisPath = $found.Source }
    else { $NsisPath = Join-Path $env:LOCALAPPDATA 'electron-builder/Cache/nsis/nsis-3.0.4.1/makensis.exe' }
}
if (!(Test-Path -LiteralPath $NsisPath)) { throw 'Install NSIS or supply -NsisPath.' }
if (!$DriverSource) { $DriverSource = Join-Path $repoRoot '.artifacts/dd-research/hid.drv' }
$dll = Join-Path $repoRoot 'dependencies/dd/hid.72000.dll'
$required = @($dll) + @('ddc.exe','ddhid72000.sys','ddhid72000.inf','ddhid72000.cat' | ForEach-Object { Join-Path $DriverSource $_ })
foreach ($file in $required) { if (!(Test-Path -LiteralPath $file)) { throw "Missing package prerequisite: $file" } }
foreach ($file in @($dll, (Join-Path $DriverSource 'ddc.exe'), (Join-Path $DriverSource 'ddhid72000.sys'), (Join-Path $DriverSource 'ddhid72000.cat'))) {
    if ((Get-AuthenticodeSignature -LiteralPath $file).Status -ne 'Valid') { throw "Invalid vendor signature: $file" }
}
& dotnet publish (Join-Path $repoRoot 'src/key_flow_app/key_flow_app.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $payload --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$ddTarget = Join-Path $payload 'dependencies/dd'
New-Item -ItemType Directory -Force $ddTarget,(Join-Path $payload 'driver') | Out-Null
Copy-Item -LiteralPath $dll -Destination $ddTarget
foreach ($name in @('ddc.exe','ddhid72000.sys','ddhid72000.inf','ddhid72000.cat')) { Copy-Item -LiteralPath (Join-Path $DriverSource $name) -Destination (Join-Path $payload 'driver') }
Copy-Item -LiteralPath (Join-Path $repoRoot 'installer/prepare_installation.ps1') -Destination $payload
Copy-Item -LiteralPath (Join-Path $repoRoot 'installer/THIRD-PARTY-NOTICES.txt') -Destination $payload
# Explicit file list: uninstall never recursively deletes arbitrary user files.
$removeList = Join-Path $buildRoot 'remove-files.nsh'
$lines = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object { 'Delete "$INSTDIR\' + $_.FullName.Substring($payload.Length + 1) + '"' })
$lines += @(Get-ChildItem -LiteralPath $payload -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object { 'RMDir "$INSTDIR\' + $_.FullName.Substring($payload.Length + 1) + '"' })
$lines | Set-Content -LiteralPath $removeList -Encoding utf8
$output = Join-Path $outputDir "key_flow_setup_${version}_x64.exe"
& $NsisPath "/DVERSION=$version" "/DOUTPUT=$output" "/DPAYLOAD=$payload" "/DICON=$(Join-Path $repoRoot 'resource/key_flow.ico')" "/DHELPER=$(Join-Path $repoRoot 'installer/prepare_installation.ps1')" "/DREMOVE_LIST=$removeList" (Join-Path $repoRoot 'installer/key_flow.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($output) | Set-Content -LiteralPath ($output + '.sha256') -Encoding ascii
Write-Output "Installer: $output"
Write-Output "Payload: $payload"
