param([Parameter(Mandatory=$true)][string]$Installer, [Parameter(Mandatory=$true)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $ReportDirectory | Out-Null
Start-Transcript -Path (Join-Path $ReportDirectory 'lifecycle.log') -Force
function Assert-True($Condition, [string]$Message) { if (!$Condition) { throw $Message }; Write-Output "PASS: $Message" }
function Wait-Until([scriptblock]$Condition, [int]$Seconds = 30) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while (!( & $Condition )) { if ((Get-Date) -gt $deadline) { throw 'Condition timed out' }; Start-Sleep -Milliseconds 250 }
}
function Install-key_flow {
    $process = Start-Process -FilePath $Installer -ArgumentList '/S' -PassThru -Wait -WindowStyle Hidden
    Assert-True ($process.ExitCode -eq 0) "Installer exit code $($process.ExitCode)"
}
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    Assert-True ((New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) 'Lifecycle test is elevated'
    $regPath = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\key_flow'
    Assert-True (!(Test-Path $regPath)) 'No pre-existing key_flow install; safe to test uninstall'
    $configPath = Join-Path $env:LOCALAPPDATA 'key_flow/config.json'
    $before = if (Test-Path $configPath) { (Get-FileHash $configPath).Hash } else { '' }
    Install-key_flow
    $registration = Get-ItemProperty $regPath
    $installDir = $registration.InstallLocation
    Assert-True ($installDir -eq (Join-Path $env:ProgramFiles 'key_flow')) 'Install directory is Program Files/key_flow'
    Assert-True ($registration.DisplayName -eq 'key_flow') 'Uninstall entry has correct product name'
    Assert-True ($registration.UninstallString -eq ('"' + (Join-Path $installDir 'Uninstall.exe') + '"')) 'Uninstall command quotes the executable path'
    Assert-True (Test-Path ($registration.DisplayIcon -replace ',0$','')) 'Uninstall icon points to an installed icon'
    $shell = New-Object -ComObject WScript.Shell
    $links = @((Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'key_flow.lnk'), (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'key_flow/key_flow.lnk'))
    foreach ($link in $links) {
        Assert-True (Test-Path $link) "Shortcut exists: $link"
        $shortcut = $shell.CreateShortcut($link)
        Assert-True ($shortcut.TargetPath -eq (Join-Path $installDir 'key_flow.exe')) 'Shortcut target is key_flow.exe'
        Assert-True ($shortcut.IconLocation -match ('key_flow_' + [regex]::Escape($registration.DisplayVersion) + '\.ico')) 'Shortcut uses versioned key_flow icon'
    }
    Add-Type -AssemblyName System.Drawing
    foreach ($exe in @($Installer, (Join-Path $installDir 'key_flow.exe'), (Join-Path $installDir 'Uninstall.exe'))) {
        $icon = [Drawing.Icon]::ExtractAssociatedIcon($exe)
        Assert-True ($null -ne $icon) "Shell extracts executable icon: $exe"
        $bitmap = $icon.ToBitmap()
        $bitmap.Save((Join-Path $ReportDirectory ([IO.Path]::GetFileName($exe) + '.png')))
        $bitmap.Dispose(); $icon.Dispose()
    }
    $app = Start-Process -FilePath (Join-Path $installDir 'key_flow.exe') -PassThru
    Wait-Until { $app.Refresh(); $app.MainWindowHandle -ne 0 }
    Assert-True ($app.MainWindowTitle -match 'key_flow') 'Installed GUI opens with key_flow title'
    Start-Sleep -Seconds 2
    Assert-True (!$app.HasExited) 'Installed GUI remains running'
    $logPath = Join-Path $env:LOCALAPPDATA 'key_flow/key_flow.log'
    Assert-True (Test-Path $logPath) 'Installed app writes user log'
    Assert-True ((Get-Content $logPath -Tail 12 -Encoding UTF8) -match '等待用户点击初始化') 'App waits for preparation and does not automatically initialize output'
    Assert-True ($app.CloseMainWindow()) 'Window accepts normal close'
    Assert-True ($app.WaitForExit(10000)) 'Application exits cleanly'
    $uninstaller = Start-Process -FilePath (Join-Path $installDir 'Uninstall.exe') -ArgumentList '/S' -PassThru -WindowStyle Hidden
    $uninstaller.WaitForExit(30000) | Out-Null
    Wait-Until { !(Test-Path $regPath) }
    Wait-Until { !(Test-Path (Join-Path $installDir 'key_flow.exe')) }
    Assert-True (!(Test-Path $installDir)) 'Uninstall removes installation directory'
    foreach ($link in $links) { Assert-True (!(Test-Path $link)) 'Uninstall removes shortcut' }
    if ($before) { Assert-True ((Get-FileHash $configPath).Hash -eq $before) 'Uninstall preserves user configuration byte-for-byte' }
    Assert-True ((Get-Service ddhid72000).Status -eq 'Running') 'Shared input driver remains running'
    Install-key_flow
    Assert-True (Test-Path (Join-Path $installDir 'key_flow.exe')) 'Reinstallation succeeds'
    'PASS' | Set-Content (Join-Path $ReportDirectory 'result.txt')
} catch {
    $_ | Out-String | Set-Content (Join-Path $ReportDirectory 'failure.txt')
    'FAIL' | Set-Content (Join-Path $ReportDirectory 'result.txt')
    Write-Output $_
    exit 1
} finally { Stop-Transcript }
