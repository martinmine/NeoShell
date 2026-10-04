<#
.SYNOPSIS
    Replaces the running Explorer shell with NeoShell, for this session only.
.DESCRIPTION
    Closes Explorer's desktop and taskbar the way its hidden "Exit Explorer" command does (Ctrl+Shift+right-click on
    the taskbar), so Windows doesn't restart it, then starts NeoShell, which becomes the shell, and ends what's left of
    Explorer's shell process. File Explorer windows in that process close; those in their own process stay open.
    Nothing is written to the registry: after signing out, Explorer is the shell again.

    If NeoShell doesn't become the shell, Explorer is started again so the session isn't left without one.
    Back to Explorer: Start > Switch to Explorer, or Ctrl+Alt+Del > Task Manager > Run new task > explorer.exe.
.EXAMPLE
    .\start-shell.ps1
.EXAMPLE
    .\start-shell.ps1 -Path C:\NeoShell\NeoShell.exe
#>
param(
    [string]$Path = (Join-Path $PSScriptRoot '..\src\NeoShell\bin\Debug\net10.0-windows10.0.26100.0\win-x64\NeoShell.exe')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) {
    throw "NeoShell.exe not found at $Path. Build it first (dotnet build) or pass -Path."
}
$exe = (Resolve-Path -LiteralPath $Path).Path

Add-Type -Namespace NeoShellTools -Name Shell -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
'@

function Get-ShellProcessId {
    $window = [NeoShellTools.Shell]::GetShellWindow()
    if ($window -eq [IntPtr]::Zero) { return 0 }
    $processId = 0
    [NeoShellTools.Shell]::GetWindowThreadProcessId($window, [ref]$processId) | Out-Null
    return [int]$processId
}

function Wait-Until([scriptblock]$Condition, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

# A NeoShell running alongside Explorer has to go first: there's one instance per session.
if (Get-Process -Name NeoShell -ErrorAction SilentlyContinue) {
    Write-Host 'Closing the running NeoShell...'
    & $exe /exit
    if (-not (Wait-Until { -not (Get-Process -Name NeoShell -ErrorAction SilentlyContinue) } 15)) {
        throw 'The running NeoShell did not exit. Close it from its taskbar menu (Exit NeoShell) and try again.'
    }
}

$shellProcessId = Get-ShellProcessId
if ($shellProcessId -ne 0) {
    $shellName = (Get-Process -Id $shellProcessId).ProcessName
    if ($shellName -ne 'explorer') {
        throw "The shell is $shellName (process $shellProcessId), not Explorer; not replacing it."
    }

    Write-Host 'Closing Explorer''s desktop and taskbar...'
    # WM_USER + 436 is what "Exit Explorer" sends; unlike ending the process, Windows doesn't restart Explorer after it.
    $tray = [NeoShellTools.Shell]::FindWindow('Shell_TrayWnd', $null)
    if ($tray -eq [IntPtr]::Zero -or -not [NeoShellTools.Shell]::PostMessage($tray, 0x5B4, [IntPtr]::Zero, [IntPtr]::Zero) -or
        -not (Wait-Until { (Get-ShellProcessId) -eq 0 } 15)) {
        Write-Warning 'Explorer did not exit; ending its process. Windows may start it again.'
        Stop-Process -Id $shellProcessId -Force
        Wait-Until { (Get-ShellProcessId) -eq 0 } 5 | Out-Null
    }
}

Write-Host "Starting $exe..."
$neoShell = Start-Process -FilePath $exe -PassThru
# The first start after a build can take a while (the binaries get scanned).
$started = Wait-Until { (Get-ShellProcessId) -eq $neoShell.Id } 60

# After "Exit Explorer" the old process can live on with its DDE and desktop windows, and an Explorer started later
# (Switch to Explorer) hangs talking to it. It goes once NeoShell holds the shell role, so if Windows restarts Explorer
# for it, that Explorer finds a shell and doesn't put up a second taskbar.
if ($shellProcessId -ne 0 -and (Get-Process -Id $shellProcessId -ErrorAction SilentlyContinue)) {
    Stop-Process -Id $shellProcessId -Force
}

if ($started) {
    Write-Host 'NeoShell is the shell. Back to Explorer: Start > Switch to Explorer.'
}
else {
    Write-Warning 'NeoShell did not become the shell; starting Explorer again.'
    if ((Get-ShellProcessId) -eq 0) {
        Start-Process explorer.exe
    }
    exit 1
}
