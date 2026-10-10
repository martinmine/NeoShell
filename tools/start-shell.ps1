<#
.SYNOPSIS
    Replaces the running Explorer shell with NeoShell, for this session only.
.DESCRIPTION
    Closes Explorer's desktop and taskbar the way its hidden "Exit Explorer" command does (Ctrl+Shift+right-click on
    the taskbar), so Windows doesn't restart it, then starts NeoShell, which becomes the shell, and ends what's left of
    Explorer's shell process. File Explorer windows in that process close; those in their own process stay open.
    The shell isn't changed in the registry: after signing out, Explorer is the shell again. The one registry change is
    machine-wide: Windows restarts a shell that ends unexpectedly (AutoRestartShell), which would bring Explorer back
    when its leftover process is ended, so the script asks to turn that off and does it from an elevated PowerShell.

    If NeoShell doesn't become the shell, Explorer is started again so the session isn't left without one.
    Back to Explorer: Start > Switch to Explorer, or Ctrl+Alt+Del > Task Manager > Run new task > explorer.exe.
.EXAMPLE
    .\start-shell.ps1
.EXAMPLE
    .\start-shell.ps1 -Path C:\NeoShell\NeoShell.exe
#>
param(
    [string]$Path
)

$ErrorActionPreference = 'Stop'

if (-not $Path) {
    # The Debug build for the machine's own architecture, as Directory.Build.props picks it. The machine's, not this
    # PowerShell's: an x64 PowerShell runs emulated on ARM64 and says AMD64.
    $machine = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE
    $architecture = if ($machine -eq 'ARM64') { 'arm64' } else { 'x64' }
    $Path = Join-Path $PSScriptRoot "..\src\NeoShell\bin\Debug\net10.0-windows10.0.26100.0\win-$architecture\NeoShell.exe"
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "NeoShell.exe not found at $Path. Build it first (dotnet build) or pass -Path."
}
$exe = (Resolve-Path -LiteralPath $Path).Path

# Ending Explorer's leftover process below counts as the shell stopping unexpectedly; unless AutoRestartShell is 0,
# Windows then starts a new Explorer that puts its taskbar back next to NeoShell's and takes the Win-key hotkeys.
$winlogonKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
function Get-AutoRestartShell {
    (Get-ItemProperty -LiteralPath $winlogonKey -Name AutoRestartShell -ErrorAction SilentlyContinue).AutoRestartShell
}
if ((Get-AutoRestartShell) -ne 0) {
    Write-Host 'Windows restarts Explorer when it ends unexpectedly (AutoRestartShell), so it would come back next to NeoShell.'
    Write-Host 'Turning this off is machine-wide and needs administrator rights: if Explorer crashes later, for any user,'
    Write-Host 'start it again from Task Manager (Run new task > explorer.exe). To undo, set AutoRestartShell back to 1.'
    if ((Read-Host 'Turn off AutoRestartShell? [y/N]') -notmatch '^(y|yes)$') {
        throw 'AutoRestartShell is on; NeoShell was not started.'
    }
    $command = "Set-ItemProperty -LiteralPath '$winlogonKey' -Name AutoRestartShell -Value 0 -Type DWord"
    try {
        Start-Process -FilePath powershell.exe -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList "-NoProfile -Command $command"
    }
    catch {
        throw "AutoRestartShell was not turned off ($($_.Exception.Message)); NeoShell was not started."
    }
    if ((Get-AutoRestartShell) -ne 0) {
        throw 'AutoRestartShell is still on; NeoShell was not started.'
    }
    Write-Host 'AutoRestartShell is off.'
}

# A type can't be redefined in a PowerShell session, so a second run in the same window reuses the first one's.
if (-not ('NeoShellTools.Shell' -as [type])) {
    Add-Type -Namespace NeoShellTools -Name Shell -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
'@
}

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
    # One that is still starting has no window to take the request yet (/exit then fails), so ask until it's taken.
    Wait-Until { & $exe /exit; $LASTEXITCODE -eq 0 } 15 | Out-Null
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
        Stop-Process -Id $shellProcessId -Force -ErrorAction SilentlyContinue
        Wait-Until { (Get-ShellProcessId) -eq 0 } 5 | Out-Null
    }
}

Write-Host "Starting $exe..."
$neoShell = Start-Process -FilePath $exe -PassThru
# The first start after a build can take a while (the binaries get scanned).
$started = Wait-Until { (Get-ShellProcessId) -eq $neoShell.Id } 60

# After "Exit Explorer" the old process can live on with its DDE and desktop windows, and an Explorer started later
# (Switch to Explorer) hangs talking to it. It goes once NeoShell holds the shell role; with AutoRestartShell off,
# Windows doesn't start a new Explorer for it. It may already have gone by itself, so a missing process isn't an error.
if ($shellProcessId -ne 0) {
    Stop-Process -Id $shellProcessId -Force -ErrorAction SilentlyContinue
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
