# Starts a program at medium integrity (as Explorer starts apps) from an elevated session, through a scheduled task
# that runs in the interactive session with a limited token. Testing NeoShell elevated proves nothing about UIPI,
# drag and drop or elevated windows, so start it, Explorer and test apps with this when the shell is elevated.
# Usage: start-medium.ps1 -Path <exe> [-Arguments '<args>']
param([Parameter(Mandatory)][string]$Path, [string]$Arguments = '')

$name = 'NeoShellStartMedium'
$action = New-ScheduledTaskAction -Execute $Path -Argument $(if ($Arguments) { $Arguments } else { ' ' }) `
    -WorkingDirectory (Split-Path $Path)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0
Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $name
Start-Sleep -Milliseconds 500
Unregister-ScheduledTask -TaskName $name -Confirm:$false
