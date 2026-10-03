# NeoShell plan

Milestones in order. Each one builds cleanly, passes `dotnet test`, and is committed on its own.
Tick items off as they land. The feature details are in [design.md](design.md).

## 0. Scaffold

- [x] `NeoShell.slnx`, `Directory.Build.props` (TFM, nullable, implicit usings, warnings as errors, `LangVersion latest`)
- [x] `src/NeoShell` — WinUI 3, unpackaged, self-contained Windows App SDK, custom `Main`
- [x] `src/NeoShell.Interop` — class library, `AllowUnsafeBlocks`, `InternalsVisibleTo` tests
- [x] `tests/NeoShell.Tests` — xunit, references both projects
- [x] `.gitignore` (Visual Studio template), `README.md`
- [x] `tools/set-shell.ps1`, `tools/restore-explorer.ps1` (Windows PowerShell 5.1, HKCU only)

## 1. Skeleton

- [x] Single instance (named mutex) and `/exit` via a registered window message
- [x] Run-mode detection (`GetShellWindow`)
- [x] File logger
- [x] Settings record + JSON load/save, corrupt-file fallback (tests)
- [x] Crash handlers: log, start `explorer.exe` in shell mode
- [x] Interop basics: `MessageWindow`, `WindowSubclass`, window-style helpers

## 2. Wallpaper

- [x] `WallpaperWindow` per monitor at `HWND_BOTTOM`
- [x] Read wallpaper, style and background colour; style mapping (tests)
- [x] Reload on wallpaper/display changes
- [x] Only in shell mode

## 3. Taskbar window

- [x] `AppBar` registration and rect calculation (tests); release on exit
- [x] One taskbar per monitor; recreate on display/DPI changes
- [x] Acrylic backdrop kept active; light/dark theme following the system
- [x] Layout: Start, Search, task area, tray area, indicators, clock, show-desktop
- [x] Clock (time + date) and calendar flyout
- [x] Show desktop
- [x] Taskbar context menu (Task Manager, settings toggles, Exit) — toggles for combine, auto-hide and tray mode
      are added with those features

## 4. Tasks

- [x] `ShellHook` + `SetWinEventHook` + `EnumWindows`
- [x] `WindowInfo` snapshot and the "gets a button" filter (tests)
- [x] AUMID / exe grouping (tests), window icons
- [x] Task buttons: indicators, click/middle-click/Shift+click, flashing
- [x] Context menu: launch, pin/unpin, close window(s)
- [x] Pinned apps (settings), drag to reorder (pinned apps keep their order; running apps fall back in line)
- [x] Combine modes: always / when full / never
- [x] DWM thumbnail popup with close buttons
- [x] Centre / left alignment

## 5. Start menu

- [x] Popup window, anchoring, hide on deactivate/Esc
- [x] App catalog from `shell:AppsFolder` with icons
- [x] Pinned grid + All apps list; pin to Start / taskbar
- [x] App search ranking (tests)
- [x] Indexer search via `ISearchQueryHelper` + `System.Data.OleDb` (query building tests), debounce + cancellation
- [x] Keyboard navigation (type-to-search, arrows, Enter) — not driven live: that needs global keystrokes
- [x] Settings button
- [x] Power menu: Lock, Sign out, Sleep, Restart, Shut down — not invoked live: each ends the test session
- [x] Switch to Explorer (confirm, clear HKCU Shell value, start Explorer, exit)
- [x] Start/Search buttons on the taskbar open it

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
