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
- [x] Home page like Windows 11: Pinned, Recent; All apps behind its button
- [x] Explorer's Start pins imported once (`Export-StartLayout`'s COM object; tests for parsing and matching)
- [x] Recent apps from UserAssist; NeoShell's launches logged there (`SEE_MASK_FLAG_LOG_USAGE`) - packaged apps
      started by NeoShell aren't recorded (the activation manager doesn't log)
- [x] App search ranking (tests)
- [x] Indexer search via `ISearchQueryHelper` + `System.Data.OleDb` (query building tests), debounce + cancellation
- [x] Keyboard navigation (type-to-search, arrows, Enter) — not driven live: that needs global keystrokes
- [x] Settings button
- [x] Power menu: Lock, Sign out, Sleep, Restart, Shut down — not invoked live: each ends the test session
- [x] Switch to Explorer (confirm, clear HKCU Shell value, start Explorer, exit)
- [x] Start/Search buttons on the taskbar open it

## 6. System tray

- [x] `TrayHost` with `Shell_TrayWnd` / `TrayNotifyWnd`, `TaskbarCreated` broadcast
- [x] `NOTIFYICONDATA` parsing for 32/64-bit callers (tests)
- [x] Add/modify/delete/set version, `NIS_HIDDEN`, dead-owner cleanup
- [x] `Shell_NotifyIconGetRect` replies
- [x] Mouse forwarding (v4 and legacy semantics), `AllowSetForegroundWindow`
- [x] `TrayMode`: show all / overflow flyout

## 7. Indicators

- [x] Network: `NetworkInformation`, glyphs, tooltip, opens `ms-settings:network`
- [x] Volume: endpoint volume + callbacks, default-device changes, wheel, flyout (slider, mute)
- [x] Microphone: capture session monitoring, tooltip with apps, hidden when idle

## 8. Shell mode

- [x] `SetShellWindow` / `SetTaskmanWindow`, shell-ready event
- [x] Startup apps: RunOnce, Run (incl. WOW6432Node), Startup folders, `StartupApproved` (tests), once per session,
      no double launch after switching to Explorer
- [x] Session end handling
- [x] Win key (low-level hook), Ctrl+Esc, Win+D, Win+T, Win+S
- [x] Crash fallback: a watchdog process starts Explorer when NeoShell ends abnormally (crash, fail-fast, kill)
- [x] End-to-end test on a VM/test account: sign in with NeoShell as shell, verify tray icons, startup apps,
      power options, Switch to Explorer, crash fallback (done by the owner; defects noted for later)

## 9. Parity polish

- [x] Win+1…9
- [x] Full-screen app handling: a window covering its monitor, or marked with `MarkFullscreenWindow`
- [x] Auto-hide
- [x] Show on all displays setting (since milestone 3; only one monitor on the VM to test with)
- [x] Progress bars and overlay badges (`ITaskbarList3` messages)

## 10. Desktop icons

- [x] Desktop items: user + public Desktop merged, system icons per "Desktop icon settings", hidden-file options (tests)
- [x] Sort by name / size / type / date, system icons and folders first (tests); icon size and "Show desktop icons"
      in Explorer's own registry values
- [x] Thumbnails and shortcut overlays; refresh on file, Recycle Bin and setting changes
- [x] Selection, open, keyboard (Enter, Delete, F2, F5, Ctrl+C/X/V, Alt+Enter), inline rename — keys not driven
      live (global keystrokes); the same actions were tested through the menus
- [x] Icon menu and desktop menu (View, Sort by, Refresh, Paste, New, Desktop icon settings), "Show more options"
      with the shell's full menu — Empty Recycle Bin not invoked live (it deletes for good)

## 11. Requested features

- [x] Desktop: drag a selection rectangle over the icons (Ctrl adds to the selection)
- [x] Taskbar and Start: the accent colour when "Show accent color on Start and taskbar" is on
- [x] Taskbar backdrop setting: Acrylic, Mica or Translucent (taskbar menu)
- [x] Start: resize by dragging a top corner; the size is saved

## Later / not planned yet

- Jump lists
- Desktop icons: moving icons to free positions, dragging files onto the desktop and its icons
