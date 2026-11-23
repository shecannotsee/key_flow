$ErrorActionPreference = 'Stop'
$appPath = Join-Path $PSScriptRoot 'src\InputProbe.App\bin\Debug\net8.0-windows\InputProbe.App.exe'
try {
    if (!(Test-Path -LiteralPath $appPath)) { throw 'Build the application first: dotnet build InputProbe.sln' }
    Start-Process -FilePath $appPath -WorkingDirectory (Split-Path $appPath) -Verb RunAs
} catch {
    Write-Host "Unable to start: $($_.Exception.Message)"
    Read-Host 'Press Enter to close'
}
