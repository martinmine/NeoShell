# NeoShell design

This document describes what NeoShell does and how each feature is built. The milestones are in [plan.md](plan.md);
coding rules are in [CLAUDE.md](../CLAUDE.md).

## Scope

- Display the wallpaper and the desktop icons, with Explorer's context menus for icons and the desktop.
- A WinUI taskbar with feature parity with the Windows 11 taskbar (exceptions in the system tray area).
- System tray icons, either all shown or hidden behind an overflow flyout.
- Network, volume and microphone-in-use indicators.
- A Start menu with search (apps + Windows Search Indexer), Settings, power options (Lock, Sign out, Sleep,
  Restart, Shut down) and a button to switch back to `explorer.exe`.

Out of scope: Quick Settings, Action Center/toasts, Widgets, Task View, Win+X, pinning items in jump lists.

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
  value. Icons fill columns from the top left of the primary monitor's work area; there is no free positioning.
- **View settings** live where Explorer keeps them, so they carry over when switching shells: the icon size in
  `HKCU\Software\Microsoft\Windows\Shell\Bags\1\Desktop\IconSize` (32/48/96), "Show desktop icons" in
  `Explorer\Advanced\HideIcons`.
- **Images** come from `IShellItemImageFactory` without `SIIGBF_ICONONLY`, so pictures get thumbnails, loaded off the
  UI thread at physical pixel size. Shortcuts get the stock link overlay (`SHGetStockIconInfo(SIID_LINK)`) in the
  corner, at most medium-icon size. Labels are white over a dark copy offset by a pixel, readable on any wallpaper.
- **Updates.** `FileSystemWatcher`s on both Desktop folders and on each fixed drive's `$Recycle.Bin\<SID>` (the
  Recycle Bin icon shows whether it's empty), and every `WM_SETTINGCHANGE` (folder options, Desktop icon settings,
  the work area), queue a debounced refresh. A refresh enumerates off the UI thread and updates the
  `ObservableCollection` in place (remove, move, insert), so the selection and loaded images survive.
- **View** (`DesktopIconsView`): a `GridView` (extended selection, vertical `ItemsWrapGrid`) in the primary monitor's
  `WallpaperWindow`. Double-click or Enter opens; Delete, F2, F5, Ctrl+C/X/V and Alt+Enter work as in Explorer; a
  click on the empty desktop clears the selection. Dragging from the empty desktop draws a selection rectangle (accent
  coloured) and selects every icon it touches; with Ctrl held it adds to the selection.
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

## Taskbar (`Taskbar/`)

### Window

- One `TaskbarWindow` per monitor (setting: show on all displays), bottom edge, 48 effective pixels high, kept in
  place and topmost by `PinnedWindow`, frameless (`FramelessWindow`), `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
- Screen space:
  - Alongside Explorer: registered as an AppBar with `SHAppBarMessage` (`ABM_NEW`, `ABM_QUERYPOS`, `ABM_SETPOS`,
    `ABM_REMOVE`); Explorer places it above its own taskbar and sends `ABN_POSCHANGED` when it must move.
  - As the shell: `SHAppBarMessage` is served by Explorer's `Shell_TrayWnd`, so it doesn't work. NeoShell sets the
    monitor's work area itself (`SPI_SETWORKAREA`) and restores it on exit. Serving other apps' AppBar messages
    belongs with `Shell_TrayWnd` (Tray).
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
  window's alpha shows the desktop instead of black. Mica falls back to Acrylic where unsupported. Start and the
  thumbnails stay Acrylic.
- Light/dark following the system theme (`HKCU\...\Themes\Personalize\SystemUsesLightTheme`), re-read on every
  `WM_SETTINGCHANGE`.
- "Show accent color on Start and taskbar" (`Personalize\ColorPrevalence`): the acrylic of the taskbar and Start is
  tinted with the second darker shade of `HKCU\...\Explorer\Accent\AccentPalette`, as Explorer does, and their
  text is light or dark by that colour's brightness. Re-read on `WM_SETTINGCHANGE` with the theme.
- Flyouts and menus set `ShouldConstrainToRootBounds="False"`: the window is only as tall as the taskbar.
- Show desktop minimizes every minimizable window of other processes (`SW_SHOWMINNOACTIVE`) and the next click
  restores those still minimized; Explorer's own toggle isn't available as the shell.

### Layout (left → right, or centred like Windows 11 by setting)

1. Start button.
2. Search button — opens the Start menu with the search box focused. Can be hidden from the taskbar menu.
3. Pinned and running apps.
4. Tray area: chevron/overflow, tray icons.
5. Indicators: network, volume, microphone (when active).
6. Clock: time and short date; tooltip with full date; click opens a calendar flyout (`CalendarView`).
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
  the taskbar.
- Hovering a thumbnail could later add aero peek; not planned.

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

- Low-level keyboard hook (`WH_KEYBOARD_LL`): Win pressed and released alone, or Ctrl+Esc → toggle Start
  (`StartKeyDetector`, unit tested). Keys are not swallowed: Windows has to see Win go down for the Win+ hotkeys,
  and without Explorer nothing else reacts to Win alone. Ctrl+Esc also arrives as `SC_TASKLIST` on the taskman
  window; keyboard toggles within 300 ms of each other count once.
- `RegisterHotKey`: Win+D (show desktop toggle), Win+T (focus taskbar), Win+1…9 (the Nth button of the primary
  taskbar: no window → launch, one window → as a click, several → the one after the foreground window, wrapping;
  `TaskActivation`, unit tested), Win+S (Start with search focus).
- Win+T: the taskbar is `WS_EX_NOACTIVATE`, which keeps it from ever becoming active (and so from getting the
  keyboard). Win+T drops that style, activates the taskbar and focuses the first task button; the style comes back
  when the taskbar loses activation.
- Start closed because another window took the foreground (deactivation) doesn't hand the foreground back to the
  previous app — that would take it from the window being activated, e.g. the taskbar for Win+T.

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
  `NIF_SHOWTIP` get `NIN_POPUPOPEN`/`NIN_POPUPCLOSE` instead); balloon notifications are ignored (toasts are out of
  scope).
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
  disconnected. Each maps to a Segoe Fluent Icons glyph.
- Tooltip: network name and access status. Click opens `ms-settings:network` (shell mode: `ncpa.cpl`).

### Volume

- `IMMDeviceEnumerator` → default render endpoint → `IAudioEndpointVolume` with `IAudioEndpointVolumeCallback`
  (`AudioEndpoint`).
- `IMMNotificationClient` to follow default-device changes.
- Icon reflects mute and level (0 / low / medium / high glyphs). Mouse wheel changes volume in 2% steps.
- Click opens NeoShell's own flyout: device name, slider, mute toggle, link to `ms-settings:sound` (shell mode:
  `mmsys.cpl`). Moving the slider up unmutes, as Windows' own slider does.

### Microphone in use

- For each active capture endpoint: `IAudioSessionManager2` → `IAudioSessionNotification` for new sessions and
  `IAudioSessionEvents.OnStateChanged` per session (`CaptureMonitor`). The manager only reports new sessions after
  its session list has been asked for once.
- Visible while any capture session is `AudioSessionStateActive` (system sounds session excluded). Tooltip lists the
  apps (`IAudioSessionControl2.GetProcessId` → file description, else process name). Click opens
  `ms-settings:privacy-microphone` (shell mode: Sound's Recording tab, `mmsys.cpl,,1`).

### Threads and placement

- Core Audio and `NetworkInformation` call back on their own threads, and calling back into Core Audio from inside
  its callbacks can deadlock. So callbacks only mark state stale and raise `Changed`; `Indicators` marshals one
  update per burst to the UI thread (`DispatcherQueue.TryEnqueue`), which rebinds devices and reads fresh values.
- The callback objects are `[GeneratedComClass]` classes; the COM interfaces are `[GeneratedComInterface]`.
- The indicators sit on the primary taskbar, next to the tray, in both run modes (they don't depend on Explorer).
  Glyphs and tooltips come from `IndicatorDisplay` (unit tested).

## Start menu (`StartMenu/`)

- One topmost popup window (`StartMenuWindow`), created at startup and shown above the taskbar that opened it:
  centred on the monitor, or at its left when taskbar items are left-aligned. Acrylic, rounded corners, activatable
  (unlike the taskbar) for the search box; hides on deactivation, Esc, a click elsewhere on the taskbar, or Win.
- Activation: `Window.Activate` alone doesn't take the foreground from the app the user was in, so Start calls
  `SetForegroundWindow` (allowed: the click or key was the last input). On closing it hands the foreground back to
  that app; otherwise Windows picks the next window in z-order, which can be Explorer's invisible Start/search host.
- Flies out of the taskbar (250 ms, decelerating) and back into it on closing (150 ms, accelerating), as in Windows 11.
  The window itself moves, frame by frame (`CompositionTarget.Rendering`): the acrylic belongs to the window, so
  sliding the content would leave an empty acrylic panel standing still. It sits just below the taskbar that opened it
  in the topmost band (`PinnedWindow.SetLayer(Topmost, above)`), so the taskbar covers it on the way. What it shows is
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
TaskbarBackdrop      Acrylic | Mica | Translucent
PinnedTaskbarApps    list
PinnedStartApps      list
ExplorerStartPinsImported  bool
StartMenuWidth       double (epx)
StartMenuHeight      double (epx)
DesktopSortOrder     Name | Size | ItemType | DateModified
```

## Testing strategy

- **xunit** tests in `tests/NeoShell.Tests` for pure logic: taskbar window filter, grouping keys, NOTIFYICONDATA
  parsing (32/64-bit), app search ranking, indexer query building, startup entry parsing and `StartupApproved`,
  settings round-trip and corrupt-file handling, wallpaper style mapping, AppBar rect calculation, which desktop
  items get icons and in which order.
- Logic that touches Windows is split so the decision is a pure function over a snapshot (e.g. `WindowInfo`) that
  tests can construct.
- **Live UI checks** through UI Automation (`AutomationId`s on all interactive controls), never global keystrokes.
- **Shell mode** only on a test account or VM, via `tools/set-shell.ps1` (every sign-in) or `tools/start-shell.ps1`
  (this session: "Exit Explorer"'s message to `Shell_TrayWnd`, NeoShell started, then what's left of Explorer's
  process ended, since a later Explorer hangs on it).
