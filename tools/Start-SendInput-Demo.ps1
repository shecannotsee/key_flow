param([switch]$VirtualKey, [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$demoProject = Join-Path $PSScriptRoot 'SendInputDemo/SendInputDemo.csproj'
$demoExe = Join-Path $PSScriptRoot 'SendInputDemo/bin/Debug/net8.0-windows/SendInputDemo.exe'
try {
    & dotnet build $demoProject --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $launch = @{ FilePath = $demoExe; WorkingDirectory = (Split-Path $demoExe); Verb = 'RunAs' }
    $demoArgs = @()
    if ($VirtualKey) { $demoArgs += '--vk' }
    if ($CheckOnly) { $demoArgs += '--check' }
    if ($demoArgs.Count) { $launch.ArgumentList = $demoArgs }
    Start-Process @launch
} catch {
    Write-Host "Demo not started: $($_.Exception.Message)"
    Read-Host 'Press Enter to close' | Out-Null
    exit 1
}
