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
- [x] Recent apps from UserAssist; NeoShell's launches logged there (`SEE_MASK_FLAG_LOG_USAGE`; packaged apps as
      Explorer's Start records them, milestone 19 T22)
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
      grouped by app, "+N notifications" / See fewer, the chevron for trimmed text, "…" and Clear, Clear all, "No new
      notifications"; clicking one opens its app (the toast's own activation isn't available) and removes it
- [x] Do not disturb (quiet hours profile, undocumented `IQuietHoursSettings`): the button, the bell beside the
      clock, no toasts while on
- [x] Focus sessions: Windows' own API is limited to Microsoft's apps, so NeoShell's own — Do not disturb for the
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
- [x] About Windows widget: the flag on the left; edition, version, build, architecture and install date on the right
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
- [x] Thumbnail toolbars as the shell (`ITaskbarList3::ThumbBar*`): the buttons under a window's preview, with their
      images, tooltips and states, following the app's updates; clicks go to the app (tested with VLC; tests for
      the shared data and the image list format)
- [x] Fix: right-clicking the desktop or an icon left the wait cursor (spinner) over the menu until the pointer moved

- [x] Fix: a widget clicked soon after the shell started could sink under the wallpaper, as if closed: it went below
      a console's sizeless window lying under the wallpaper. Such windows, and all below the wallpaper, are skipped
- [x] Fix: a widget's settings and close buttons took clicks while hidden
- [x] Fix: windows couldn't be dragged over the widget sidebar (Windows keeps the pointer in the work area during a
      move); the clip is widened over it, and windows snap right at the screen's edge
- [x] Fix: a minimized window of an app running as administrator (an elevated Terminal) couldn't be brought back from
      the taskbar, nor minimized: Windows refuses NeoShell's ShowWindow; the system menu's commands get through
- [x] Widgets moved between the sidebar and the desktop stay as they were instead of showing empty and filling in: the
      new view is covered with a picture of the old one until it's ready; dropped on the sidebar, a widget takes its
      place at once (no slide), and the pictures widget keeps its picture

## 19. Explorer parity (2)

Each item is done when it looks and behaves as Explorer's (layout, behaviour, animation), compared side by side.

- [x] T1 App bar messages from other apps (`SHAppBarMessage` on `Shell_TrayWnd`, `WM_COPYDATA` dwData 0) — not
      compared live: `ABM_ACTIVATE` lifting an auto-hide bar; Explorer sends one more `ABN_POSCHANGED` per work area
- [x] T2 Tray balloon notifications (`NIF_INFO`) shown as toasts — not run live: an app with a process-wide explicit
      AppUserModelID (Explorer's private resolver finds it; NeoShell names it after its executable)
- [x] T3 Maximized windows follow work area changes (taskbar, sidebar) — not run live: a second monitor
- [x] T4 Shell service objects (`ShellServiceObjects`, SSODL): Safely Remove Hardware and other built-in tray items —
      not run live: ejecting a device (each ejectable device on the VM is a controller, disk or the NIC in use)
- [x] T5 AutoPlay on inserted media as the shell, NeoShell's own (Windows' runs only in Explorer): content, saved
      choices, banner toast and Windows 8 flyout as Explorer's, choices remembered and run (tests for content, events,
      choice lists and saving) — not run live: USB sticks and memory cards, audio CDs, Blu-ray, VCD and blank discs
      (no hardware; blank media isn't detected), CLSID handlers; not done: WPD devices (phones, MTP cameras) and
      the "new choices" prompt
- T6 `Progman` / `WorkerW` desktop windows (0x052C), for wallpaper tools — skipped: not needed for now
- [x] T7 Toast sound and toast activation: the toast's own sound (scheme sounds, files, scenarios, loop, settings),
      started with the slide-in; a click runs the toast's activation through the notification platform's controller
      (COM activator, protocol, packaged), from a toast or the notification center — not run live: background
      activation and a packaged app's own toast audio file; not matched: ghost toasts pop up, `duration="long"` and
      alarms with buttons time out as other toasts
- [x] T8 Input language indicator and its picker: Explorer's input switcher (InputSwitch.dll) for the input method
      in front and switching; NeoShell's own switcher flyout and Win+Space (shell mode), the IME mode button —
      not done: the IME's right-click menu (InputSwitch draws it only in Explorer); not run live: text services
      other than the Japanese IME (their glyphs paired by meaning), "a different input method for each app
      window", other DPIs
- [x] T9 Badges for packaged apps (`BadgeUpdateManager`): counts and glyphs read from Windows' badge store as
      Explorer reads them, drawn and animated as Explorer's, in both run modes; "Show badges on taskbar apps" followed
      live; the overlay icon moved to the badge's corner — not run live: NeoShell in the light theme (it crashes on
      start in light mode, an existing backdrop bug); not matched: "99+" sits 1 px left and the playing glyph 1 px
      right of Explorer's (text rendering)
- [x] T10 End task in the task button's menu: as Explorer offers it with the developer setting on (followed live),
      in both run modes; desktop apps end through `EndTask` (hung and elevated ones too), UWP apps by their package;
      the separator above Close window, which Explorer doesn't draw, removed — not run live: UWP apps in shell mode
      (they can't show there); not matched: the menu's width and the launch item's icon (existing differences)
- [x] T11 Dragging over the taskbar, in both run modes: hovering a button brings its window forward (or its previews,
      and hovering one of those its window), as Explorer; one program or shortcut to one is pinned where it's dropped,
      the buttons making way; anything else is refused (25H2 opens nothing dropped on a button) — not run live: a
      second monitor, a centred Explorer taskbar to compare the gap with, drags of packaged apps from Start (not done)
- [x] T12 Tray icons promoted one by one (`NotifyIconSettings`), dragged between overflow and taskbar: Explorer's keys,
      order and defaults kept and followed live, the hidden icon menu (taskbar menu), drags with Explorer's picture,
      captions, marker and drop rules; `TrayMode` folded in — not done: moving icons with the keyboard, `Publisher` and
      `IconSnapshot` values; not run live: a vertical or top taskbar (other chevron glyphs), other DPIs
- [x] T13 Privacy indicator as 25H2's: one button for the microphone and the location (one glyph for both), from
      Windows' capability access manager as Explorer (the microphone moved off Core Audio sessions), Explorer's
      tooltips, clicks and menu, in both run modes — not run live: a packaged app using the location, the voice
      assistant; no camera indicator exists in Explorer on 25H2 (and the VM has no camera)
- [x] T14 Clock: seconds (`ShowSecondsInSystemClock`); notification count/badge — Explorer's clock settings followed
      live (seconds, hide the time and date, the notification bell, additional clocks in the tooltip), its time
      format, tabular digits and layout, the bell's four looks from the platform's new-notification count (shared
      with Explorer), the clock's menu, in both run modes — not done: "Show abbreviated time and date" (feature-
      flagged off on this build), "Show time in Notification Centre", the `DisableNotificationCenter` policy
- [x] T15 Taskbar search box style: Explorer's four search looks (hidden, icon, box, icon and label) from its
      `SearchboxTaskbarMode`, followed live and set from the taskbar menu's Search submenu, drawn and coloured as
      Explorer's in dark, light and accent, with its hover and press states; the box and the label collapse to the
      icon when the taskbar is full; `ShowSearchButton` carried over, in both run modes — not done: search highlights
      (Bing content, no public API); not matched: the box's press is animated (133 ms) where Explorer's is instant,
      the label's icon shrinks to 0.8 (Explorer ~0.86), Explorer's labelled buttons narrow before the search collapses
      (NeoShell's don't narrow)
- T16 Start: Recommended with recent files and newly installed apps — skipped: not wanted for now
- [x] T17 Start: folders beside the power button; folders of pins in the pinned grid — the folders chosen in
      Settings (`VisiblePlaces`, and the `AllowPinnedFolder…` policies) in Explorer's order, glyphs, spacing and
      tooltips, opening what Explorer opens, "Personalise this list"; folders of pins made by holding an app on
      another, Explorer's preview, tile, panel (grows out of the tile and back), rename, drag in, out and within,
      one-app folders kept and emptied ones removed, the folder commands in the pins' menus (and Move to front/
      left/right), in both run modes — not done: carrying Explorer's folders over (its export lists their apps
      without the folder; `start2.bin` has them but is encrypted with a private key derivation), Explorer's
      fade-out/fade-in when a folder is made (NeoShell swaps at once), icons on the menu items (T18); not run live:
      the light theme and no-accent colours of the folder panel, the policies, typing a name (set through UI
      Automation instead)
- [x] T18 Start: app menu (Run as administrator, Open file location, Uninstall, App settings, jump list) — Explorer's
      items, order, groups and glyphs (from its `ContextMenuSorter`) in pins, folders, folder tiles, All apps and
      Recent, the search box's order in search results; Run as administrator, Open file location, Uninstall and File
      Explorer's verbs from the app's `shell:AppsFolder` menu, as Explorer's; a packaged app's Uninstall asks in
      Explorer's dialog and removes the package, a desktop app's opens Installed apps; App settings opens the app's
      page; the jump list below, trimmed at 290 (Edge's Recent and Settings' empty list now as Explorer's, taskbar
      too); folder items' icons; in both run modes (as the shell: no App settings, Programs and Features for a
      desktop app's Uninstall, no Run as administrator for packaged apps) — not done: Rate and review / Share
      (search, Store apps), pinned jump list entries, a URL's short name in Recent; not run live: the light theme,
      keyboard opening (Shift+F10, menu key), elevation itself (the UAC prompt was shown, then cancelled)
- [x] T19 Power menu: Hibernate, Switch user, Update and restart / shut down — shutdownux's choices (read with its
      PDB) in Start's power button, the Quick Link menu and the Shut Down Windows dialog, each in Explorer's order and
      read as it opens: Lock in Start (Sign out moves to the user picture, T20), Switch user in the dialog, Hibernate
      and Sleep by Power Options, capabilities and policies, Update and shut down / Update and restart (with
      estimates) from Windows Update's `ShutdownFlyoutOptions`, Start's glyphs, orange dots and tooltips; shutdownux's
      flags (Fast Startup, Shift for boot options, `SHUTDOWN_INSTALL_UPDATES`, ARSO), Switch user as
      `WTSDisconnectSession`, in both run modes — not run live: Hibernate (the VM's firmware has no S4), the power
      button's update dot (`WNF_USO_REBOOT_REQUIRED` can't be faked), real updates, and the calls themselves
      (stopped at a cdb breakpoint); not done: Start's confirmation when other users are signed in, USO's
      `EnhancedShutdownEnabled` path
- [x] T20 Start: account menu on the user picture — Explorer's 25H2 account card (its React Native account control,
      decompiled, and windows.internal.shell.broker's user tile commands, read with its PDB): Microsoft's logo, Sign
      out, "…" with the other accounts (signed-in first, "Signed in") and, on joined PCs, Switch user; the picture,
      name, "Local account" or the Microsoft account's email, Manage my account (Settings → Accounts; User Accounts
      as the shell); switching to an account as Explorer does (`UserSwitch` key, then its session's lock screen or the
      sign-in screen); the default silhouette for accounts without a picture; layout, hover, placement, Esc and
      animation compared side by side, in both run modes — not done: a Microsoft account's subscription and storage
      cards, a work account's tenant name; not run live: a Microsoft or work account, switching to a signed-in
      account, the joined-PC Switch user, and the calls themselves (stopped at a cdb breakpoint)
- T21 Start search: Settings pages, web, filter tabs, best match preview pane with actions — skipped: search is
      good as it is
- [x] T22 Packaged apps started by NeoShell recorded in Recent — found with Procmon and cdb how Explorer's Start
      (through the shell broker and twinui's view manager) and taskbar (ShellExecuteEx on `shell:AppsFolder`) record
      a start, and NeoShell now makes the same shell32 calls: the app's UserAssist value and, for a packaged desktop
      app, its executable's; values compared before and after the same launch from Explorer's Start and NeoShell's,
      in both run modes, and the Recent row checked — differs: alongside Explorer the session totals in
      `UEME_CTLSESSION` stay Explorer's (shell32 keeps them per process; as the shell they count NeoShell's
      starts); Explorer's own Start doesn't reorder its category folders or drop "New" for apps NeoShell starts (it
      keeps its own launch counts)
- [x] T23 Start: All apps category view — Explorer 25H2's Home with Recommended off: Pinned, then All on the same
      page (NeoShell's Recent row, All apps button and separate All apps page removed) with Category, Grid and List
      views in Explorer's `AllAppsViewMode`; Explorer's categories (its saved web-service answers, else Windows'
      LZMS mappings file), merging and order (StartMenu.dll's `CategoryProvider`, read with its PDB), cards and the
      category panel; Most used, "New" (from Start's CloudStore tile store, Bond) and "System"; the letters view;
      Settings' Pinned, All and Most used followed live; compared side by side in both run modes (layout to 1 px) —
      not done: Explorer's folders in All (Start Menu subfolders; NeoShell's catalog is flat, so its categories have
      more apps), arrow-key navigation, the page's zoom-out animation; differs: use is measured from UserAssist (starts
      plus minutes in front) where Explorer keeps its own count of the starts it saw
- [x] T24 Wallpaper: slideshow, Windows Spotlight, a wallpaper per monitor, `IDesktopWallpaper` in shell mode —
      found with Procmon, Ghidra and cdb (shell32's `CDesktopWallpaper`/`CSlideshowWorker`) that Explorer's desktop
      serves `CLSID_DesktopWallpaper` and runs the slideshow; as the shell NeoShell now serves it (every call answered
      as Explorer's, compared with a test client against both; "Set as desktop background" works again) and runs the
      slideshow (Explorer's order, shuffle, midnight-aligned timing, power-plan pause, 1 s linear crossfade measured at
      60 fps against Explorer's), with a picture per monitor, all in Explorer's own registry values, `slideshow.ini`
      and transcoded files, so each shell continues the other's (checked both ways) — not done: Windows spotlight
      beyond showing its current picture (the "Learn about this picture" icon, flyout, Next and the rotation live in
      Explorer and private UDK/COM interfaces); the slideshow's pause while the display is off; not run live: a second
      monitor (per-monitor values and the monitor mapping unit tested), a battery
- [x] T25 Desktop icons on every monitor — tested with a second monitor from VMware Tools (`VMwareResolutionSet`);
      one view per monitor sharing one selection, drags between monitors, the selection rectangle and arrow keys
      across them, Sort by and Auto arrange per monitor, new icons on the primary first; places kept in Explorer's
      own `IconLayouts` (format and desktop matching read from shell32 with Ghidra), so icons stay put when switching
      shells (checked both ways, with and without the widget sidebar) and come back when a monitor is unplugged and
      plugged back; Explorer's grid spacing (stretched to the work areas) and per-monitor DPI (125%) — compared with
      `LVM_GETITEMPOSITION`/`LVM_GETITEMSPACING` on Explorer's desktop, cells to the pixel — differs: the matching of
      a new monitor arrangement is a simplified `DesktopMatcher` (no linked desktops of its own, rating by fit); the
      spacing's label height is a constant (44 at 96 DPI; Explorer measures the icon font and text scaling), and
      with mixed DPI Explorer's spacing came out 76x98, NeoShell's 76x101 — not run live: Auto arrange toggled with
      icons on two monitors in NeoShell (unit tested; Explorer's checked), Ctrl+A (no letter keys)
- [x] T26 Desktop: Undo; "Align icons to grid" off — found with cdb and Ghidra in shell32 that the session's undo
      history is the desktop undo manager, a local server the shell's process serves (Explorer on request, else
      rundll32); as the shell NeoShell now serves it on a thread of its own, so every app's file operations land there
      and the desktop's menu shows the units' own "Undo Delete"/"Redo Rename" (Ctrl+Z, Ctrl+Y), only when there's
      something, after Paste, as Explorer's; checked live for delete (NeoShell's menu, a drop on the Recycle Bin, another
      app's IFileOperation), rename, copy/paste and New folder, undo and redo, against Explorer's menu. Align icons to
      grid off (Explorer's `FWF_SNAPTOGRID` in `FFlags`): icons stay exactly where dropped, overlapping, kept in the work
      area, new icons in cells no icon overlaps, snapped to the nearest cells when turned on again, saved as fractions
      in IconLayouts, each measured on Explorer's desktop through `IFolderView` and compared; drops on an icon whose
      target refuses fall back to the desktop as in Explorer — not run live: Ctrl+Z/Ctrl+Y (letter keys), Undo Move
      (same path as the others); differs: an icon placed by the grid and then saved off it is 2 px higher in Explorer's
      saved rows (its unsubtracted top inset)
- [x] T27 Snap Assist, snap groups, Snap layouts on the maximize button and at the top edge — studied in Explorer
      with WinForms test windows (standard and a custom title bar answering `HTMAXBUTTON`), 60 fps recordings, UIA
      and pixel scans; the flyout (hover, ~630 ms, closing 230 ms after leaving; Win+Z with numbers) matches Explorer's
      to the pixel in size, layouts and zone geometry (60/40 uneven pair below 1920 epx) and colours (dark), with 24H2's
      suggestions; the Snap bar (peek, reach, slide timings, place and size measured at 60 fps); Snap Assist (layout
      choice, panels, card rows and sizes, order, keys, closing; minimized windows' previews); snap groups on the
      taskbar previews and in Alt+Tab (placement, title, first selection, restoring); the five Multitasking settings
      and "Snap windows" (registry values found by toggling them) honoured live, each checked — differs: Assist's
      cards fade in (Explorer's fly in from their windows, appearing later), the dragged window isn't shrunk over the
      bar, with `DITest` off Windows' own top-edge maximize still answers within 6 px, elevated apps get no hover
      flyout (UIPI), suggestions follow NeoShell's z-order rather than Explorer's activation order, the Maximize
      tooltip shows briefly before NeoShell hides it — not run live: a second monitor, light theme colours (dark
      measured), screens of 1920+ epx (thirds layouts)
- [x] T28 Win+Shift+arrows and Win+Left/Right across monitors — studied on Explorer with a second monitor (VMware
      Tools; 1280x800 beside 1764x988, at 100% and 125%) and with none: every key from every state (normal, halves,
      quarters, maximized, stretched), extended frame bounds and Snap Assist recorded; win32k's own share found with
      cdb and Ghidra (Win+Shift+Left/Right move ordinary windows and Win+Shift+Down restores maximized ones without
      Explorer; its hotkey table, monitor order and rectangle transform), so NeoShell takes those keys only for windows
      it snapped or stretched; Win+Left/Right go on to the next monitor and round, Win+Shift+Up stretches, the window's
      own bounds move as win32k moves them (tested against Explorer's numbers, DPI and vertical offsets unit tested);
      Win+Left/Right restore a maximized window and Win+Up maximizes a top quarter, as Explorer does (were wrong);
      DPI-unaware windows are now placed in their own coordinates (they came out a pixel narrow or scaled twice next
      to a 125% monitor). Compared side by side: 13 sequences with two monitors at 100% and 125% and 6 with one, the
      same to the pixel — differs: no move animation (Explorer's ~250 ms slide, as for every Win+arrow), a DPI-unaware
      window's own bounds 1 px off at 125% — not run live: three or more monitors (order of Win+Left/Right), monitors
      offset vertically (unit tested)
- [x] T29 Title-bar shake; Ctrl+Alt+Tab — found with cdb and Ghidra that the shake is spotted by uxtheme in each
      app's process and posted to `Shell_TrayWnd` (0x4F2), which NeoShell owns as the shell: it now does Explorer's
      part (setting off by default, policy, `IsValidShakeWindow`, toggle per window shared with Win+Home, restored
      behind the shaken window); minimize/restore animations as the shell (`HSHELL_GETMINRECT` answered with the
      taskbar button), recorded at 60 fps against Explorer's; Ctrl+Alt+Tab sticky switcher (keys, Space, focus,
      click outside, Delete, no wheel) compared live with Explorer's; Alt+Tab, Snap Assist and the previews' groups
      now by last activation, as Explorer — differs: after Esc or a click outside NeoShell gives the foreground back
      (Explorer leaves it on its hidden switcher); Edge's tabs in Alt+Tab (private `WindowTabHost`) not shown — not
      run live: the shake policy, a low-integrity app's shake
- [ ] T30 Win+V, Win+Period, Win+H, Win+Shift+R, Copilot key — partly: the Copilot key (Win+Shift+F23) and Win+C done as
      Explorer's (`BrandedKey` choice: app toggled or started, search; tested), compared live with the choice unset
      (Explorer opens Settings' page, which can't show as the shell, NeoShell searches); not run live: the "App" and
      "Search" choices (the setting is protected, only Settings writes it), press-and-hold for apps with the
      copilotkeyprovider extension; not possible: Win+V, Win+Period/Semicolon and Win+H (TextInputHost's panels are
      hosted by Explorer's immersive shell in its own window band) and Win+Shift+R (Snipping Tool can't capture a
      monitor without Explorer; Windows.Graphics.Capture's monitor items come from Explorer) — see design.md, Hotkeys
- [x] T31 Snips: freeform, window and full-screen modes, the toolbar, opening in Snipping Tool — NeoShell's overlay as
      Snipping Tool's (toolbar, four modes, Snipping Tool's remembered mode and auto-save setting, clipboard PNG + DIB,
      Snipping Tool's toast whose click opens its editor, which works as the shell), Print Screen by its setting;
      compared live (UIA, 60 fps recordings, pixel measurements) with Snipping Tool's under Explorer, two monitors too
      — differs: no text extractor, colour picker, quick mark-up or recording (recording shown unavailable); the toast
      is a banner only (not kept in the notification center); freeform smoothing differs for sparse pointer input —
      not run live: the MakePrintScreenKeyYieldable policy, Snipping Tool's keyboard handling (no keys pressed with
      Explorer running)
- [x] T32 Quick Settings: brightness, mobile hotspot, VPN, rotation lock; night light and nearby sharing working as
      the shell — through Windows' own Settings handlers, as its Quick Settings does. No keyboard layout tile: 25H2's
      Quick Settings has none. Compared side by side with Windows' own (Win+A; dark and light, accent on and off,
      zoomed grabs and 60 fps recordings): tiles are now shown where Windows' settings environment offers them (no VPN
      tile without a VPN, no hotspot without Wi-Fi, no airplane mode without radios), so the set, order and paging
      match; nearby sharing is split only when Windows offers its page; Cast reads Windows' "Wired display" state;
      glyphs, split tiles, the VPN page, footers and page transitions as Windows'. Checked live: night light and nearby
      sharing (off, My devices, Everyone nearby) switched from either side, a throwaway VPN appearing, "Can't
      connect" and going away. Still differs: Windows' animated icons (night light's moon keeps faint rays), the
      flyout's 2-3 frames of resizing between pages; with light theme and accent on Windows' panel is light-accent
      while NeoShell's stays dark (Quick Settings' backdrop, outside T32). Not run live: brightness (no controllable
      display), rotation lock (no sensor), the hotspot (no Wi-Fi: Windows hides it too), a VPN connecting (no server),
      the nearby sharing page (Windows doesn't offer it here)
- [x] T33 Bluetooth page: connect and disconnect paired devices — as Windows' Quick Settings (found with Ghidra in
      DevicesFlowBroker and DeviceFlows.DataModel): only audio devices connect and disconnect, through a
      `KSPROPSETID_BtAudio` one-shot request to the Bluetooth audio filter behind each of the device's endpoints, plus
      `BluetoothDisconnectDevice` for the link; choosing a paired audio device connects it, a connected one opens with
      Disconnect, 15 s before "Couldn't connect." / "Couldn't disconnect."; statuses in Windows' words ("Connected mic,
      audio"…), battery while connected, one row per device; works the same in both run modes; rows, statuses and
      choices unit tested — not run live: connecting or disconnecting a real device (the VM has no Bluetooth adapter
      and none can be emulated; the KS request was checked up to the VM's HD Audio filter, and the page with fake
      devices in both run modes); Windows' own Bluetooth page can't show here, so its layout wasn't compared
- [x] T34 Accessibility page: switch Colour filters and Mono audio — through the quick actions Windows' Accessibility
      page uses (ControlCenter's `Microsoft.QuickAction.ColorFilters` / `.MonoMix`: the Settings handlers'
      `…_ColorFiltering_IsEnabled` and `…_IsAudioMonoMixStateEnabled`), so both switch, show their state and follow
      changes made elsewhere while the page is open (the filter's handler event; mono through its registry value);
      the links to Settings are gone. Compared side by side with Windows' page (Win+A): same rows, switches and
      On/Off texts. Checked live in both run modes: switched from NeoShell (real clicks and UIA), from Windows' page
      and from another process; the filter applies as the shell (greyscale screenshots) and NeoShell exits cleanly
- [x] T35 Focus sessions: hide badges and flashing, end chime — NeoShell's calendar now runs Windows' own session
      alongside Explorer (Explorer's undocumented focus theme manager, as Explorer's calendar does), so sessions
      started in Explorer, Settings or the Clock app show in NeoShell and NeoShell's everywhere; Windows hides badges
      and flashing (`TaskbarBadges` / `TaskbarFlashing`, which NeoShell's taskbar now follows, forgetting flashes as
      Explorer does), turns on the focus quiet moment (NeoShell's bell and toasts now count it as Do not disturb),
      opens the Clock app's timer and its end-of-session toast is the chime (Alarm04, its `ms-resource:` texts now
      looked up); footer "Focusing" / End session, 30 minutes on each opening, matched to the pixel against
      Explorer's; recorded at 60 fps with loopback audio and Process Monitor — as the shell NeoShell applies the
      settings itself (Windows' manager needs Explorer to start the Clock app), so no Clock timer and no chime there —
      differs: no "Do not disturb is on" banner in the notification center (Explorer shows one for any Do not
      disturb) — not run live: a session left running when switching to shell mode
- [ ] T36 UWP (CoreWindow) apps, Settings among them, in shell mode — not possible: UWP activation needs Explorer's
      immersive shell (twinui.pcshell's Immersive Shell Builder), which starts only in the process that owns the shell
      window and then needs window bands and the shell cloak, which win32k grants only to Microsoft-signed `.imrsiv`
      images (explorer.exe, CustomShellHost for Shell Launcher/Assigned Access, ShellAppRuntime for Windows 365
      Boot). Reproduced with Calculator, traced in the app with cdb, and tried with a test host started as
      CustomShellHost starts it (fail-fast in `CFallbackWindow`'s `CreateWindowInBand`); the Control Panel fallbacks
      stay — see design.md, "UWP (CoreWindow) apps as the shell"

## Future work

Found while working on milestone 19, not done yet.

- [ ] T37 Screen capture as the shell: `Windows.Graphics.Capture` of a monitor fails for every app without Explorer
      (screen sharing, OBS, Snipping Tool's overlay and recorder; see design.md "Hotkeys", T30)
- [ ] T38 The session's change-notification server (`SHChangeNotify`): without Explorer no process serves it, so folder
      windows may not hear of changes made by other processes (T5; not confirmed live)
- [ ] T39 Explorer parity polish, existing differences noticed by the agents:
  - Flyouts don't close when another app is clicked; the Quick Settings button isn't highlighted while it's open; the
    Quick Settings footer's shading is reversed (T8) — flyouts and highlight fixed (T39a): every taskbar flyout and
    menu takes the foreground as Explorer's and closes when it loses it (Win+A and Win+N too); the footer's shading
    fixed (T39b)
  - The first opening of a flyout shows for a frame before it's hidden (T40) — not reproduced (T39a): 60 fps
    recordings of the first Quick Settings, jump list and network menu after a start slide in cleanly
  - Task button menu narrower than Explorer's; its launch item shows a generic glyph, not the app's icon (T10) — fixed
    (T39a): always 296 px as Explorer's, its rows to the pixel, the app's icon; the items' content stays a pixel left
  - The taskbar's right-hand items sit 5 px left of Explorer's (T12); the centred group 1 px left (T15) — fixed
    (T39a): tray, indicators and clock right against each other as Explorer's, to the pixel; the centred Start slot
    45 px and a single window's plate 40 px
  - Network and volume menus: Explorer's are right-aligned with icons, and the network menu has two more items (T13)
    — fixed (T39a): Explorer's items, glyphs, separators, commands (Get Help, Bing's speed test; both work as the
    shell) and places
  - Taskbar tooltips are kept out of the widget sidebar's space (Explorer's show over it); no "open" highlight on the
    clock while the notification center is open (T14) — fixed (T39a): tooltips placed as Explorer's (centred on the
    pointer, 12 px above the taskbar, inside the monitor), the clock's plate; still differs: Explorer's tooltips are
    acrylic, NeoShell's WinUI's
  - Labelled task buttons never narrow when the taskbar is full; Explorer narrows them before collapsing search (T15)
    — fixed (T39a): content-sized up to 180, narrowing together towards 84, then the search collapses, matched step by
    step as the shell; not done: Explorer's smaller buttons and overflow menu after that (NeoShell cuts the row off),
    and its group-by-group "combine when full"
  - A new pin folder swaps in at once (Explorer fades out and in); the folder panel's colour is approximate (T17) — fixed
    (T39c): Explorer's fade-out, pause and fade-in, recorded at 60 fps and matched frame by frame; the panel's colour from
    Start's own `FolderModal` template (in-app acrylic), matched in dark, light and dark accent to 1-3 levels; still
    differs: on a light accent Start, NeoShell's Start (and so its panel) stays dark accent where Explorer 25H2's turns
    light accent
  - All apps is flat (Explorer shows Start Menu folders); no arrow keys between items; no zoom-out for the letter
    index (T23) — fixed (T39c): Start Menu folders
    (`System.Tile.SuiteDisplayName`, from two apps on) in all three views, with Explorer's hidden Windows Tools folders
    and block lists, so All lists Explorer's apps and categories hold its items exactly; Explorer's keyboard behaviour
    (Tab stops, arrows through headers and items, Home/End, Enter, Shift+F10, Esc); the page's zoom-out and back,
    recorded at 60 fps; not compared: opening a folder from inside an open category
  - Win+arrow moves jump; Explorer slides them over about 250 ms (T28)
  - Quick Settings: night light's moon keeps faint sun rays; a 2-3 frame resize between pages; with the light theme
    and accent on Windows' panel is light-accent; footer colour, slider and page-dot spacing; the panel sits 1 px left
    (T32). The Bluetooth page's battery glyph, Disconnect button and row spacing weren't compared (T33) — fixed (T39b),
    from ControlCenter's and DevicesFlowUI's own XAML (decoded from their PRIs): Windows' panel and page colours in
    all four theme/accent combinations (in-app acrylic matched over black, grey and white to 1-3 levels; the light
    accent panel), the footer plain below its rule and the tiles on the lighter layer, 360 wide with the border, the
    volume row, pager, gear and page links to the pixel, the tile icons' sizes and night light's moon with its faint
    rays, the Bluetooth rows, battery glyph and plain Disconnect; the page transition no longer jumps left of the
    widget sidebar nor shows empty frames. Still differs: Windows crossfades old and new content between pages
    (NeoShell cuts), animates nearby sharing's arrow on turning on; the night light moon's swap timing; not run live:
    the Bluetooth page (no radio; checked with fake devices)
  - Snap Assist's cards fade in after 170 ms; Explorer's fly in from the windows after about 450 ms (T27)
  - The auto-hidden taskbar's 2 px edge can't be reached under a topmost maximized window; a maximized window's border
    covers the sidebar's resize grip (T3)
- [x] T40 Robustness and correctness, existing problems noticed by the agents:
  - WinUI access violation when auto-hide was switched through UI Automation with the menu still open (T1) — not
    reproducible: auto-hide switched by UIA Toggle with the menu (and a submenu) open 30+ times, and the taskbars
    remade (`WM_DISPLAYCHANGE`) with each menu and flyout open, alongside and as the shell; the taskbar now closes
    as widget windows do (`Shut`: flyouts closed and forgotten first, `IgnoreMoves`), and a fail-fast found on the
    way is fixed: as the shell with auto-hide on NeoShell ended at every start (the auto-hide tick asked WinUI for
    the popups of a not yet loaded window's null `XamlRoot`)
  - Posting SC_MAXIMIZE to an elevated window is refused, contrary to design.md / `TopLevelWindows.Maximize` (T3)
    — fixed: win32k lets `WM_SYSCOMMAND` through to a higher integrity level only with `SC_MINIMIZE`, `SC_RESTORE` or
    `SC_CLOSE`, and Explorer maximizes and snaps through a call only the immersive broker may make; nothing else gets
    through, so Snap leaves elevated windows out (no preview, bar, Win+arrow or Snap Assist place), documented
  - UI Automation reports the overflow flyout's icons about 360 px left of where they're drawn (T4) — fixed: flyout
    windows were moved with `SetWindowPos` behind WinUI's back (centring, past the work area); they now move by
    their popup's offset, so WinUI places them and UIA matches (jump lists, overflow, Quick Settings, the taskbar
    menu and its submenus)
  - Explorer once laid out the desktop from scratch after a switch: NeoShell had replaced its full-width
    IconLayouts entry with the narrower one saved while the sidebar was open (T26) — fixed: saving replaced every
    desktop differing only in the primary's grid; NeoShell now saves only its own key's and prefers it when reading,
    so Explorer's stays (checked both ways, sidebar open and closed)
  - Test windows started at medium integrity often open below the wallpaper window as the shell; once right after a
    restart the work area read was the whole monitor (T27) — fixed: Windows puts the new window of an app without
    the foreground below the lowest window of the thread in front, which with NeoShell in front is its wallpaper; such
    windows are now put above the others as under Explorer. Snap read Windows' work area, the whole monitor after a
    display change until NeoShell sets it again; it now uses NeoShell's own reservations
- Not possible without Explorer, with the evidence in design.md: Windows' own AutoPlay UI (T5, NeoShell has its own),
  the IME's right-click menu (T8), Win+V, Win+Period, Win+Semicolon and Win+H (T30), importing Explorer's pin folders
  (T17), Spotlight's "Learn about this picture", Next and rotation (T24), Edge tabs in Alt+Tab (T29)

## Later / not planned yet

- Notifications: toast images, buttons and inline replies (not exposed to listeners)

- Jump lists: pinned entries, Pin to / Remove from this list
