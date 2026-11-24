param([switch]$CheckRunningOnly)
$ErrorActionPreference = 'Stop'
try {
    if (Get-Process key_flow -ErrorAction SilentlyContinue) {
        Write-Output 'Please close key_flow before installing or uninstalling.'
        exit 2
    }
    if ($CheckRunningOnly) { exit 0 }
    $service = Get-Service ddhid72000 -ErrorAction SilentlyContinue
    if (!$service) {
        $driverPath = Join-Path $PSScriptRoot 'driver'
        Start-Process -FilePath (Join-Path $driverPath 'ddc.exe') -WorkingDirectory $driverPath -WindowStyle Hidden | Out-Null
        $deadline = (Get-Date).AddSeconds(45)
        do {
            Start-Sleep -Milliseconds 500
            $service = Get-Service ddhid72000 -ErrorAction SilentlyContinue
        } until ($service -or (Get-Date) -gt $deadline)
    }
    if (!$service) { throw 'Required input component was not installed. Restart Windows and run setup again.' }
    if ($service.Status -ne 'Running') { Start-Service ddhid72000 }
    $service = Get-Service ddhid72000
    if ($service.Status -ne 'Running') { throw 'Required input component is not running.' }
    Write-Output 'Input component ready. No keyboard initialization was performed.'
    exit 0
} catch { Write-Output $_.Exception.Message; exit 1 }
