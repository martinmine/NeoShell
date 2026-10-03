<#
.SYNOPSIS
    Restores Explorer as the shell for the current user.
.DESCRIPTION
    Removes the per-user HKCU\...\Winlogon\Shell value so the machine-wide default (explorer.exe) applies again.
    Takes effect at the next sign-in. If you have no shell right now, start one first:
    Ctrl+Alt+Del > Task Manager > Run new task > explorer.exe
#>

$ErrorActionPreference = 'Stop'

$key = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon'
$current = (Get-ItemProperty -Path $key -Name Shell -ErrorAction SilentlyContinue).Shell

if ($null -eq $current) {
    Write-Host "No per-user shell is set for $env:USERNAME; Explorer is already the shell."
}
else {
    Remove-ItemProperty -Path $key -Name Shell
    Write-Host "Removed per-user shell $current for $env:USERNAME. Sign out and back in to use Explorer."
}
