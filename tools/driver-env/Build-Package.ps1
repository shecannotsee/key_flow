param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
# Dot-source the compile check: reuse its resolved, version-pinned tool paths.
. (Join-Path $PSScriptRoot 'Build-Check.ps1') -SkipRestore:$SkipRestore
$package = Join-Path $repo '.artifacts/driver-package'
New-Item -ItemType Directory -Force $package | Out-Null
# A relink invalidates a previous catalog; never leave a stale signature beside new binaries.
$oldCatalog = Join-Path $package 'InputProbeHid.cat'
if (Test-Path -LiteralPath $oldCatalog) { Remove-Item -LiteralPath $oldCatalog }
$linkArgs = @('/NOLOGO','/DRIVER','/SUBSYSTEM:NATIVE,10.00','/MACHINE:X64','/ENTRY:FxDriverEntry','/NODEFAULTLIB','/INCREMENTAL:NO','/DYNAMICBASE','/NXCOMPAT','/WX',"/OUT:$package/InputProbeHid.sys", "$output/Driver.obj", "/LIBPATH:$wdk/Lib/10.0.26100.0/km/x64", "/LIBPATH:$wdk/Lib/wdf/kmdf/x64/1.33", 'ntoskrnl.lib','hal.lib','wmilib.lib','BufferOverflowFastFailK.lib','wdfdriverentry.lib','wdfldr.lib','vhfkm.lib')
& "$vc/bin/Hostx64/x64/link.exe" @linkArgs
if ($LASTEXITCODE -ne 0) { throw 'Driver link failed' }
Copy-Item (Join-Path $repo 'drivers/InputProbeHid/InputProbeHid.inf') $package -Force
& "$wdk/tools/10.0.26100.0/x64/infverif.exe" /u "$package/InputProbeHid.inf"
if ($LASTEXITCODE -ne 0) { throw 'INF verification failed' }
& "$vc/bin/Hostx64/x64/dumpbin.exe" /headers "$package/InputProbeHid.sys" | Set-Content "$output/pe-headers.txt"
if ($LASTEXITCODE -ne 0) { throw 'PE inspection failed' }
& "$vc/bin/Hostx64/x64/dumpbin.exe" /imports "$package/InputProbeHid.sys" | Set-Content "$output/pe-imports.txt"
if ($LASTEXITCODE -ne 0) { throw 'Import inspection failed' }
Write-Host "Linked unsigned driver and validated INF: $package"
