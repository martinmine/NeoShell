# NeoShell design

This document describes what NeoShell does and how each feature is built. The milestones are in [plan.md](plan.md);
coding rules are in [CLAUDE.md](../CLAUDE.md).

## Scope

- Display the wallpaper and the desktop icons, with Explorer's context menus for icons and the desktop.
- A WinUI taskbar with feature parity with the Windows 11 taskbar (exceptions in the system tray area).
- System tray icons, either all shown or hidden behind an overflow flyout.
- Network, volume, battery and microphone-in-use indicators; network, volume and battery are one button, as in
  Windows 11, that opens Quick Settings.
- Quick Settings: tiles (Wi-Fi, Bluetooth, Airplane mode, Accessibility, Energy saver, Live captions, Night light,
  Nearby sharing, Cast, Project), the volume slider with its Sound output page, battery and All settings.
- A Start menu with search (apps + Windows Search Indexer), Settings, power options (Lock, Sign out, Sleep,
  Restart, Shut down) and a button to switch back to `explorer.exe`.
- The notification center and calendar from the clock, Do not disturb and focus sessions, and toasts while NeoShell
  is the shell.
- Start's Quick Link menu (right-click Start, Win+X) and, while NeoShell is the shell, Alt+Tab, Explorer's other
  Win+ shortcuts, Snap layouts (Win+Z) and screenshots (Win+PrtScn, Win+Shift+S).
- A widget sidebar, as Windows Vista's: profile and clock, resource usage, pictures, now playing, weather, notes and
  wireless devices' batteries, docked along the right of the screen or dragged out to float on the desktop.

Out of scope: editing Quick Settings' tiles, Windows 11's Widgets board (Win+W), Task View, pinning items in jump
lists, toast images, buttons and inline replies.

## Technical decisions

| Area | Decision | Why |
|---|---|---|
| UI | WinUI 3, unpackaged, self-contained Windows App SDK | A shell starts before anything can install a runtime; MSIX gets in the way of being the shell |
| Target | `net10.0-windows10.0.26100.0`, min. Windows 11 (10.0.22000) | The Windows TFM gives WinRT projections (e.g. `NetworkInformation`) without packages |
| Look | Windows 11 Fluent, `DesktopAcrylicController` (or `MicaController`) with a `SystemBackdropConfiguration` whose `IsInputActive` stays `true` | Keeps the acrylic on windows that rarely have focus |
| Interop | Hand-written `[LibraryImport]` and `[GeneratedComInterface]` in `NeoShell.Interop` | BCL only, trim/AOT friendly, readable |
| Win32 messages | Message-only windows (`MessageWindow`) and `SetWindowSubclass` (`WindowSubclass`) on WinUI HWNDs | WinUI doesn't expose a WndProc |
| Search | `ISearchQueryHelper` builds SQL; `System.Data.OleDb` runs it against `Search.CollatorDSO` | The supported way to query the indexer |
| Settings | `System.Text.Json` record in `%LOCALAPPDATA%\NeoShell\settings.json` | |
| Logging | Own small file logger in `%LOCALAPPDATA%\NeoShell\logs` | |

Approved packages: `Microsoft.WindowsAppSDK`, `System.Data.OleDb`, and for tests `xunit`,
`xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`. Anything else needs the owner's approval.

### WinUI windows as shell surfaces

All shell surfaces (taskbar, Start menu, wallpaper, flyouts) are WinUI `Window`s configured through `AppWindow`:

- `OverlappedPresenter`: `SetBorderAndTitleBar(false, false)`, `IsResizable = false`, `IsMaximizable/IsMinimizable = false`.
- Extended styles set through Interop: `WS_EX_TOOLWINDOW` (not in Alt+Tab), and `WS_EX_NOACTIVATE` where
  the surface should not steal focus (taskbar).
- Taskbar and Start menu are topmost (`IsAlwaysOnTop`); the wallpaper window is forced to `HWND_BOTTOM`.
- Sizes are in physical pixels from `AppWindow`; convert using the window's DPI (`GetDpiForWindow`).
- A `ShellBackdrop` (acrylic or Mica kept active, or a see-through colour) can have several targets at once: a
  menu's backdrop is used by its submenus' popups too. They share one controller (or brush), disposed with the last
  target. A controller left behind closes itself when the dispatcher shuts down and touches a popup that's gone (a
  crash in `CPopup::GetSystemBackdrop` on exit after the taskbar's backdrop submenu had been used).

## Application lifecycle

- **Custom `Main`** (`Program.cs`):
  - Single instance via a named mutex. A second instance with `/exit` posts a registered message
    (`NeoShell_Exit`) to the running instance and exits; without arguments it just exits.
  - Detects the run mode: when `GetShellWindow() == 0` it registers as the shell right away (`ShellRegistration`:
    `SetShellWindow` on a hidden window of its own, on the thread WinUI then runs on) and runs in shell mode; if that
    fails, or another shell exists, it runs alongside. Registering this early matters: a killed Explorer is restarted
    by Winlogon within a second or two.
  - Installs crash handlers (`AppDomain.UnhandledException`, `Application.UnhandledException`,
    `TaskScheduler.UnobservedTaskException`) that log. WinUI fail-fasts on an exception in a `DispatcherQueue`
    callback without raising any of these, so UI-thread work goes through `UiThread.Post` and a logging
    `SynchronizationContext`, which log the exception before it ends the process.
  - **Watchdog** (shell mode): `Main` starts `NeoShell.exe /watch <pid>`, which waits for NeoShell to exit. A clean
    exit returns 0; any other exit (crash, fail-fast, stack overflow, killed from Task Manager) makes the watchdog
    start `explorer.exe` — unless another shell has registered or the session is ending. NeoShell is gone by then, so
    Explorer becomes the shell rather than opening a folder window (which it does while a shell is registered).
  - Native callbacks (window procedures, subclasses, hooks) catch every exception and raise
    `NativeCallback.UnhandledException`, which the app logs: an exception escaping `[UnmanagedCallersOnly]`
    would end the process without running any handler.
- **`App`** creates and wires the long-lived objects by hand: settings, logger, `ShellSession` (shell mode only),
  taskbars per monitor, Start menu, tray host, indicators.
- **Clean exit** disposes in reverse order: unregister AppBars (frees the reserved space), unhook hooks,
  destroy `Shell_TrayWnd`, release COM objects.

### Shell mode (`ShellSession`)

1. `SetShellWindow` (done in `Main`, see above; a dedicated hidden top-level window rather than a wallpaper window,
   which is recreated on display changes) and `SetTaskmanWindow` on the same window, which then receives
   `WM_SYSCOMMAND SC_TASKLIST` for Ctrl+Esc. The registration is released last on exit, so Explorer started by
   Switch to Explorer becomes the shell.
2. Create `Shell_TrayWnd` (see Tray) and broadcast `TaskbarCreated`.
3. Signal the shell-ready events (`Local\ShellDesktopSwitchEvent`, `msgina: ShellReadyEvent`, whichever exist) so
   logon completes. Also set `ARW_HIDE` in `SPI_SETMINIMIZEDMETRICS` (not saved, as Explorer does), otherwise
   minimized windows are drawn as small title bars along the bottom of the screen.
4. Run startup apps (below), queued at low priority after the tray exists.
5. Register hotkeys and the low-level keyboard hook (see Hotkeys).
6. Handle `WM_QUERYENDSESSION` (always allow) / `WM_ENDSESSION` on the shell window: shut down cleanly before Windows
   ends the process. Settings are already saved on every change.

### Startup apps (shell mode only)

Explorer, not Windows, launches startup apps. As the shell NeoShell must do the same, otherwise OneDrive,
chat apps, password managers, GPU/audio/Bluetooth utilities etc. never start and the tray stays empty.

- Sources, in Explorer's order:
  1. `HKCU\...\RunOnce` — delete each value before running it (after, for names starting with `!`), as Explorer
     does. `HKLM\...\RunOnce` needs an administrator to delete its values; like Explorer, NeoShell leaves it.
  2. `HKLM\...\Run`, `HKLM\...\WOW6432Node\...\Run`, `HKCU\...\Run`.
  3. `shell:common startup` and `shell:startup` folders (not `desktop.ini`).
- Respect `HKCU|HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{Run,Run32,StartupFolder}`,
  in the hive of the entry: a value whose first byte is odd (e.g. `0x03`) means disabled in Task Manager.
- Split command lines the way `CreateProcess` does (quoted program, else the shortest run of words naming an existing
  file, with `.exe` tried), after expanding environment variables; launch with `ShellExecuteEx` (`Process.Start`
  with `UseShellExecute`) in the program's folder.
- Run once per session: Explorer marks it with the volatile keys `HKCU\...\Explorer\SessionInfo\<session id>\
  StartupHasBeenRun` and `RunStuffHasBeenRun`; NeoShell checks the first and sets both, so a NeoShell restart, or
  Explorer after Switch to Explorer, doesn't launch them again.
- Parsing and the `StartupApproved` decision are unit tested.

### Switch to Explorer

Start menu button → confirmation dialog → delete the per-user `Winlogon\Shell` value (HKCU only) → start
`explorer.exe` → exit NeoShell cleanly (so the AppBar space and `Shell_TrayWnd` are released first, then Explorer starts).

## Wallpaper (`Desktop/`)

- One `WallpaperWindow` per monitor covering the full monitor bounds, kept there and at `HWND_BOTTOM` by
  `BottomWindow` (rewrites `WM_WINDOWPOSCHANGING`).
- Frameless through `FramelessWindow`: a borderless `OverlappedPresenter` still keeps `WS_DLGFRAME` and restores it on
  every style change, so `WM_STYLECHANGING` strips the frame and isn't passed on; DWM border and rounded corners off.
- Not `AppWindow.IsShownInSwitchers`: it goes through the taskbar and throws when there is none; `WS_EX_TOOLWINDOW`
  keeps the window out of Alt+Tab instead.
- Reads `HKCU\Control Panel\Desktop`: `Wallpaper`, `WallpaperStyle`, `TileWallpaper`, and
  `HKCU\Control Panel\Colors\Background` for the fill colour.
- Layout (unit tested): `WallpaperLayout.Arrange` returns the image rectangles in physical pixels for each style —
  Fill, Fit, Stretch, Center (exact pixels), Tile (one rectangle per tile; WinUI has no tiled brush) and Span (Fill
  over the virtual screen, offset per monitor). The window places `Image` elements on a `Canvas` at those rectangles.
- Any `WM_SETTINGCHANGE` or `WM_SYSCOLORCHANGE` re-reads the settings and reloads only if path, style, colour or the
  file's timestamp changed; `WM_DISPLAYCHANGE` recreates the windows. Bursts of broadcasts become one update.
- Shell mode only.

## Desktop icons (`Desktop/`)

Shell mode only, like the wallpaper: alongside Explorer, Explorer's desktop has its own icons.

- **Contents.** `DesktopFolder` (Interop) enumerates the desktop's `IShellFolder` (`SHGetDesktopFolder`), which
  merges the user's and the public Desktop folders and also lists the namespace (This PC, Libraries, OneDrive,
  drives…). `DesktopContents.IsShown` (unit tested) keeps what Explorer's desktop shows: items directly in one of the
  two Desktop folders, and the five icons of "Desktop icon settings" (This PC, User's Files, Network, Recycle Bin,
  Control Panel) as chosen in `HKCU\…\Explorer\HideDesktopIcons\NewStartPanel` (1 hides, 0 shows; only the Recycle
  Bin shows by default). Hidden and protected files follow Explorer's `Hidden` and `ShowSuperHidden` values.
- Each item is kept as its desktop-relative ID list (a byte array; a child of the desktop is also an absolute PIDL),
  so one menu can cover several items, even from both Desktop folders.
- **Order** (unit tested): system icons first in Explorer's order, then folders, then files, by the Sort by choice
  (Name, Size, Item type, Date modified; `ShellSettings.DesktopSortOrder`) and then by name, numbers compared by
  value.
- **Places** (`DesktopGrid`, unit tested): the icons sit on a grid of cells over the primary monitor's work area, as
  Explorer's do with Align icons to grid (always on here). Icons fill columns from the top left in sort order, and
  can be dragged anywhere on the grid; every icon's cell is then remembered (`ShellSettings.DesktopIconPositions`, by
  parsing name; Explorer's own `IconLayouts` is undocumented), so a moved or deleted icon leaves a gap and new ones
  fill the first gaps. A cell that's taken, or off a grid that got smaller (taskbar, icon size, resolution), sends
  the icon to the nearest free cell. A dragged icon goes to the cell nearest to where it was under the pointer (the
  grab point kept), the rest of the selection alongside. Files dropped on the desktop, and items made with New, go
  to the cell where that happened, as in Explorer. Sort by packs the icons again and forgets their places; a
  rename keeps the place. Arrow keys move the selection to the nearest icon that way, keeping to the row or column
  where they can.
- **View settings** live where Explorer keeps them, so they carry over when switching shells: the icon size in
  `HKCU\Software\Microsoft\Windows\Shell\Bags\1\Desktop\IconSize` (32/48/96), "Auto arrange icons" as `FWF_AUTOARRANGE`
  (bit 0x1) of that key's `FFlags` (icons stay packed in sort order; turning it on forgets their places), "Show
  desktop icons" in `Explorer\Advanced\HideIcons`.
- **Images** come from `IShellItemImageFactory` without `SIIGBF_ICONONLY`, so pictures get thumbnails, loaded off the
  UI thread at physical pixel size. Shortcuts get the stock link overlay (`SHGetStockIconInfo(SIID_LINK)`) in the
  corner, at most medium-icon size. Labels are white over a dark copy offset by a pixel, readable on any wallpaper.
- **Updates.** `FileSystemWatcher`s on both Desktop folders and on each fixed drive's `$Recycle.Bin\<SID>` (the
  Recycle Bin icon shows whether it's empty), and every `WM_SETTINGCHANGE` (folder options, Desktop icon settings,
  the work area), queue a debounced refresh. A refresh enumerates off the UI thread and updates the
  `ObservableCollection` in place (remove, move, insert), so the selection and loaded images survive.
- **View** (`DesktopIconsView`): a `GridView` (extended selection; `DesktopIconPanel` places each container at its
  icon's cell) in the primary monitor's
  `WallpaperWindow`. Double-click or Enter opens; Delete, F2, F5, Ctrl+C/X/V and Alt+Enter work as in Explorer; a
  click on the empty desktop clears the selection. Dragging from the empty desktop draws a selection rectangle (accent
  coloured) and selects every icon it touches; with Ctrl held it adds to the selection.
- **Drag and drop** as on Explorer's desktop (`DesktopDragDrop`, Interop). Whatever is dropped on an icon that takes
  drops (`SFGAO_DROPTARGET`: a folder, the Recycle Bin, an app) or on the desktop itself goes to the shell's own
  `IDropTarget` for it (`GetUIObjectOf` for an icon, `CreateViewObject` for the desktop: the Desktop folder), so
  moving, copying and linking by the keys held, confirmations and progress are Explorer's. The icon under a drag is
  highlighted.
  - From other apps: a native OLE drop target, registered on WinUI's content window
    (`Microsoft.UI.Content.DesktopChildSiteBridge`) — OLE looks only at the window under the pointer, so one on the
    top-level window is never asked. The shell's targets need the drag's own data object, which WinUI's drop events
    don't give. `IDropTargetHelper` draws the source's drag image over the desktop.
  - Out to other apps: WinUI's drag (`StartDragAsync` on the icon's container, the files and folders as storage
    items, copy, move and link allowed), once the pointer has moved 4 epx with the button down on an icon; an icon
    that isn't selected is selected first. OLE's own drag loop (`SHDoDragDrop`) can't be used: WinUI turns on mouse
    in pointer, and the loop never sees the mouse move or the button go up. The storage API won't open anything in
    a hidden folder, so the public Desktop's shortcuts go as copies made in `%TEMP%\NeoShell\Dragged` (a streamed
    file instead hangs WinUI as it's added to the drag). System folders drag only within the desktop. A throwing
    async `DragStarting` handler ends the app, and an empty storage item list throws.
  - The desktop's own icons over the desktop: WinUI's drag reaches only WinUI's drop events in its own process, not
    OLE targets, so those events pass it on to the same logic with the shell's data object for the items
    (`GetUIObjectOf(IDataObject)`) in place of the drag's.
- **Menus**: Explorer's full menu (what its "Show more options" shows) in one WinUI menu, without that item.
  - `ShellMenu` (Interop) builds the shell's own `IContextMenu` (`GetUIObjectOf` for icons, `CreateViewObject` for
    the desktop, `CMF_NODEFAULT` there as Explorer does), fills the submenus filled on opening (New, Send to, Open
    with) by sending `WM_INITMENUPOPUP` through `IContextMenu3`, and reads the `HMENU` into items: label (access
    keys and shortcut text removed), verb, state, the item's bitmap and submenus. NeoShell shows them as
    `MenuFlyout` items; the handler's image, else a glyph for standard verbs (cut, copy, delete...).
  - The chosen command is invoked by its ID once the WinUI menu has closed, so it behaves exactly as in Explorer
    (Recycle Bin, confirmations, progress, installed apps' commands). The `ShellMenu` is released after that.
  - Icons: the shell's menu as is (Open, Open with, Send to, Give access to, Cut, Copy, Create shortcut, Delete,
    Rename, Properties, installed apps' commands...).
  - Desktop: what Explorer's view adds itself, View (icon size, Show desktop icons), Sort by, Refresh, Paste and
    Paste shortcut, then the shell's menu: installed apps' commands, New (the shell's New menu: Folder, Shortcut and
    every registered file type) and Display settings / Personalize. Explorer's Undo is left out: its undo history
    is Explorer's own.
  - Display settings and Personalize open the Settings app, which can't start while NeoShell is the shell (the only
    time the desktop is NeoShell's), and Control Panel's pages for them open Settings too. They open the classic
    dialogs left instead: the display adapter's properties (`display.dll,ShowAdapterSettings`, with List All Modes)
    and Desktop icon settings (`desk.cpl,,0`).
  - Rename comes back to NeoShell (only the view can edit a name); an item made from New is renamed straight away,
    as in Explorer.
- **Rename**: a text box in a flyout over the label, with the name selected without its extension; Enter or a click
  elsewhere renames through `IShellFolder::SetNameOf` (keeps a hidden extension, reports errors in the shell's
  dialogs), Esc cancels.
- After a file operation the desktop takes the foreground back: the shell's operation windows hand it to the next
  window in the z-order when they close, and that is never the bottom-most desktop.
- **Alt+F4** on the desktop (or on the taskbar while it has the keyboard) doesn't close the window
  (`AppWindow.Closing` is cancelled; NeoShell's own `Close` doesn't raise it) but opens the **Shut Down Windows**
  dialog (`ShutDownDialog`), as Explorer does: "What do you want the computer to do?" with Sign out, Sleep, Shut
  down (chosen) and Restart, a line on what the choice does, OK and Cancel (Enter and Esc). shell32's own dialog
  (`ExitWindowsDialog`, ordinal 60) hands the request to Explorer's taskbar and shows nothing without it. A WinUI
  window with Mica, in the middle of the primary monitor; one at a time.

## Taskbar (`Taskbar/`)

### Window

- One `TaskbarWindow` per monitor (setting: show on all displays), bottom edge, 48 effective pixels high, kept in
  place and topmost by `PinnedWindow`, frameless (`FramelessWindow`), `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
- Screen space:
  - Alongside Explorer: registered as an AppBar with `SHAppBarMessage` (`ABM_NEW`, `ABM_QUERYPOS`, `ABM_SETPOS`,
    `ABM_REMOVE`); Explorer places it above its own taskbar and sends `ABN_POSCHANGED` when it must move.
  - As the shell: `SHAppBarMessage` is served by Explorer's `Shell_TrayWnd`, so it doesn't work. NeoShell sets the
    monitor's work area itself (`SPI_SETWORKAREA`) and restores it on exit. Serving other apps' AppBar messages
    belongs with `Shell_TrayWnd` (Tray). `ShellWorkArea` keeps what the taskbar and the widget sidebar reserve and
    sets each change from the thread pool, one at a time: `SPIF_SENDCHANGE` waits for every window, and apps that
    answer by calling the shell (re-adding tray icons) would wait for the UI thread in turn (startup hung that way).
    Windows doesn't resize maximized windows for a new work area; NeoShell doesn't either (yet).
- Rect calculation (unit tested) from monitor bounds and DPI.
- Recreated on `WM_DISPLAYCHANGE`, on `WM_DPICHANGED` to a DPI other than the monitor's, and on settings changes.
- Full-screen apps: while the foreground window covers its monitor (or the app marked it with
  `ITaskbarList2::MarkFullscreenWindow`), that monitor's taskbar leaves the topmost band and sits just below it.
  Maximized windows, minimized ones, NeoShell's own and the desktop (shell window, `Progman`, `WorkerW`) don't
  count. Re-checked on foreground changes, minimize/restore and the foreground window moving (`EVENT_OBJECT_LOCATIONCHANGE`).
  Explorer would send `ABN_FULLSCREENAPP`; NeoShell is the one deciding, alongside Explorer too.
- Auto-hide (setting): no screen space is reserved (no AppBar, no work area change). The taskbar slides down until
  2 pixels show; the pointer entering them slides it back. It hides again after ~0.75 s with the pointer off it,
  unless a menu or flyout is open, the thumbnails or Start are open, or it has the keyboard (Win+T). Start, Win+T and
  Win+1…9 reveal it first.
- Backdrop by setting (`ShellBackdrop`), chosen in the taskbar menu: Acrylic (default) or Mica — a
  `DesktopAcrylicController` or `MicaController` whose configuration keeps `IsInputActive` true — or Translucent: a
  see-through colour brush in the window's system backdrop slot, with DWM blur-behind on an empty region so the
  window's alpha shows the desktop instead of black — or Transparent: the same, with a fully clear brush that ignores
  the accent colour. Mica falls back to Acrylic where unsupported. Start and the
  thumbnails stay Acrylic.
- Light/dark following the system theme (`HKCU\...\Themes\Personalize\SystemUsesLightTheme`), re-read on every
  `WM_SETTINGCHANGE`.
- "Show accent color on Start and taskbar" (`Personalize\ColorPrevalence`): the acrylic of the taskbar and Start is
  tinted with the second darker shade of `HKCU\...\Explorer\Accent\AccentPalette`, as Explorer does, and their
  text is light or dark by that colour's brightness. Re-read on `WM_SETTINGCHANGE` with the theme.
- Flyouts and menus (`TaskbarFlyouts`) open as in Windows 11: above the taskbar with a 12 epx gap, sliding up from
  behind it. A task button's menu (the jump list) is centred on the button, the overflow above the chevron, the
  volume flyout and the calendar at the right of the screen, 12 epx from its edge.
  Each is opened by hand (`ShowAt` with a position 12 epx above the taskbar's top edge; not a presenter margin, which
  WinUI leaves out when it places the popup): context menus by handling `ContextRequested` (a `ContextFlyout` would be opened by WinUI itself, at
  the pointer, before any handler runs), button flyouts as attached flyouts opened on `Click`. WinUI's Top placement
  puts the flyout's edge, not its middle, at the point given, and the width is only known once it's open, so a
  centred flyout opens hidden and is shown again, moved by half its width (kept 12 epx from the screen's edges).
- The taskbar's own menu is the exception, as in Explorer: its bottom-left corner is at the pointer, over the taskbar,
  and it opens with WinUI's own animation instead of sliding. WinUI keeps popup windows inside the work area, which
  leaves out the taskbar, so the menu opens at the taskbar's top edge and its popup windows (its submenus' too) are
  subclassed to go the rest of the way down whenever WinUI places them (`PopupWindows.Offset`).
- They're unconstrained (`ShouldConstrainToRootBounds="False"`, the window is only as tall as the taskbar), so each
  popup is a window of its own (`PopupWindows`, class `Microsoft.UI.Content.PopupWindowSiteBridge`), owned by the
  taskbar and so always in front of it, and its acrylic belongs to that window. To come out from behind the taskbar
  anyway, WinUI's own open animation is off and the popup window itself slides up from the taskbar's edge (200 ms,
  frame by frame) under a window region that cuts off what's still below the edge. When `Opened` comes, the window
  may not exist yet or be in place: WinUI keeps a flyout's window between openings, so it's remembered per flyout;
  on the first opening it's the taskbar's popup window no other flyout has. It's hidden at once, and slides once
  it's shown where the flyout belongs.
- Hidden means cloaked (`DWMWA_CLOAK`), from then on whenever WinUI shows the window (`SWP_SHOWWINDOW`) until the
  slide places it (`PopupWindows.Conceal`): WinUI shows a reused window where the flyout last was a frame before
  `Opened`, and clears a window region of its own accord while it lays the menu out, so an empty region let the
  flyout flash at its final place before sliding (the jump list's "bounce", the tray flyout's after a few openings).
  While it slides, WinUI's own moves of the window (another layout pass) are held at the slide's position.
- The taskbar's menus (jump lists, the taskbar menu, network and speaker menus, the Quick Link menu) get a
  `ShellBackdrop` of their own with a see-through presenter: WinUI's menu backdrop turns solid while the menu's
  window is inactive, as a menu of the no-activate taskbar always is, where Explorer's stay acrylic. Submenus keep
  WinUI's (no way to give them a backdrop).
- A right-click on a task button stops its previews: none open while its menu is, and the button shows none again
  until the pointer has left it (a hover that began before the click would otherwise open them over the menu).
- Start's corner: as in Explorer, a click on the taskbar around the Start button acts on it, with its hover and
  press states — left-aligned, everything from the screen's left edge to the button's right, at any height (the
  corner pixel opens Start); centred, also the 13 epx gap on the button's left (`TaskbarLayout.IsStartZone`).
- Quick Link menu (`QuickLinkMenu`): right-clicking Start or its corner, or Win+X as the shell (keyboard hook,
  `PanelKeys`; the taskbar takes the keyboard for the arrow keys), opens Explorer's list above the Start button,
  left edges aligned: Installed apps, Power Options, Event Viewer, System, Device Manager, Network Connections, Disk
  Management, Computer Management, Terminal and Terminal (Admin) (Windows PowerShell without Terminal), Task Manager,
  Settings, File Explorer, Search, Run, Shut down or sign out (Sign out, Sleep, Shut down, Restart), Desktop. As the
  shell, Settings pages are Control Panel applets. Run is shell32's `RunFileDlg` (ordinal 61) on a thread of its
  own, moved above the taskbar's left end by a thread CBT hook as it activates.
- Show desktop minimizes every minimizable window of other processes (`SW_SHOWMINNOACTIVE`) and the next click
  restores those still minimized; Explorer's own toggle isn't available as the shell. `ShowDesktop` also does
  Win+M (minimize all, adding to those minimized before), Win+Shift+M (restore them) and Win+Home (all but the
  window in front; again restores them without activating, though some apps, Chromium's, activate themselves).

### Layout (left → right, or centred like Windows 11 by setting)

1. Start button.
2. Search button — opens the Start menu with the search box focused. Can be hidden from the taskbar menu.
3. Pinned and running apps.
4. Tray area: chevron/overflow, tray icons.
5. Indicators: microphone (when active), then network, volume and battery as one button (Quick Settings).
6. Clock: time and short date, and Do not disturb's bell while it's on; tooltip with the full date and the day and
   time, as Explorer's; click opens the notification center and calendar (see Notifications and calendar).
7. Show-desktop sliver at the far right edge.

### Window tracking

- `ShellHook` (`RegisterShellHookWindow` on a message window): `HSHELL_WINDOWCREATED`, `WINDOWDESTROYED`,
  `WINDOWACTIVATED`/`RUDEAPPACTIVATED`, `REDRAW`, `FLASH`, `WINDOWREPLACED`.
- `SetWinEventHook` for `EVENT_OBJECT_NAMECHANGE`, `EVENT_OBJECT_SHOW/HIDE`, `EVENT_OBJECT_CLOAKED/UNCLOAKED`,
  `EVENT_SYSTEM_FOREGROUND`, `EVENT_SYSTEM_MINIMIZESTART/END` to catch what shell hooks miss.
- Initial list from `EnumWindows`; afterwards each event re-reads only the window it is about (`WindowTracker`).
  Show/uncloak events of unknown windows are checked, hide/cloak of unknown ones ignored (menus and tooltips fire
  these constantly); title changes only re-read the title. Changes are coalesced into one refresh per burst.
- **Which windows get a button** (unit tested on a `WindowInfo` snapshot), Explorer's rules:
  - visible, not cloaked (`DWMWA_CLOAKED`), and
  - `WS_EX_APPWINDOW`, or (no owner and not `WS_EX_TOOLWINDOW` and not `WS_EX_NOACTIVATE`).
  - Exclude NeoShell's own windows.
- **Grouping** (unit tested): by AppUserModelID from `SHGetPropertyStoreForWindow` (`PKEY_AppUserModel_ID`), else the
  packaged process's (`GetApplicationUserModelId`), falling back to the process image path
  (`QueryFullProcessImageName`). A pinned app matches windows by AUMID, or by path for windows without one.
- App names: the `shell:AppsFolder\<AUMID>` item's display name, else the executable's `FileDescription`.
- Icons are read on a background thread and arrive as premultiplied BGRA bytes for a `WriteableBitmap`:
  - window icon: `WM_GETICON` (`ICON_BIG`/`ICON_SMALL2`/`ICON_SMALL`, `SMTO_ABORTIFHUNG`), then the class icon;
  - app icon (combined and pinned buttons): `IShellItemImageFactory` on the AppsFolder item or the executable.
  - Alpha comes straight, premultiplied, or not at all (AND mask); `IconBitmap.PremultiplyAlpha` tells them apart.

### Task buttons

- A `ListView` of `TaskButton` view models (built by `TaskListBuilder`, unit tested, and synced in place by key).
- The list's own item transitions are off: WinUI turns a collection move into a removal and an insertion, so a moved
  button would vanish and fade back in. The taskbar animates changes itself with composition animations on the
  containers (explicit start values): buttons that stay slide from where they were, new ones grow in (scale and
  fade). A refresh during an animation lets it run on; a drag first finishes running ones at their end values
  (stopping one would leave a half-faded button).
- Sized as Explorer's: 44×48 buttons with a 40×40 plate behind the 24 px icon, shown when hovered or active.
- Packaged apps' icons come from their package's `targetsize-24_altform-unplated` image, as in Explorer; the shell's
  24 px icon is scaled from a bigger image and comes out a pixel off.
- Indicators: running (short grey pill), active (long accent pill and plate), several windows (the plate shows a
  second card's edge behind it), flashing (amber background until activated).
- Left click: one window → activate it, or minimize if it's already foreground; several windows → show thumbnails.
  Pinned, not running → launch.
- No tooltip: hovering shows the thumbnails instead.
- Middle click or Shift+click: launch a new instance. Shift is read with `GetAsyncKeyState`: the taskbar never has
  focus, so its thread's key state doesn't see it.
- Right click menu: the app's jump list (below), then app name (launch), Pin to taskbar / Unpin, Close window / Close
  all windows (`SC_CLOSE`).
- Pressed, the icon shrinks to 0.8 (only the icon, as in Explorer); dragged, it grows to 1.2 and loses its plate and
  pill (`IconPress`, a `ScaleTransition` on the icon). The Start and Search buttons' icons shrink too.
- Drag to reorder (by hand, `TaskReorder`: the button slides along the row and its neighbours make way). The order
  is persisted for pinned apps and kept for the session for the others (`TaskOrder`, unit tested): buttons keep the
  order last shown, a new window goes next to its app's others and a newly started app at the end. Shared by the
  taskbars and kept when they're recreated.
- Combine setting: always combine, combine when full, never combine (labels shown). "When full" compares the labeled
  buttons' width with the space left beside the right-hand panel (on both sides when centred); the list is capped at
  that width so it never covers the clock.
- Activation uses `SetForegroundWindow` (allowed: the user's click on the taskbar was the last input); minimized
  windows are restored with `ShowWindow(SW_RESTORE)`. Clicking the active window's button minimizes it.

### Pinned apps

- Stored in settings as a list of `{ AppUserModelId or Path, Arguments, DisplayName }`.
- Launch packaged apps (AUMID `<family>!<app>`) with `IApplicationActivationManager::ActivateApplication`, on a
  background thread as it waits for the app; other apps through `shell:AppsFolder\<AUMID>` when there is an AUMID,
  otherwise `ShellExecuteEx` on the path. Opening `shell:AppsFolder\<packaged AUMID>` needs a handler hosted by
  Explorer and fails without it ("Class not registered").
- UWP (CoreWindow) apps, Settings and Calculator among them, can't show in shell mode: their windows stay cloaked
  without Explorer's view management, and activation fails. Packaged desktop apps (Notepad, Terminal) work.
- Pinning from the Start menu and from a task button's context menu.
- The first time Start's catalog loads, Explorer's taskbar pins are added once (`ExplorerTaskbarPinsImported`),
  matched to catalog apps as Start's are. The shortcuts in `User Pinned\TaskBar` have no order and packaged apps
  have none, so they're read from `HKCU\…\Explorer\Taskband\Favorites`: a version byte, then per pin a 32-bit
  length, the ID list and a separator byte (0xFF after the last). Each ID list is resolved with
  `SHCreateItemFromIDList` to its AppUserModelID and shortcut target.

### Jump lists

The app's jump list heads the button's menu, read each time it opens (`JumpLists`, Interop): the app's own
categories and Recent or Frequent, in the app's order, then its Tasks. Headings are text-only menu items; entries
have their icons (loaded in the background) and open on click.

- **AppID**: the button's AppUserModelID, or for an app without one the implicit AppID Windows gives it: its path
  starting with a known folder's GUID where it can (`{1AC14E77-…}\notepad.exe`), as in `shell:AppsFolder` (tested).
- **The app's list** (`ICustomDestinationList`) can't be read back through any API. Windows keeps it in
  `%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\<name>.customDestinations-ms`, where the name is a CRC-64 of the
  AppID in capitals as UTF-16 (polynomial 0x92C64265D32139A4, reflected, starting from all ones; hex without leading
  zeros; tested against Windows' own file names). `CustomDestinations` (tested) reads it: version 2, the category
  count; per category its kind (custom with a title, known with Frequent 1 / Recent 2, or tasks), the entries and a
  0xBABFFBAB footer. An entry is a CLSID and the object's persisted data, in practice a shell link; a link's data
  carries no length, so it's measured from its structure ([MS-SHLLINK]) and then loaded into the shell's own
  `ShellLink` with `IPersistStream::Load` (from `SHCreateMemStream`).
- An entry's title is the link's `System.Title`, else its description, resolved with `SHLoadIndirectString` when it's
  a resource reference (`@shell32.dll,-21817`); `System.AppUserModel.IsDestListSeparator` links are separators.
- **Known categories** come from `IApplicationDocumentLists` (Recent or Frequent, at most 10, as Explorer). An app
  without a list of its own gets Recent.
- **Opening**: a link through its own `IContextMenu` default command, which keeps its arguments, working directory and
  a packaged app's identity, as Explorer does; a recent file with `ShellExecuteEx` on its ID list (the default verb,
  which may not be the app the list belongs to).
- Icons: a link's icon location (`SHDefExtractIcon`), else its target's icon; `ms-appx:` icons of packaged apps
  aren't read, so those entries show the target's.
- Pinned entries (kept in `AutomaticDestinations`) aren't shown, and entries can't be pinned or removed.

### Thumbnails

- Hovering a button (500 ms) opens a popup with one live DWM thumbnail per window (`DwmRegisterThumbnail` on the
  popup's HWND, `DwmUpdateThumbnailProperties` to place each one over a XAML placeholder), title and close button.
  It closes 400 ms after the pointer leaves both the button and the popup; once open, it follows the pointer along
  the taskbar straight away. Only a pointer heading up off the taskbar (steeper than ~27°, over its last 150 ms,
  `TaskbarLayout.IsHeadingUp`, tested) takes 200 ms to move it to a button it enters, so a neighbour crossed on the
  way up to the previews doesn't take them over (Explorer pauses 200 ms on every button).
- The popup slides up out of the taskbar (200 ms) and back into it on closing, and slides sideways to the next button
  (`WindowSlide`, as Start): it's the window that moves, since DWM draws the thumbnails into the window. It sits
  just below the taskbar in the topmost band (`PinnedWindow.SetLayer(Topmost, above)`), so the taskbar covers it.
- Peek: hovering a thumbnail for 400 ms shows only its window (`DwmpActivateLivePreview`, dwmapi ordinal 113: the
  undocumented call Explorer's taskbar makes, as there is no public one); moving to the next thumbnail moves the peek
  straight away, and it ends when the pointer leaves the thumbnails, on a click (after switching) or when the popup
  closes. DWM ignores a second peek while one is on, so moving it ends the first, which crossfades. The taskbar,
  popup and wallpaper windows set `DWMWA_EXCLUDED_FROM_PEEK` to stay visible, as Explorer's do.

### Progress, overlay badges

Apps call `ITaskbarList3`, which is implemented in `explorerframe.dll` inside the app process: it finds the task
band through the `TaskbandHWND` window property on `Shell_TrayWnd` and sends it messages (`HrInit` fails with
`E_NOTIMPL` without it). So in shell mode `TrayHost` creates an `MSTaskSwWClass` child of `Shell_TrayWnd` and sets
that property; alongside Explorer these calls go to Explorer. Messages (`TaskbarListCall`, unit tested):

| Message | Call | wParam | lParam |
|---|---|---|---|
| `WM_USER+65` | `SetProgressState` | window | `TBPF_*` |
| `WM_USER+64` | `SetProgressValue` | window | 0…0xFFFE (scaled by the caller) |
| `WM_USER+79` | `SetOverlayIcon` | window | `HICON`, 0 removes |
| `WM_USER+60` | `MarkFullscreenWindow` | flag | window |
| `WM_USER+85` | overlay description | window | atom (ignored) |

Apps only start once told their button exists: the `TaskbarButtonCreated` registered message, sent with
`SendNotifyMessage` when a window is added to the task list (shell mode). The task button shows the first window's
progress (bar along the bottom; indeterminate, error and paused states) and overlay icon (bottom-right of the icon).

### Hotkeys (shell mode only)

Explorer's own shortcuts: without Explorer nothing answers them. Which process holds a combination can be probed
with `RegisterHotKey` (it fails while another has it): as the shell, Windows' components keep Win+A/K/P/Ctrl+V
(Quick Settings' host), Win+L, Win+U, Win+O, Win+C, Win+Plus, Win+Ctrl+Enter, Win+Space, Win+Enter, Snap's
Win+arrows and Ctrl+Shift+Esc; the rest were Explorer's.

- Low-level keyboard hook (`WH_KEYBOARD_LL`): Win pressed and released alone, or Ctrl+Esc → toggle Start
  (`StartKeyDetector`, unit tested). Keys are not swallowed (but for Quick Settings' shortcuts, below): Windows has
  to see Win go down for the Win+ hotkeys,
  and without Explorer nothing else reacts to Win alone. Ctrl+Esc also arrives as `SC_TASKLIST` on the taskman
  window; keyboard toggles within 300 ms of each other count once.
- `RegisterHotKey` (`ShellSession.RegisterHotkeys`), Win plus:
  - D show desktop (toggle), M minimize all, Shift+M restore them, Home all but the window in front (`ShowDesktop`).
  - T focus the taskbar's first button, Shift+T its last; B the notification area (the hidden icons' chevron, else
    Quick Settings' button). The taskbar is `WS_EX_NOACTIVATE`, which keeps it from ever becoming active (and so
    from getting the keyboard); these drop that style, activate the taskbar and focus the button; the style comes
    back when the taskbar loses activation.
  - 1…9 the Nth button of the primary taskbar: no window → launch, one window → as a click, several → the one after
    the foreground window, wrapping (`TaskActivation`, unit tested). Shift: a new instance; Ctrl+Shift: a new
    instance as administrator (not packaged apps: their elevation goes through Explorer); Ctrl: the app's window
    that was in front last (highest in the z-order), then on round its windows (`TaskActivation.LastActiveWindow`);
    Alt: its menu with the jump list, with the keyboard in it.
  - S and Q Start with search focus; E File Explorer (`shell:AppsFolder\Microsoft.Windows.Explorer`: a bare
    `explorer.exe` makes itself a second shell, taskbar and all, even with NeoShell registered); R the Run dialog
    above the Start button; I Settings and Pause System (as the shell their Control Panel applets, `control.exe` and
    `sysdm.cpl`).
  - Alt+D the notification center and calendar; Alt+K mutes the default microphone, or unmutes it (Explorer mutes
    calls in apps that support it; the endpoint's mute is the nearest without them). The microphone indicator
    shows it.
  - Shift+S the screen snip, PrtScn a screenshot of the whole screen, Z Snap layouts (see Screenshots, Snap
    layouts). A window opened from a hotkey is brought to the front with `SetForegroundWindow` after WinUI shows it:
    WinUI's `Activate` leaves the foreground with the app the keys went to.
- Quick Settings' shortcuts — Win+A (tiles), Win+Ctrl+V (Sound output), Win+K (Cast), Win+P (Project) — can't be
  registered: Windows' own Quick Settings host (ShellHost) keeps them after Explorer has gone, and would open its
  panel. The hook takes them instead (`PanelKeys`, unit tested): the letter is swallowed (down, repeats, up)
  and an unassigned key (vkE8, as AutoHotkey's menu mask key) is injected while Win is still down, so letting go of
  Win doesn't count as Win alone (which Windows sends to the shell as `SC_TASKLIST`, opening Start). Pressing the
  shortcut of the page shown closes Quick Settings; another page's switches to it. Win+N (the notification center
  and calendar, toggled) and Win+X (the Quick Link menu) are taken the same way. With Shift or Alt held the letters
  are left alone: Win+Alt+K is the microphone's.
- Win+Comma peeks at the desktop while Win is held (`PeekKeys`, unit tested, through the hook: a hotkey can't see
  Win let go of): Aero Peek at the taskbar, a window left out of peeking like the wallpaper, so only those show.
  The comma is swallowed and Win masked as for Quick Settings' keys.
- Start closed because another window took the foreground (deactivation) doesn't hand the foreground back to the
  previous app — that would take it from the window being activated, e.g. the taskbar for Win+T.
- Not done: Task View and virtual desktops (Win+Tab, Win+Ctrl+D/F4/arrows; out of scope, and the desktops live in
  Explorer), Widgets (Win+W), the Snipping Tool video (Win+Shift+R: Snipping Tool shows nothing without Explorer),
  and the clipboard history, emoji panel and voice typing (Win+V, Win+Period, Win+H), which Explorer passes to
  Windows' text input host through no public API.

### Taskbar context menu

Task Manager, the taskbar settings toggles (alignment, search button, combine, backdrop, auto-hide, all displays, tray mode),
Exit NeoShell (alongside Explorer only).

## System tray (`Tray/`)

- `TrayHost` (Interop) creates a hidden top-level window of class `Shell_TrayWnd` and a child `TrayNotifyWnd`,
  because `Shell_NotifyIcon` finds the tray by that class name. It is placed over the primary taskbar: apps read its
  rectangle to learn where the taskbar is.
- Handles `WM_COPYDATA`:
  - `dwData == 1`: `SHELLTRAYDATA` → `NIM_ADD`, `NIM_MODIFY`, `NIM_DELETE`, `NIM_SETFOCUS`, `NIM_SETVERSION`;
    replies 1 or 0, which the app gets back from `Shell_NotifyIcon`.
  - `dwData == 3`: `Shell_NotifyIconGetRect`, asked twice: `dwMessage` 1 → the top-left corner, 2 → the size, each
    as MAKELONG. An icon in the overflow answers with the chevron, as Explorer does.
  - `dwData == 0` (`SHAppBarMessage` from other app bars) is not served yet; it returns 0.
  - `NOTIFYICONDATA` parsing is done from a byte buffer and handles both 32- and 64-bit callers
    (HWND/HICON fields are 32-bit handles in both, sign-extended) and the V1/V2/V3/current sizes. Unit tested.
- Icons are keyed by (`hWnd`, `uID`) or `guidItem` (`TrayIconStore`, unit tested): adding an existing icon or
  changing a missing one fails, as in Explorer; only flagged fields change. `NIS_HIDDEN` is honoured. Icon pixels
  are copied when they arrive, as the app may destroy its HICON. Tooltips from `szTip` (version 4 icons without
  `NIF_SHOWTIP` get `NIN_POPUPOPEN`/`NIN_POPUPCLOSE` instead); balloon notifications are ignored (Explorer turns
  them into toasts; NeoShell doesn't yet).
- After `Shell_TrayWnd` exists, broadcast `RegisterWindowMessage("TaskbarCreated")` so running apps re-add icons.
- Remove icons whose owner window has died (`IsWindow` every 5 s and before forwarding input).
- **Mouse forwarding** with `NOTIFYICON_VERSION_4` semantics: `wParam` = anchor point (x, y), `lParam` low word =
  message (`WM_LBUTTONUP`, `NIN_SELECT`, `WM_CONTEXTMENU`, `NIN_POPUPOPEN`…), high word = icon ID; older versions
  get `wParam = uID`, `lParam = mouse message` (version 3 also gets `NIN_SELECT`/`WM_CONTEXTMENU`). Clicks call
  `AllowSetForegroundWindow` for the owner process first. A second press within the double-click time becomes
  `WM_LBUTTONDBLCLK`. Unit tested.
- **Display mode** (setting `TrayMode`, toggled from the taskbar menu): `ShowAll` (every icon in the taskbar) or
  `Overflow` (icons behind a chevron flyout). Primary taskbar only.
- Alongside Explorer: Explorer owns `Shell_TrayWnd`, so NeoShell shows no tray. Fully supported as the shell.

## Indicators (`Tray/`)

### Network

- `NetworkInformation.GetInternetConnectionProfile()` + `NetworkStatusChanged`.
- States: Ethernet, Wi-Fi (`WlanConnectionProfileDetails`, `GetSignalBars()` 0–5), cellular, no internet access,
  disconnected. Each maps to a Segoe Fluent Icons glyph; airplane mode shows the plane instead, even with a cable
  still connected, as Explorer does.
- Tooltip: network name and access status ("Airplane mode" while it's on). Right-click menu: Network and Internet
  settings (`ms-settings:network`; shell mode `ncpa.cpl`).

### Volume

- `IMMDeviceEnumerator` → default render endpoint → `IAudioEndpointVolume` with `IAudioEndpointVolumeCallback`
  (`AudioEndpoint`).
- `IMMNotificationClient` to follow default-device changes.
- Icon reflects mute and level (0 / low / medium / high glyphs). Mouse wheel over the button changes volume in 2%
  steps.
- Right-click menu on the icon, as Explorer's: Open volume mixer (`ms-settings:apps-volume`; shell mode the classic
  `sndvol.exe`) and Sound settings.

### Battery and energy saver

- `Battery.AggregateBattery` (WinRT) and its `ReportUpdated` (`BatteryMonitor`): the charge in tenths, with the
  plug while charging; tooltip "Battery: 54% remaining". Shown only on PCs with a battery.
- Energy saver's leaf shows after the volume while energy saver is on and there's no battery icon (a desktop).

### The button

- Network, volume and battery are one flat button with one hover plate, as on the Windows 11 taskbar; each icon is
  a cell with its own tooltip and right-click menu (the cell under the pointer decides; from the keyboard, the
  speaker's). Clicking opens Quick Settings at the screen's right edge; clicking again closes it.

### Microphone in use

- For each active capture endpoint: `IAudioSessionManager2` → `IAudioSessionNotification` for new sessions and
  `IAudioSessionEvents.OnStateChanged` per session (`CaptureMonitor`). The manager only reports new sessions after
  its session list has been asked for once.
- Visible while any capture session is `AudioSessionStateActive` (system sounds session excluded). Tooltip lists the
  apps (`IAudioSessionControl2.GetProcessId` → file description, else process name). Click opens
  `ms-settings:privacy-microphone` (shell mode: Sound's Recording tab, `mmsys.cpl,,1`).
- While the default microphone is muted (Win+Alt+K; an `AudioEndpoint` on the capture device follows it), the icon
  is the slashed microphone and the tooltip starts with "Microphone muted".

### Threads and placement

- Core Audio, `NetworkInformation`, the radios, energy saver and the battery call back on their own threads, and
  calling back into Core Audio from inside its callbacks can deadlock. So callbacks only mark state stale and raise
  `Changed`; `Indicators` marshals one update per burst to the UI thread (`DispatcherQueue.TryEnqueue`), which
  rebinds devices and reads fresh values.
- The callback objects are `[GeneratedComClass]` classes; the COM interfaces are `[GeneratedComInterface]`.
- The indicators sit on the primary taskbar, next to the tray, in both run modes (they don't depend on Explorer).
  Glyphs and tooltips come from `IndicatorDisplay` and `QuickSettingsDisplay` (unit tested).

## Notifications and calendar (`Notifications/`)

What the clock opens, laid out and measured as Explorer's (Windows 11 24H2/25H2): the notification center above the
calendar, both 336 epx wide, 12 epx in from the screen's right edge; the calendar 12 epx above the taskbar, the
notification center 12 epx above it and as tall as its notifications need, up to 8 epx from the screen's top.

- **Two windows** (`ClockFlyout`, two `PanelWindow`s): each panel has its own acrylic with the desktop between
  them, which one flyout (one popup window, one backdrop) can't do. They're frameless with Windows 11's rounded
  corners, topmost, out of Alt+Tab, take the accent colour as Start does, and slide in from the screen's right edge
  together (250 ms, decelerating) and out again (150 ms), as Explorer's. The calendar's window takes the foreground;
  the flyout closes once neither window has it, on Esc, on the clock again, or on a click elsewhere on the taskbar
  (which never takes the foreground). Their heights are measured from the content and again as it changes. Win+N
  toggles it on the primary taskbar (shell mode).
- **Notifications** (`NotificationCenter`, Interop `UserNotifications`): read with WinRT's
  `UserNotificationListener`, which an unpackaged app may use (access is the Privacy setting "Let apps access your
  notifications"), but its `NotificationChanged` event needs package identity, so they're read once a second; an
  unchanged set of IDs changes nothing. The notification platform keeps notifications whether or not a shell runs.
  The listener gives each one's app (AppUserModelID and name), time and texts â€” not images, buttons or launch
  arguments, so clicking a notification opens its app as Start would and removes it, rather than delivering the
  toast's own activation. App icons as the taskbar's (packaged logo, else the `shell:AppsFolder` item's icon).
  Per-app settings from `HKCU\...\Notifications\Settings\<AppUserModelID>` (`Enabled`, `ShowBanner`,
  `ShowInActionCenter`) and the global `PushNotifications\ToastEnabled` are honoured. "Turn off all notifications
  for <app>" writes `Enabled = 0` there, Settings' own store; the platform only notices it later (Settings tells it
  through a private channel), so NeoShell hides that app's notifications itself meanwhile.
- **Notification center** (`NotificationPanel`): "Notifications" with Do not disturb and Clear all; groups by app,
  the app with the newest notification first (`NotificationDisplay.Group`, unit tested). A group shows its newest
  notification with "+N notifications"; expanded, all of them and "See fewer". Cards show the time (with the date
  for older days), the title (2 lines) and the body (1 line, with Explorer's chevron to show all of a trimmed one),
  and "â€¦" (turn off the app, notification settings) and Clear under the pointer; the group header has the same.
  "No new notifications" when there are none.
- **Do not disturb** (Interop `DoNotDisturb`): no public API; it's the notification platform's quiet hours profile,
  switched through the undocumented `IQuietHoursSettings` (CLSID `f53321fa-â€¦`), as Explorer's bell button does:
  `Microsoft.QuietHoursProfile.PriorityOnly` is on, `â€¦Unrestricted` off. Read with the notifications, so a change
  made elsewhere shows within a second; the clock shows the bell while it's on, and no toasts pop up.
- **Calendar** (`CalendarPanel`): today's long date without the year (`NotificationDisplay.DayHeading`, unit tested
  for several cultures) and a button folding the month away (remembered in settings); a `CalendarView` restyled as
  Explorer's (no borders or backgrounds, other months' days dimmed, today in the accent circle), starting the week
  on Windows' regional first day rather than the display language's; the footer with the focus length (âˆ’/+: 5
  minutes at a time to 30, then 15, between 5 and 240; remembered) and Focus.
- **Focus** (`FocusSession`): Windows' own focus sessions (`Windows.UI.Shell.FocusSessionManager`) are a limited
  access feature only Microsoft's apps can unlock ("Access is denied"), so NeoShell runs its own: Do not disturb for
  the chosen time with a countdown and Stop focus in the footer, then Do not disturb as it was before (also on exit).
  Unlike Windows', it doesn't hide taskbar badges or flashing, and there's no chime at the end.
- **Toasts** (`ToastPopups`, shell mode only): without Explorer no toasts show at all â€” they belong to the
  ShellExperienceHost that Explorer runs â€” though the notifications are still stored. Each notification that
  arrives while NeoShell runs (not those already there at start, not while Do not disturb is on or the flyout is
  open) shows as Explorer's: its own acrylic window, 364 epx wide, 16 epx from the screen's right edge and 12 above
  the taskbar, with the app's icon and name, "â€¦" and close, the title and up to three lines of body. It slides in
  from the edge; the newest is lowest and older ones move up, three at most. It leaves after the system's "Dismiss
  notifications after" time (`SPI_GETMESSAGEDURATION`, 5 s by default), later while the pointer is on it. Closing
  it only puts it away (it stays in the notification center, as in Explorer); clicking it opens the app. Alongside
  Explorer, Explorer shows toasts.

## Quick Settings (`QuickSettings/`)

What Windows 11 opens from the network and volume icons (what earlier Windows called the action center).
`QuickSettingsPanel` is the content of the primary taskbar's flyout, 360 effective pixels wide, laid out as
Windows': tiles, the volume slider, and a footer with the battery (when there is one) and All settings
(`ms-settings:`; shell mode Control Panel). Its state comes from `Indicators` and is only read while it's open.
Like Windows', it takes the taskbar's colour: the flyout's popup window gets its own acrylic `ShellBackdrop` (the
presenter is transparent), tinted with the accent colour and with the theme readable on it when "Show accent color
on Start and taskbar" is on, and plain theme acrylic otherwise; it follows theme changes with the taskbar.

### Tiles

- Two rows of three per page (`QuickSettingsDisplay`, unit tested); more pages are turned with the mouse wheel or the
  arrows beside the page dots, sliding up or down. A tile is a toggle button, accent-filled while its feature is on,
  with its name below: a switch, a page (glyph and chevron), or both split in two halves (left switches, right opens
  the page). Tiles without hardware or support aren't shown, as in Windows.
- Windows' default order, then the owner's: Wi-Fi, Bluetooth, Airplane mode, Accessibility, Energy saver, Live
  captions, Night light, Nearby sharing, Cast, Project.
- **Wi-Fi** and **Bluetooth** (shown when the PC has the radio): the switch turns the radio on or off
  (`RadioSwitches`, WinRT `Windows.Devices.Radios`; `Radio.RequestAccessAsync` once). The Wi-Fi tile shows the
  connected network's name. Radios are looked for again each time Quick Settings opens (adapters come and go).
- **Airplane mode** (shown when the radio management service answers): every radio off at once through the Radio
  Management API (`AirplaneMode`): `IRadioManager` (CLSID `581333F6-…`, RmSvc), undocumented but unchanged since
  Windows 8 and what airplane-mode tools use; there's no public API. No change notification: it's read again
  whenever a radio changes or Quick Settings opens.
- **Energy saver** (Windows 11 24H2 and later): state from the documented `GUID_ENERGY_SAVER_STATUS` power setting
  notification (`PowerSettingRegisterNotification`; the first call comes straight away). Setting it has no public
  API: Windows' own setting (`SettingsHandlers_OneCore_BatterySaver.dll`) publishes the WNF state
  `0x41C6013DA3BC3075` with 1 (on) or 2 (off), which is what `EnergySaver.Set` does (`RtlPublishWnfStateData`).
- **Live captions**: on while `LiveCaptions.exe` runs; see Accessibility below.
- **Night light** and **Nearby sharing** open their Settings pages (`ms-settings:nightlight`,
  `ms-settings:crossdevice`), and are hidden in shell mode, where Settings can't open. Their state lives in private
  stores: night light in Windows' cloud data store (on Windows 11 25H2 the old CloudStore registry blob is neither
  written nor read any more), nearby sharing in the Connected Devices Platform service (writing its registry values
  changes nothing).

### Pages

Each slides in from the right (back slides the tiles in from the left) and has a header with a back button and, as
Windows', its shortcut as key caps (`ShortcutKeys`), and a footer with a link to Settings.

- **Wi-Fi**: a switch for the radio, a refresh button, and the networks in range (`WifiNetworks`, WinRT
  `WiFiAdapter`): one entry per name at its strongest, the connected one first, then by signal (unit tested), each
  with its signal and a lock when secured, and "Connected, secured" / "Secured" / "Open". Choosing one opens it up:
  Connect automatically and Connect, or Disconnect for the connected one. Connecting uses the saved profile; when
  Windows has no key (or a wrong one) a password box and Next appear. Scanned when the page opens and on refresh.
  Windows only gives network names to apps allowed to use the location. More Wi-Fi settings
  (`ms-settings:network-wifi`; shell mode `ncpa.cpl`).
- **Bluetooth**: a switch for the radio and the paired devices, classic and LE (`BluetoothDevices`,
  `DeviceInformation` with `GetDeviceSelectorFromPairingState(true)`), with a glyph by kind (class of device or LE
  appearance) and Connected or Paired. There's no public API to connect or disconnect a paired device, so they're
  only listed. More Bluetooth settings (`ms-settings:bluetooth`; shell mode Devices and Printers).
- **Accessibility**, by need as Windows': Vision (Magnifier, Narrator, Colour filters), Hearing (Live captions, Mono
  audio), Motor and Mobility (Voice access, Sticky keys). Each row: glyph, name, description, its state in words and
  a switch. Magnifier, Narrator, Live captions and Voice access are apps of their own: on while their executable runs
  in this session, started from System32, and closed with their window's `SC_CLOSE` (Magnifier ignores `WM_CLOSE`),
  or ended when they have no window (`AssistiveTools`). Sticky keys is `SPI_SETSTICKYKEYS` (saved to the profile and
  announced). Colour filters and Mono audio can't be switched from outside Settings (colour filters are applied by
  the AT broker through `user32!SetDesktopColorTransform`; `atbroker /start colorfiltering` turns them on but nothing
  turns them off again), so their rows link to their Settings pages (shell mode: `access.cpl`). None of these states
  is reported, so they're read when the page or the tiles open. More Accessibility settings.
- **Cast** (Win+K): without Wi-Fi there's no Miracast, and the page says so as Windows does ("Connect a cable to
  cast"); with Wi-Fi it offers Settings' wireless display search, since connecting to one has no public API. More
  cast settings opens Display settings (shell mode the adapter's classic properties).
- **Project** (Win+P): PC screen only, Duplicate, Extend, Second screen only, the current one selected
  (`QueryDisplayConfig` with `QDC_DATABASE_CURRENT`); choosing one is `SetDisplayConfig(SDC_APPLY | SDC_TOPOLOGY_…)`
  (`DisplayProjection`). More Display settings.
- **Sound output** (Win+Ctrl+V), from the button beside the volume slider (speaker with sliders, chevron). Moving a
  slider up unmutes, as Windows' own sliders do. Letting go of the slider (or each keyboard step) plays the default
  beep as a system sound (`PlaySound` with `SND_SYSTEM`, so it goes to the System Sounds session), as Windows does to
  let the new volume be heard. The page: Output device, Spatial sound, Volume mixer with a settings button, and More
  volume settings (`ms-settings:sound`; shell mode `mmsys.cpl`). Read only while it's shown.
  - **Output device**: the enabled outputs (`AudioDevices`, the default selected); choosing one makes it the default
    for all three roles (console, multimedia, communications), as the Sound control panel does. There's no public
    API for that: it's the undocumented `IPolicyConfig::SetDefaultEndpoint` (`PolicyConfigClient`), unchanged since
    Windows 7 and what volume tools use. The volume then follows the new device.
  - **Spatial sound** (`SpatialSound`, WinRT `SpatialAudioDeviceConfiguration` for the default output): Off (the
    empty GUID) and Windows Sonic for Headphones when supported, set with `SetDefaultSpatialAudioFormatAsync`. Dolby
    Atmos and DTS report themselves supported but need their apps and licences, which Windows' page checks; they
    aren't offered. Hidden when the output has no spatial sound.
  - **Volume mixer** (`AudioMixer`): the default output's audio sessions that haven't expired
    (`IAudioSessionManager2`), one row per app as Windows' mixer groups them (packaged app, else executable; the
    system sounds, whose session answers `GetProcessId` with the success code `AUDCLNT_S_NO_SINGLE_PROCESS`), each an
    icon that mutes it and a slider (`ISimpleAudioVolume` on all its sessions; the slider shows the loudest); the
    name is the icon's tooltip. Names: the session's display name (resource references resolved), else the packaged
    app's or the executable's description. New sessions, state changes and volume changes made elsewhere
    (`IAudioSessionEvents`) refresh the rows in place; NeoShell's own changes carry an event context GUID and aren't
    reported back.

## Start menu (`StartMenu/`)

- One topmost popup window (`StartMenuWindow`), created at startup and shown above the taskbar that opened it:
  centred on the monitor, or at its left when taskbar items are left-aligned. Acrylic, rounded corners, activatable
  (unlike the taskbar) for the search box; hides on deactivation, Esc, a click elsewhere on the taskbar, or Win.
- Activation: `Window.Activate` alone doesn't take the foreground from the app the user was in, so Start calls
  `SetForegroundWindow` (allowed: the click or key was the last input). On closing it hands the foreground back to
  that app; otherwise Windows picks the next window in z-order, which can be Explorer's invisible Start/search host.
- Flies out of the taskbar (250 ms, decelerating) and back into it on closing (150 ms, accelerating), as in Windows 11.
  The window itself moves, frame by frame (`WindowSlide`, on `CompositionTarget.Rendering`): the acrylic belongs to the window, so
  sliding the content would leave an empty acrylic panel standing still. It sits just below the taskbar that opened it
  in the topmost band (`PinnedWindow.SetLayer(Topmost, above)`), and is cut off at the taskbar's edge
  (`PinnedWindow.VisibleBottom`, a window region) so it rises from behind the taskbar whatever is in front of the
  screen's bottom and however see-through the taskbar is; the thumbnails do the same. It's shown once, cut off
  entirely, at startup, so the first opening doesn't slide up a black window before WinUI's first frame. What it shows is
  reset (search, All apps, scroll) once it's out of sight; opened again while closing, it turns back from where it is.
- Toggling: pressing the Start button deactivates Start before the button's click arrives, so a click within
  400 ms of a deactivation doesn't reopen it.
- Resizable by dragging a top corner (`ResizeGrip`, with the resize pointer): the bottom stays above the taskbar, a
  centred Start grows on both sides, a left-aligned one only has the top-right grip. Live while dragging; the size
  (as the monitor allowed it) is saved on release. 832 by 860 epx by default, at least 480 by 400, at most the
  monitor above the taskbar (`StartMenuLayout`, tests).
- Home is one scrolling page, as in Windows 11, below the search box:
  - **Pinned** grid (persisted in settings). Drag to reorder by hand (`GridReorder`, unit tested): the icon follows
    the pointer and the icons between its old and new place shift one slot. Pinned and recent icons shrink while
    pressed and a dragged one grows, as on the taskbar. The grid's own drag and drop keeps the
    dropped icon hidden until the drag operation winds down, and its item transitions animate the move as a removal
    and an addition, so both are off. Pinned and Recent are updated in place when Start opens, not refilled (which
    would replay every icon's entrance). The first time the catalog loads, Explorer's Start pins
    are added once (`ExplorerStartPinsImported`). Start keeps them encrypted in `start2.bin`, so they're read through
    `StartTileData.dll`'s `IStartLayoutCmdlet::ExportStartLayout` (the object behind `Export-StartLayout`), which
    writes `{"pinnedList":[{"packagedAppId":…},{"desktopAppLink":"%APPDATA%\…\x.lnk"}]}`. A shortcut is matched to
    the catalog by its `System.AppUserModel.ID`, else its `System.Link.TargetParsingPath`.
  - **Recent**: the six catalog apps started most recently, three columns, with "30m ago" / "5h ago" / the date.
    Read from UserAssist (`HKCU\…\Explorer\UserAssist\{CEBFF5CD-…}\Count`: ROT13 value names, AUMIDs or
    known-folder paths, last run as a FILETIME at offset 60) each time Start opens. `Launcher` starts non-packaged
    apps with `ShellExecuteEx` + `SEE_MASK_FLAG_LOG_USAGE` so NeoShell's launches are recorded too; packaged apps
    started through the activation manager are not.
  - An **All apps** button opens the alphabetical list with letter headers ("#" first) in place of the page, with a
    Back button (unlike Windows 11, which puts All on the same page).
  - Context menu on every app: Open, Pin to/Unpin from Start, Pin to/Unpin from taskbar.
- **App catalog** (`AppCatalog`): enumerate `shell:AppsFolder` (`SHCreateItemFromParsingName` → `BHID_EnumItems`),
  reading display name and parent-relative parsing name — an AUMID, or a path such as `{KnownFolder}\app.exe` that
  is resolved with `SHGetKnownFolderPath` — and, for shortcuts, `System.Link.TargetParsingPath`. Shortcuts may give
  an app an AUMID its windows don't carry, so the pinned app keeps the target path to match those windows. Icons
  load only for items being shown. Reloaded in the background when Start opens if older than two minutes.
- **Search** — typing anywhere in the open menu focuses the search box:
  - Apps: ranked exact > prefix > every-word-starts-a-word > contains (case-insensitive, unit tested). Shown first,
    instantly.
  - Indexer: `CSearchManager` → catalog `SystemIndex` → `ISearchQueryHelper` (`QuerySelectColumns`,
    `QueryContentProperties` = `System.ItemNameDisplay` so words match names like the Windows search box,
    `QueryWhereRestrictions` = files only and no `.lnk`, prefix term expansion, `QueryMaxResults`),
    `GenerateSQLFromUserQuery`, executed with `OleDbConnection`
    (`Provider=Search.CollatorDSO;Extended Properties='Application=Windows'`). OLE DB needs built-in COM interop,
    which the app has (a file-based or AOT build would not).
    Columns: `System.ItemNameDisplay`, `System.ItemPathDisplay`, `System.Kind`. Results grouped as Documents,
    Folders, Other (Settings pages aren't in `SystemIndex`).
  - Runs on a background thread, 150 ms debounce, cancelled by the next keystroke; index failures leave app results.
  - Enter opens the selected result (the best match is selected); arrow keys move the selection.
- **Bottom row**:
  - User name (`GetUserNameEx(NameDisplay)`, else the account name) and picture
    (`HKLM\...\AccountPicture\Users\<SID>\Image96`, else initials).
  - Settings button → `ms-settings:`; in shell mode Control Panel (`control.exe`), as Settings is a UWP app.
  - Switch to Explorer button (see lifecycle), shell mode only, behind a confirmation dialog that defaults to Cancel.
  - Power button menu:
    - Lock → `LockWorkStation`
    - Sign out → `ExitWindowsEx(EWX_LOGOFF)`
    - Sleep → `SetSuspendState(false, false, false)`
    - Restart / Shut down → enable `SeShutdownPrivilege`, `InitiateShutdown` with `SHUTDOWN_RESTART` /
      `SHUTDOWN_POWEROFF` and a planned reason code.

## Window switcher (`Switcher/`)

- As the shell only: alongside, Explorer's Alt+Tab runs; without Explorer Windows would cycle windows with no UI.
- Keys from the keyboard hook (`AltTabKeys`, unit tested): Alt+Tab opens it, Tab/Shift+Tab and the arrow keys move,
  letting go of Alt or Enter switches, Esc cancels, Delete closes the chosen window. Its keys are swallowed (down,
  repeats and up); Alt always passes, masked with vkE8 so the app in front doesn't open its menu bar.
- `WindowSwitcher`: the taskbar's windows of other processes, the one in front first and then by z-order (most
  recently used, `AltTabLayout.Order`), the previous one chosen (Shift: the last). Shown after 100 ms, so a quick
  Alt+Tab switches without the panel flashing up; moving on shows it at once.
- Explorer's look: an acrylic panel centred on the monitor of the window in front; cards in centred rows
  (`AltTabLayout.Arrange`), each the window's colours with icon and title over a live DWM thumbnail 132 epx high
  (a minimized window shows its icon), the chosen one ringed in the accent colour 3 epx off the card; the close button
  shows over a card. More windows than fit make the previews smaller (Explorer's panel scrolls instead).
- Switching: `SetForegroundWindow` after a key of NeoShell's own (vkE8), which lifts the foreground lock (the keys
  went to the app in front). `SwitchToThisWindow` sends the window left behind to the bottom of the stack, which
  breaks the most recently used order.

## Snap layouts (`Snap/`)

- Win+Z: `SnapLayoutsWindow` for the window in front, if it's another process's resizable window, at its top right
  below the title bar (where Explorer shows them under the maximize button), kept on the monitor. Acrylic, rounded,
  topmost; closes on Esc or when it loses activation.
- The layouts (`SnapLayouts`, unit tested) are Windows 11's: halves, two thirds and a third, a half and two
  quarters, four quarters, and with at least 1920 effective pixels of work area also thirds and a wide middle;
  stacked rows on a portrait screen. Previews in the work area's shape, numbered.
- A click on a zone (the zone under the pointer or keyboard takes the accent colour), Enter on a focused one (the
  first has the keyboard; arrows move), or a layout's number and then the zone's (shown once the layout is picked)
  places the window: restored if maximized, then sized so its visible frame (`DWMWA_EXTENDED_FRAME_BOUNDS`) fills
  the zone of the work area exactly, its invisible resize borders outside it (`TopLevelWindows.Place`). Zone edges
  are rounded on their own, so neighbours share them.

### Window snapping (`WindowSnapping`, shell mode)

Windows leaves snapping to Explorer: without it, a window dragged against an edge just moves, and Win+arrows do
nothing (they stay registered as hotkeys, so the keyboard hook takes them: `SnapKeys`, tested).

- **Dragging.** `EVENT_SYSTEM_MOVESIZESTART`/`END` (WinEvents) bracket the app's own move loop; in between the pointer
  is polled every 30 ms. A window whose size changes is being resized, not moved, and doesn't snap. The zone under
  the pointer (`WindowSnap.AtPointer`, tested): against the left or right edge a half, within an eighth of the work
  area's shorter side from a corner a quarter, against the top edge maximized (a quarter near its corners). The edges
  are the work area's, not the screen's: during a move Windows keeps the pointer inside the work area, so it stops at
  the taskbar's and the widget sidebar's edges. While the pointer is in a zone, `SnapPreview` shows it: an acrylic,
  rounded outline 8 epx inside the zone, just behind the dragged window (`PinnedLayer.Normal` below it). Let go, the
  window fills the zone (`TopLevelWindows.Place`, as Win+Z) or is maximized.
- **Its own size back.** The bounds a window had before it was snapped are kept; dragged out of its zone (not just
  clicked on its title bar), it gets that size back under the pointer, at the same share of its width as where it was
  grabbed (`WindowSnap.Unsnapped`, tested). A maximized window dragged is restored by Windows itself first. A snapped
  window stays snapped underneath when maximized, as in Windows; sized by hand, it isn't snapped any more.
- **Win+arrows** (`WindowSnap.AfterKey`, tested), Windows 11's moves: Left/Right snap to that half, cross over to the
  other quarter, and from the other half give the window its own bounds back; Up goes from a half to its top quarter
  and from a bottom quarter to the half, and maximizes a window that isn't snapped; Down undoes those, restores a
  maximized window and minimizes what's left. Once per press.
- Not done: Snap Assist (offering the other windows for the rest of the screen), snap groups, the layouts flyout at
  the top edge, Win+Shift+arrows and moving across monitors with Win+Left/Right.

## Screenshots (`Capture/`)

- Pictures are copied from the screen DC (`BitBlt` with `CAPTUREBLT`, opaque), put on the clipboard as a bottom-up
  `CF_DIB` and saved as PNG (`BitmapEncoder`) in Pictures\Screenshots (`FOLDERID_Screenshots`, created if missing)
  as Windows names them: "Screenshot 2026-10-05 183207.png", " (2)" on for more in the same second
  (`Screenshots`, unit tested).
- Win+PrtScn: the whole virtual screen.
- Win+Shift+S: Snipping Tool (`ms-screenclip:`) starts without Explorer but shows nothing and quits, so NeoShell snips
  itself (`ScreenSnip`): each monitor's picture is taken first, then shown frozen in a topmost window per monitor,
  dimmed, with a crosshair. Dragging out a rectangle shows it at full brightness, outlined; letting go keeps that
  part of the picture, as above. A click without a drag snips nothing; Esc or a right-click cancels. Rectangle
  snips only (no freeform, window or full-screen modes, no toolbar).

## Widgets (`Widgets/`)

Small widgets about the computer and its user, as Windows Vista's sidebar gadgets, with the taskbar's look.

### Sidebar and floating widgets

- `SidebarWindow`: a strip along the right of the primary monitor, from the top to the taskbar (the work area's
  height), 320 epx wide by default (`WidgetSidebarWidth`, 240 to 560); dragging its left edge (`EdgeGrip`) resizes
  it. It reserves its space so maximized windows stop at its edge: alongside Explorer as an app bar on the right
  (`AppBar.DockRight`, left of other app bars there); as the shell through `ShellWorkArea` (see the taskbar's
  Window), which keeps the taskbar's bottom and the sidebar's right reservation per monitor so neither undoes the
  other's; the sidebar's height comes from what's reserved there, not from Windows' work area, which follows a moment
  later. While the edge is
  dragged only the window moves; the space is reserved again when it's let go.
- No header text: only an add button at the top right, invisible until the pointer is over it (or its menu is
  open), whose menu lists every kind; Profile, Resource usage, Now playing and
  Weather show once only (disabled in the menu while shown), Pictures and Notes as often as wanted. Widgets are cards
  in a scrolling column.
- Hidden or shown from the taskbar's menu ("Show widgets", `ShowWidgetSidebar`); floating widgets stay.
- Right-clicking the sidebar or a widget in it opens its menu: "Show panel background" (`ShowWidgetPanel`) and
  "Add widget". Without the panel the header goes and the window is cut to its cards (`WindowRegion.SetRoundedRects`,
  kept up with layout and scrolling): each widget keeps the backdrop behind it, as floating ones do, and the rest of
  the strip shows the desktop and lets clicks through. The space stays reserved.
- `FloatingWidgetWindow`: a widget dragged out of the sidebar, a rounded window of its own, 300 epx wide and as tall
  as the widget (it follows the widget's height and the monitor's scale). The widget sits top-aligned in a
  non-scrolling `ScrollViewer`, so it takes its natural height; the window is resized after the layout pass, through
  `AppWindow.Resize` as well (WinUI's window otherwise keeps the size it last knew of, and a graph opened in a
  floating resource widget was cut off). Its position is saved in screen pixels;
  one left on a monitor that's gone comes back at the top right of the primary one (`SidebarLayout.KeepOnScreen`).
- Both are just above the desktop and below every app's window, even when clicked (`PinnedLayer.Desktop`: just below
  the lowest window that isn't the desktop, hidden, minimized, cloaked, topmost or another of NeoShell's desktop-level
  windows; a minimized window sits at the very bottom, below the wallpaper, so going below it hid the widget), stay
  while peeking at the desktop (Win+Comma), are left alone by Show desktop and Win+M, and are out of Alt+Tab. They
  take the focus when clicked: notes are typed into.
- Backdrop, theme and accent colour are the taskbar's (`TaskbarBackdrop`, `Taskbars.Updated`), on the sidebar and on
  each floating widget.
- Closing a widget window (`Shut`): its subclasses are removed and moves and resizes no longer reach WinUI
  (`WindowClosing.IgnoreMoves`). While closing, WinUI destroys the window's content and then re-applies the window's
  styles; when that moves the client area, WinUI's move handler repositions the destroyed content (an access violation
  in `CWindowChrome::UpdateBridgeWindowSizePosition`, found from a crash dump: NeoShell crashed on exit now and then,
  as the shell, after a widget had been added).

### Each widget (`WidgetFrame`, `WidgetView`)

- `WidgetFrame` draws the card (none when floating: the window is the card) and, while the pointer is over it, a
  settings button (a flyout with the widget's own settings) and a close button, on a solid plate at the top right.
- Pressing anywhere the widget's own controls don't take and moving 4 epx drags it. A widget dragged from the sidebar
  is lifted out of the column (kept in the tree without height, so it keeps the pointer; each card keeps its gap below
  itself rather than the panel's spacing, so a lifted one leaves none). While a widget is over the sidebar, the others
  make room for it (moves of a desktop-level window leave its z-order alone, so dragging stays smooth): a
  card-shaped gap opens where it would go (before the first card whose middle is below the
  pointer, measured as if the gap weren't there), and the cards slide (`RepositionThemeTransition`). Off the sidebar
  the widget follows the pointer in its own window, held where it was grabbed, and stays where it's let go. Let go
  over the sidebar it takes the gap's place; a floating widget first slides into the gap (150 ms).
- A floating note can be resized by its bottom-right corner (`WidgetView.CanResize`): its width (200 to 640 epx) and
  the height of its text (60 to 900 epx), saved as `FloatingWidth` and `ContentHeight`; the text keeps its height when
  the note is docked.
- `ShellSettings.Widgets` keeps every widget, docked and floating; the docked ones in the sidebar's order. Each has an
  id, its kind, a position while floating and its kind's options (`WidgetSettings`; unset options take defaults and
  aren't written). A widget's view is made anew when it moves between the sidebar and the desktop, so what it keeps
  lives in its settings, a file or a shared service. The default widgets have fixed ids.

### The widgets

- **Profile**: the account picture (`AccountPicture\Users\<SID>`, as Start), the user's display name, the time
  (optionally with seconds) and the date.
- **Resource usage**: CPU (Processor Utility, as Task Manager), GPU (the busiest engine, summed over processes, as
  Task Manager; left out without GPU counters), memory used of total, disk activity (100 - idle time) with the free
  space of the fixed drives, network down and up (bits a second) over the adapters that are up and carry IP (each
  adapter's filter drivers are listed as adapters too, with the same counts). Its settings show each drive and each
  adapter as a row of its own. Sampled once a second by `ResourceMonitor` (PDH through `SystemUsage`, wildcard
  counters for GPU engines and logical disks, `GlobalMemoryStatusEx`, `DriveInfo`, `NetworkInterface` statistics;
  read on the thread pool, about 10 ms a time) only while the widget is shown; it keeps the last minute of each, so moving the widget keeps its graphs. Each row
  expands to its graph (networks scaled to the minute's peak). A bar shows use and goes while the graph is open; a
  disk's bar shows its space used, as Explorer's, and stays. Colours per kind (drives take the disk's, adapters the
  network's) from Windows' accent palette.
- **Pictures**: a slideshow of the user's Pictures folder and the folders in it (up to 2,000 jpg/png/bmp/gif/webp,
  hidden and system files skipped), in random order, every 10 s by default; another folder (Windows App SDK's
  `FolderPicker`) and interval in its settings. The picture fills the whole card, without padding or border
  (`WidgetView.FillsCard`), and is decoded at the size shown. Double-click opens the one shown.
- **Now playing**: the current media session (`GlobalSystemMediaTransportControlsSessionManager`, as Windows' media
  flyout): title, artist, art (optional), previous, play/pause and next.
- **Weather**: MET Norway's Locationforecast 2.0 (compact): the hour under way (symbol, temperature, words, wind) and
  the next five hours, in degrees Celsius and m/s. The place is the computer's (Windows' location service, asked once
  a session from the thread pool: from the UI thread Windows would prompt to turn location on, and a shell shouldn't
  greet the user with that) or a latitude and longitude typed into its settings.
  As MET's terms ask: a User-Agent naming NeoShell and its repository, coordinates rounded to four decimals, a
  forecast reused until it expires (`Expires`) and then asked for with `If-Modified-Since`, and "Data from MET
  Norway" credited in its settings (kept off the widget to keep it small). Refreshed every 30 minutes, five after a
  failure.
- **Wireless devices**: the batteries of the same devices the WirelessStatus app lists, its code brought over
  into `NeoShell.Interop/Wireless`: Bluetooth devices Windows knows the level of (`DEVPKEY_Bluetooth_Battery` on the
  device nodes, connected per the paired association endpoints, linked by container id), and over HID the 2.4 GHz
  devices Windows doesn't know (Razer mice through Razer's control report, the Audeze Maxwell through its dongle's
  reports; protocols as documented by OpenRazer and HeadsetControl, reimplemented). Each kind is read on the thread
  pool with a 15 s limit, keeping its last devices when it fails. Read by `WirelessMonitor` while the widget is shown:
  at once, every 5 minutes (its setting), and 2 s after the last of a burst of `WM_DEVICECHANGE` (a receiver plugged
  in or out, from the taskbars' windows). A row per device: its kind's icon, name, battery bar (red at 10% or less
  while not charging), a charging mark and the level, or dimmed and "Unavailable" when out of reach.
- **Notes**: plain text, straight on the card (no box or underline, focused or not), saved half a second after typing stops, to `notes\<id>.txt` next to the settings; text size
  in its settings. Closing a note keeps its file.

## Settings (`Settings/`)

A single `record Settings` serialized with `System.Text.Json` (source-generated context), loaded at startup,
saved on change. Unknown/missing values fall back to defaults; a corrupt file is renamed to `.bak` and defaults used.

```
TaskbarAlignment     Center | Left
CombineButtons       Always | WhenFull | Never
AutoHide             bool
ShowOnAllDisplays    bool
ShowSearchButton     bool
TrayMode             ShowAll | Overflow
TaskbarBackdrop      Acrylic | Mica | Translucent | Transparent
PinnedTaskbarApps    list
PinnedStartApps      list
ExplorerStartPinsImported  bool
ExplorerTaskbarPinsImported  bool
StartMenuWidth       double (epx)
StartMenuHeight      double (epx)
DesktopSortOrder     Name | Size | ItemType | DateModified
ShowWidgetSidebar    bool
WidgetSidebarWidth   double (epx)
Widgets              list (id, kind, X/Y and size while floating, the kind's options)
```

## Testing strategy

- **xunit** tests in `tests/NeoShell.Tests` for pure logic: taskbar window filter, grouping keys, NOTIFYICONDATA
  parsing (32/64-bit), app search ranking, indexer query building, startup entry parsing and `StartupApproved`,
  settings round-trip and corrupt-file handling, wallpaper style mapping, AppBar rect calculation, which desktop
  items get icons and in which order, Quick Settings' paging, Wi-Fi network list and shortcut keys, the keys the
  hook takes (Start, panels, Alt+Tab, Win+Comma), Win+number's window choice, Snap layouts and screenshot names,
  the shell's work area, the sidebar's and floating widgets' placement and order, the widgets' number and colour
  formats, and MET's forecast parsing and symbols.
- Logic that touches Windows is split so the decision is a pure function over a snapshot (e.g. `WindowInfo`) that
  tests can construct.
- **Live UI checks** through UI Automation (`AutomationId`s on all interactive controls), never global keystrokes.
- **Shell mode** only on a test account or VM, via `tools/set-shell.ps1` (every sign-in) or `tools/start-shell.ps1`
  (this session: "Exit Explorer"'s message to `Shell_TrayWnd`, NeoShell started, then what's left of Explorer's
  process ended, since a later Explorer hangs on it).
