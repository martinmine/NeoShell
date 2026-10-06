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
- [x] Explorer's taskbar pins imported once (`Taskband\Favorites`; tests for parsing)
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
- [x] Taskbar backdrop setting: Acrylic, Mica, Translucent or Transparent (taskbar menu)
- [x] Start: resize by dragging a top corner; the size is saved
- [x] Desktop menus: Explorer's full menu in one (no "Show more options"), the shell's New menu, Display settings and
      Personalize opening classic dialogs in shell mode; no Undo (Explorer's own history)
- [x] `tools/start-shell.ps1`: replace the running Explorer shell with NeoShell for this session

## 12. Requested features (2)

- [x] Moving icons doesn't make them vanish and reappear: the taskbar animates its own changes (no item transitions),
      running apps keep their dragged place for the session, Start's pinned grid is reordered by hand and Pinned and
      Recent are updated in place
- [x] Animations: Start flies out of the taskbar and back; icons shrink while pressed (taskbar, Start and Search
      buttons, Start's pinned and recent apps) and grow while dragged
- [x] Jump lists in the task button's menu: the app's categories, Recent/Frequent and Tasks, with icons (file format,
      AppID hash and implicit AppIDs tested); no pinned entries
- [x] Volume flyout: choose the output device (`IPolicyConfig`), volume mixer per app; the speaker's menu (Open
      volume mixer, Sound settings) — switching outputs only checked with the VM's single device
- [x] Flyouts and menus above the taskbar with Windows 11's gap, sliding up from behind it; jump lists centred on
      their button, volume and calendar at the screen's right edge
- [x] Thumbnails slide out of the taskbar, along it and back
- [x] Peek: hovering a thumbnail shows only its window, moving to the next one moves the peek
- [x] Volume flyout as Quick Settings: slider row, Sound output page (output device, spatial sound, mixer, More volume
      settings, Win+Ctrl+V), the default beep on letting go of the slider — Win+Ctrl+V not pressed live

## 13. Quick Settings

- [x] Network, volume and battery as one taskbar button with a hover plate; each icon keeps its tooltip and menu
      (network: Network and Internet settings); energy saver's leaf; airplane mode's plane on the network icon
- [x] Quick Settings laid out as Windows 11's: pages of 2×3 tiles (wheel and arrows, page dots), the volume slider
      and Sound output page moved in, battery and All settings in the footer
- [x] Wi-Fi tile and page: radio switch, networks in range, connect (saved profile or key) and disconnect — no Wi-Fi
      on the VM, only the hidden tile checked live
- [x] Bluetooth tile and page: radio switch, paired devices and their state (no public API to connect them) — no
      Bluetooth on the VM, only the hidden tile checked live
- [x] Airplane mode (Radio Management API), Energy saver (WNF), Live captions
- [x] Accessibility page: Magnifier, Narrator, Live captions, Voice access, Sticky keys switched; Colour filters and
      Mono audio link to Settings (no way to switch them from outside) — Narrator and Voice access not run live
- [x] Cast page (no Miracast without Wi-Fi), Project page (`SetDisplayConfig`) — switching screens not done live
      (one monitor on the VM)
- [x] Night light and Nearby sharing open Settings (their state is in private stores); hidden in shell mode
- [x] Win+A, Win+K, Win+P, Win+Ctrl+V in shell mode through the keyboard hook (ShellHost holds the hotkeys)
- [x] Fix: closing an open Start at exit crashed in its hide animation (the watchdog started Explorer)

## 14. Notifications and calendar

- [x] Calendar as Explorer's: its own acrylic panel below the notification center (two windows 12 epx apart,
      matched to the pixel against Explorer's), sliding in from the right edge; long date heading without the year
      and a fold button (remembered); `CalendarView` without borders or backgrounds, other months' days dimmed,
      weeks from the regional first day; focus footer
- [x] Notification center: notifications from `UserNotificationListener` (polled: no change event unpackaged),
      grouped by app, "+N notifications" / See fewer, the chevron for trimmed text, "â€¦" and Clear, Clear all, "No new
      notifications"; clicking one opens its app (the toast's own activation isn't available) and removes it
- [x] Do not disturb (quiet hours profile, undocumented `IQuietHoursSettings`): the button, the bell beside the
      clock, no toasts while on
- [x] Focus sessions: Windows' own API is limited to Microsoft's apps, so NeoShell's own â€” Do not disturb for the
      chosen time with a countdown; no badge hiding or end chime
- [x] Toasts in shell mode (Windows shows none without Explorer): Explorer's look and place, stacking (three at
      most), the system's display time, held under the pointer; per-app and global banner settings honoured
- [x] Win+N in shell mode through the keyboard hook; a click elsewhere on the taskbar closes the flyout
- [x] Clock tooltip as Explorer's (date, then day and time)

## 15. Explorer parity

- [x] Start opens from the screen's corner and the taskbar's edges beside the button
- [x] Start's Quick Link menu: right-click Start or its corner, Win+X as the shell; Run above the Start button
- [x] Alt+Tab as the shell: Explorer's card layout, live previews, most recently used order, Tab/Shift/arrows/Enter/
      Esc/Delete, click to switch, close button
- [x] Fix: right-clicking a task button could open its previews over the menu
- [x] Fix: Start and the thumbnails rose from the screen's bottom edge; now cut off at the taskbar's edge, as
      Explorer's (and the first opening of Start slid up black)
- [x] Fix: jump lists and the tray flyout flashed at their place, or jumped, before sliding
- [x] Fix: the taskbar's menus turned solid grey while inactive; acrylic as Explorer's

## 16. Explorer's shortcuts

- [x] Survey: which Win+ shortcuts Explorer holds and which survive it (probed with `RegisterHotKey`)
- [x] Win+E, Win+R, Win+I, Win+Pause, Win+Q; Win+E without starting a second Explorer shell
- [x] Win+M, Win+Shift+M, Win+Home
- [x] Win+B, Win+Shift+T
- [x] Win+Shift/Ctrl/Alt+1…9 (new instance, last active window, jump list), Win+Ctrl+Shift+1…9 (as administrator)
      — elevation not run live (it asks through UAC's secure desktop)
- [x] Win+Alt+D, Win+Alt+K (microphone mute, shown on the microphone indicator)
- [x] Win+Comma (peek at the desktop)
- [x] Win+PrtScn and Win+Shift+S (NeoShell's own rectangle snip: Snipping Tool needs Explorer)
- [x] Win+Z: Snap layouts
- [x] Alt+F4 on the desktop or the taskbar: Shut Down Windows dialog (it closed the window before)
- [x] Fix: Win+Alt+K (and Win+Shift with A/K/P/N/X) opened Quick Settings' pages
- Not done: Win+Shift+R (Snipping Tool video), Win+V, Win+Period, Win+H (no public way to Windows' text input
  host), Task View and virtual desktops (out of scope)

## 17. Widgets

- [x] Sidebar window on the right of the primary monitor, to the taskbar: app bar alongside Explorer, work area as the
      shell (`ShellWorkArea` shared with the taskbar); resizable by its left edge; "Show widgets" in the taskbar menu
- [x] Taskbar's backdrop, theme and accent colour; just above the desktop (`PinnedLayer.Desktop`)
- [x] Widget frame: settings and close buttons on hover; add menu (one of most kinds, several notes and pictures)
- [x] Drag out to float on the desktop, back in to dock, and within the sidebar to reorder; positions saved
- [x] Profile, Resource usage (graphs, colours), Pictures, Now playing, Weather (MET Norway), Notes
- [x] Tests: work area, placement and order, formats, forecast parsing
- [x] Smaller widgets (profile, weather, resources, now playing); pictures fill their card
- [x] Resources: GPU, free disk space, each drive and each network adapter as an option
- [x] Sidebar menu: hide the panel behind the widgets (window cut to its cards), add widget
- [x] Wireless devices widget: the WirelessStatus app's devices (Bluetooth, Razer, Audeze Maxwell), its protocol
      tests brought over; read again on device changes — rows with devices checked with made-up ones (none on the VM)
- [x] Fix: exit crashed now and then as the shell after a widget was added (WinUI handling a move of a closing
      window); exit crashed after the taskbar's backdrop submenu was used (a backdrop shared by a menu and its
      submenu leaked a controller). Both found from crash dumps.
- [x] Dragging over the sidebar makes room (a gap, the cards slide); a floating widget slides into it when let go
- [x] Floating notes resize by their corner; the note's text has no box or underline
- [x] Smoother dragging (no z-order search per move); no "Widgets" heading, the add button shows on hover
- [x] Fix: a floating widget didn't follow its height (expanded graphs cut off, Now playing too tall)
- [x] Fix: a clicked widget (or the whole sidebar) could sink below the wallpaper when a window was minimized, as if
      closed; resource sampling moved off the UI thread
- [x] Fix: the shell's work area is set off the UI thread (its broadcast waits for every window; startup hung once)
- Not run live: Now playing with media playing (nothing plays on the VM), the weather from the computer's location
  (location is off on the VM), a second monitor

## 18. Requested features (3)

- [x] Desktop drag and drop with other apps, as Explorer's desktop: icons drag out to any app (WinUI's drag with the
      files as storage items), and files dragged in from other apps, or the desktop's own icons, drop on the desktop
      or on an icon that takes drops (a folder, the Recycle Bin, an app) through the shell's own drop targets
- [x] Desktop icons move freely on a grid, as on Explorer's desktop: dragged anywhere, places remembered for every
      icon, dropped files and New items where that happened, Auto arrange icons, Sort by packs again, arrow keys
      across the grid; tests for the grid
- [x] Window snapping as the shell (Windows leaves it to Explorer): drag against the work area's edges to a half, a
      quarter or maximized, with a preview of the zone; a snapped window dragged away gets its size back; Win+arrows
      through the keyboard hook (tests for zones, keys and unsnapping)
- [x] Open previews follow the pointer along the taskbar at once; the 200 ms pause is only for a pointer heading up
      to them across a neighbouring button (tested)

## Later / not planned yet

- Notifications: toast images, buttons and inline replies (not exposed to listeners); tray balloons as toasts

- Jump lists: pinned entries, Pin to / Remove from this list
