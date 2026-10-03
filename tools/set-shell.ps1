<#
.SYNOPSIS
    Makes NeoShell the shell for the current user only.
.DESCRIPTION
    Writes the per-user HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell value.
    Takes effect at the next sign-in. Use only on a test account or a virtual machine.
    Undo with restore-explorer.ps1.
.EXAMPLE
    .\set-shell.ps1 -Path C:\NeoShell\NeoShell.exe
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$exe = (Resolve-Path -LiteralPath $Path).Path
if ([System.IO.Path]::GetExtension($exe) -ne '.exe') {
    throw "Not an .exe: $exe"
}

$key = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon'
if (-not (Test-Path $key)) {
    New-Item -Path $key -Force | Out-Null
}
Set-ItemProperty -Path $key -Name Shell -Value "`"$exe`"" -Type String

Write-Host "Shell for $env:USERNAME set to `"$exe`". Sign out and back in to use it."
Write-Host "To undo: Ctrl+Alt+Del > Task Manager > Run new task > explorer.exe, then run restore-explorer.ps1."
