param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$packages = Join-Path $repo '.artifacts/driver-packages'
$version = '10.0.26100.6584'
if (!$SkipRestore) {
    & dotnet restore (Join-Path $PSScriptRoot 'Dependencies.csproj') --packages $packages --source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw 'WDK/SDK restore failed' }
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'VS 2022 C++ Build Tools not found' }
$vcVersion = (Get-Content (Join-Path $vs 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
$vc = Join-Path $vs "VC/Tools/MSVC/$vcVersion"
$wdk = Join-Path $packages "microsoft.windows.wdk.x64/$version/c"
$sdk = Join-Path $packages "microsoft.windows.sdk.cpp/$version/c"
$output = Join-Path $repo '.artifacts/driver-build'
New-Item -ItemType Directory -Force $output | Out-Null
$includes = @("$wdk/Include/10.0.26100.0/km", "$wdk/Include/10.0.26100.0/km/crt", "$wdk/Include/wdf/kmdf/1.33", "$sdk/Include/10.0.26100.0/shared", "$sdk/Include/10.0.26100.0/ucrt", "$vc/include")
$compilerArgs = @('/nologo','/c','/kernel','/W4','/WX','/wd4324','/GS','/D_AMD64_','/DAMD64','/D_WIN64','/DWINVER=0x0A00','/D_WIN32_WINNT=0x0A00','/DNTDDI_VERSION=0x0A000000',"/Fo$output/Driver.obj")
$compilerArgs += $includes | ForEach-Object { "/I$_" }
$compilerArgs += Join-Path $repo 'drivers/InputProbeHid/Driver.c'
& "$vc/bin/Hostx64/x64/cl.exe" @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Driver compile check failed' }
& (Join-Path $vs 'MSBuild/Current/Bin/MSBuild.exe') -version -nologo
if ($LASTEXITCODE -ne 0) { throw 'MSBuild unavailable' }
Write-Host "Driver compiled: $output/Driver.obj"
Write-Host 'Compile-only verification. No driver installed or signed.'

