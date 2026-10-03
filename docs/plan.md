# NeoShell plan

Milestones in order. Each one builds cleanly, passes `dotnet test`, and is committed on its own.
Tick items off as they land. The feature details are in [design.md](design.md).

## 0. Scaffold

- [ ] `NeoShell.slnx`, `Directory.Build.props` (TFM, nullable, implicit usings, warnings as errors, `LangVersion latest`)
- [ ] `src/NeoShell` — WinUI 3, unpackaged, self-contained Windows App SDK, custom `Main`
- [ ] `src/NeoShell.Interop` — class library, `AllowUnsafeBlocks`, `InternalsVisibleTo` tests
- [ ] `tests/NeoShell.Tests` — xunit, references both projects
- [ ] `.gitignore` (Visual Studio template), `README.md`
- [ ] `tools/set-shell.ps1`, `tools/restore-explorer.ps1` (Windows PowerShell 5.1, HKCU only)

## 1. Skeleton

- [ ] Single instance (named mutex) and `/exit` via a registered window message
- [ ] Run-mode detection (`GetShellWindow`)
- [ ] File logger
- [ ] Settings record + JSON load/save, corrupt-file fallback (tests)
- [ ] Crash handlers: log, start `explorer.exe` in shell mode
- [ ] Interop basics: `MessageWindow`, `WindowSubclass`, window-style helpers

## 2. Wallpaper

- [ ] `WallpaperWindow` per monitor at `HWND_BOTTOM`
- [ ] Read wallpaper, style and background colour; style mapping (tests)
- [ ] Reload on wallpaper/display changes
- [ ] Only in shell mode

## 3. Taskbar window

- [ ] `AppBar` registration and rect calculation (tests); release on exit
- [ ] One taskbar per monitor; recreate on display/DPI changes
- [ ] Acrylic backdrop kept active; light/dark theme following the system
- [ ] Layout: Start, Search, task area, tray area, indicators, clock, show-desktop
- [ ] Clock (time + date) and calendar flyout
- [ ] Show desktop
- [ ] Taskbar context menu (Task Manager, settings toggles, Exit)

## 4. Tasks

- [ ] `ShellHook` + `SetWinEventHook` + `EnumWindows`
- [ ] `WindowInfo` snapshot and the "gets a button" filter (tests)
- [ ] AUMID / exe grouping (tests), window icons
- [ ] Task buttons: indicators, click/middle-click/Shift+click, flashing
- [ ] Context menu: launch, pin/unpin, close window(s)
- [ ] Pinned apps (settings), drag to reorder
- [ ] Combine modes: always / when full / never
- [ ] DWM thumbnail popup with close buttons
- [ ] Centre / left alignment

## 5. Start menu

- [ ] Popup window, anchoring, hide on deactivate/Esc
- [ ] App catalog from `shell:AppsFolder` with icons
- [ ] Pinned grid + All apps list; pin to Start / taskbar
- [ ] App search ranking (tests)
- [ ] Indexer search via `ISearchQueryHelper` + `System.Data.OleDb` (query building tests), debounce + cancellation
- [ ] Keyboard navigation (type-to-search, arrows, Enter)
- [ ] Settings button
- [ ] Power menu: Lock, Sign out, Sleep, Restart, Shut down
- [ ] Switch to Explorer (confirm, clear HKCU Shell value, start Explorer, exit)
- [ ] Start/Search buttons on the taskbar open it

## 6. System tray

- [ ] `TrayHost` with `Shell_TrayWnd` / `TrayNotifyWnd`, `TaskbarCreated` broadcast
- [ ] `NOTIFYICONDATA` parsing for 32/64-bit callers (tests)
- [ ] Add/modify/delete/set version, `NIS_HIDDEN`, dead-owner cleanup
- [ ] `Shell_NotifyIconGetRect` replies
- [ ] Mouse forwarding (v4 and legacy semantics), `AllowSetForegroundWindow`
- [ ] `TrayMode`: show all / overflow flyout

## 7. Indicators

- [ ] Network: `NetworkInformation`, glyphs, tooltip, opens `ms-settings:network`
- [ ] Volume: endpoint volume + callbacks, default-device changes, wheel, flyout (slider, mute)
- [ ] Microphone: capture session monitoring, tooltip with apps, hidden when idle

## 8. Shell mode

- [ ] `SetShellWindow` / `SetTaskmanWindow`, shell-ready event
- [ ] Startup apps: RunOnce, Run (incl. WOW6432Node), Startup folders, `StartupApproved` (tests), once per session,
      no double launch after switching to Explorer
- [ ] Session end handling
- [ ] Win key (low-level hook), Ctrl+Esc, Win+D, Win+T, Win+S
- [ ] End-to-end test on a VM/test account: sign in with NeoShell as shell, verify tray icons, startup apps,
      power options, Switch to Explorer, crash fallback

## 9. Parity polish

- [ ] Win+1…9
- [ ] Full-screen app handling (`ABN_FULLSCREENAPP`)
- [ ] Auto-hide
- [ ] Show on all displays setting
- [ ] Progress bars and overlay badges (`ITaskbarList3` messages)

## Later / not planned yet

- Jump lists
- Desktop icons
