param(
    [switch]$Elevated, # Retained for compatibility; elevation is now always requested.
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot
$appPath = Join-Path $repoPath 'src/key_flow_app/bin/Debug/net8.0-windows/key_flow.exe'
$dllPath = Join-Path $repoPath 'dependencies/dd/hid.72000.dll'
try {
    if (!$CheckOnly) {
        $running = Get-Process key_flow -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appPath.Replace('/', '\') }
        if ($running) { throw 'Close the existing key_flow window before restarting.' }
        & dotnet build (Join-Path $repoPath 'src/key_flow_app/key_flow_app.csproj') --nologo --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    }
    if (!(Test-Path -LiteralPath $appPath)) { throw 'Build first: dotnet build key_flow.sln' }
    $launchOptions = @{ FilePath = $appPath; WorkingDirectory = (Split-Path $appPath) }
    if (!(Test-Path -LiteralPath $dllPath)) { throw 'DD DLL missing: dependencies/dd/hid.72000.dll. See dependencies/dd/README.md.' }
    $launchOptions.ArgumentList = @('--dd', ('"' + $dllPath + '"'))
    $launchOptions.Verb = 'RunAs'
    if ($CheckOnly) {
        Write-Output "Ready: DD HID; Elevated=True; App=$appPath"
        Write-Output "DLL=$dllPath"
        exit 0
    }
    Start-Process @launchOptions
} catch {
    $launchError = $_.Exception
    while ($launchError.InnerException) { $launchError = $launchError.InnerException }
    if ($launchError -is [System.ComponentModel.Win32Exception] -and $launchError.NativeErrorCode -eq 1223) {
        Write-Host 'Administrator permission was declined. key_flow was not started. Game hotkeys may not work without elevation.'
    } else {
        Write-Host "Unable to start: $($_.Exception.Message)"
    }
    if (!$CheckOnly) { Read-Host 'Press Enter to close' | Out-Null }
    exit 1
}
