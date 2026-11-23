param(
    [ValidateSet('DD', 'SendInput')][string]$Backend = 'DD',
    [switch]$Elevated,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot
$appPath = Join-Path $repoPath 'src/InputProbe.App/bin/Debug/net8.0-windows/InputProbe.App.exe'
$dllPath = Join-Path $repoPath 'dependencies/dd/hid.72000.dll'
try {
    if (!(Test-Path -LiteralPath $appPath)) { throw 'Build first: dotnet build InputProbe.sln' }
    $launchOptions = @{ FilePath = $appPath; WorkingDirectory = (Split-Path $appPath) }
    if ($Backend -eq 'DD') {
        if (!(Test-Path -LiteralPath $dllPath)) { throw 'DD DLL missing: dependencies/dd/hid.72000.dll. See dependencies/dd/README.md.' }
        $launchOptions.ArgumentList = @('--dd', ('"' + $dllPath + '"'))
    }
    if ($Elevated) { $launchOptions.Verb = 'RunAs' }
    if ($CheckOnly) {
        Write-Output "Ready: Backend=$Backend; Elevated=$Elevated; App=$appPath"
        if ($Backend -eq 'DD') { Write-Output "DLL=$dllPath" }
        exit 0
    }
    Start-Process @launchOptions
} catch {
    Write-Host "Unable to start: $($_.Exception.Message)"
    if (!$CheckOnly) { Read-Host 'Press Enter to close' | Out-Null }
    exit 1
}
