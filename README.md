# NeoShell

A replacement for the Windows 11 `explorer.exe` shell, built with WinUI 3 and .NET 10.

- Wallpaper
- A Windows 11 style taskbar: pinned and running apps, grouping, live thumbnails, system tray, network, volume
  and microphone indicators, clock and calendar
- A Start menu with app and Windows Search results, Settings, Lock / Sign out / Sleep / Restart / Shut down,
  and a button to switch back to Explorer

> **Status:** in development. See [docs/plan.md](docs/plan.md) for progress, [docs/design.md](docs/design.md)
> for the design and [docs/architecture.md](docs/architecture.md) for the architecture and the parts of Windows it uses.

## Requirements

- Windows 11
- .NET 10 SDK

## Build and run

```
dotnet build
dotnet test
dotnet run --project src/NeoShell
```

Run normally, NeoShell starts **alongside Explorer**: its taskbar and Start menu work, while Explorer keeps the
desktop, the system tray and the Win key.

To stop it, use the taskbar menu → Exit, or run `NeoShell.exe /exit`. Don't kill the process: that leaves its
taskbar space reserved.

## Using NeoShell as the shell

**Only do this on a test account or a virtual machine, never on your main account.**

For this session only, run `tools\start-shell.ps1` (optionally `-Path <NeoShell.exe>`; it defaults to the Debug
build). It closes Explorer's desktop and taskbar and starts NeoShell as the shell; nothing is written to the
registry, so signing out brings Explorer back.

To make it the shell at every sign-in:

1. Publish or build NeoShell.
2. Signed in as the test user, run `tools\set-shell.ps1 -Path <full path to NeoShell.exe>`. This sets the
   per-user `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell` value only.
3. Sign out and back in.

To go back, use **Switch to Explorer** in the Start menu.

## Recovery

If you are left without a shell:

1. Press Ctrl+Alt+Del → Task Manager → Run new task → `explorer.exe`.
2. Run `tools\restore-explorer.ps1` (Windows PowerShell 5.1) to remove the per-user Shell value.
3. Sign out and back in.

If NeoShell crashes while it is the shell, it starts `explorer.exe` itself.
