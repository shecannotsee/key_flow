$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$folder = Join-Path $repo '.artifacts/driver-build'
New-Item -ItemType Directory -Force $folder | Out-Null
$admin = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$secureBoot = 'Unknown'
try { $secureBoot = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' -Name UEFISecureBootEnabled).UEFISecureBootEnabled -eq 1 } catch {}
$memoryIntegrity = 'Unknown'
try { $dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard; $memoryIntegrity = $dg.SecurityServicesRunning -contains 2 } catch {}
$driver = Join-Path $repo '.artifacts/driver-package/InputProbeHid.sys'
$signature = if (Test-Path $driver) { Get-AuthenticodeSignature $driver } else { $null }
$report = [pscustomobject]@{
    CheckedAt = (Get-Date).ToString('o')
    Administrator = $admin
    SecureBootRegistry = $secureBoot
    MemoryIntegrityRunning = $memoryIntegrity
    DriverPresent = (Test-Path $driver)
    SignatureStatus = if ($signature) { $signature.Status.ToString() } else { 'Missing' }
    SignatureDetail = if ($signature) { $signature.StatusMessage } else { 'Missing driver package' }
    ReadOnly = $true
}
$report | ConvertTo-Json | Set-Content (Join-Path $folder 'host-readiness.json') -Encoding UTF8
$report | Format-List
Write-Host 'This check does not install a driver, enable test signing or change Secure Boot.'
Write-Host 'Authenticode status alone is not kernel load authorization; verify with SignTool /kp.'
