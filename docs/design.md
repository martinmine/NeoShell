# NeoShell design

This document describes what NeoShell does and how each feature is built. The milestones are in [plan.md](plan.md);
coding rules are in [CLAUDE.md](../CLAUDE.md).

## Scope

- Display the wallpaper.
- A WinUI taskbar with feature parity with the Windows 11 taskbar (exceptions in the system tray area).
- System tray icons, either all shown or hidden behind an overflow flyout.
- Network, volume and microphone-in-use indicators.
- A Start menu with search (apps + Windows Search Indexer), Settings, power options (Lock, Sign out, Sleep,
  Restart, Shut down) and a button to switch back to `explorer.exe`.

Out of scope: desktop icons, Quick Settings, Action Center/toasts, Widgets, Task View, Win+X, jump lists (for now).

## Technical decisions

| Area | Decision | Why |
|---|---|---|
| UI | WinUI 3, unpackaged, self-contained Windows App SDK | A shell starts before anything can install a runtime; MSIX gets in the way of being the shell |
| Target | `net10.0-windows10.0.26100.0`, min. Windows 11 (10.0.22000) | The Windows TFM gives WinRT projections (e.g. `NetworkInformation`) without packages |
| Look | Windows 11 Fluent, `DesktopAcrylicController` with a `SystemBackdropConfiguration` whose `IsInputActive` stays `true` | Keeps the acrylic on windows that rarely have focus |
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
  - Detects the run mode: `GetShellWindow() == 0` means NeoShell is the shell.
  - Installs crash handlers (`AppDomain.UnhandledException`, `Application.UnhandledException`,
    `TaskScheduler.UnobservedTaskException`): log, and in shell mode start `explorer.exe`. Unobserved task
    exceptions only log, because they don't end the process.
  - Native callbacks (window procedures, subclasses, hooks) catch every exception and raise
    `NativeCallback.UnhandledException`, which the app logs: an exception escaping `[UnmanagedCallersOnly]`
    would end the process without running any handler.
- **`App`** creates and wires the long-lived objects by hand: settings, logger, `ShellSession` (shell mode only),
  taskbars per monitor, Start menu, tray host, indicators.
- **Clean exit** disposes in reverse order: unregister AppBars (frees the reserved space), unhook hooks,
  destroy `Shell_TrayWnd`, release COM objects.

### Shell mode (`ShellSession`)

1. `SetShellWindow` on the primary wallpaper window and `SetTaskmanWindow` on the taskbar.
2. Create `Shell_TrayWnd` (see Tray) and broadcast `TaskbarCreated`.
3. Signal the shell-ready event (`ShellDesktopSwitchEvent` / `msgina: ShellReadyEvent`) so logon completes.
4. Run startup apps (below).
5. Register hotkeys and the low-level keyboard hook.
6. Handle `WM_QUERYENDSESSION` / `WM_ENDSESSION` to save settings and exit cleanly.

### Startup apps (shell mode only)

Explorer, not Windows, launches startup apps. As the shell NeoShell must do the same, otherwise OneDrive,
chat apps, password managers, GPU/audio/Bluetooth utilities etc. never start and the tray stays empty.

- Sources, in Explorer's order:
  1. `HKLM\...\RunOnce`, `HKCU\...\RunOnce` — delete each value before running it (as Explorer does).
  2. `HKLM\...\Run`, `HKLM\...\WOW6432Node\...\Run`, `HKCU\...\Run`.
  3. `shell:common startup` and `shell:startup` folders.
- Respect `HKCU|HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{Run,Run32,StartupFolder}`:
  a value whose first byte is odd (e.g. `0x03`) means disabled in Task Manager.
- Parse command lines with `CommandLineToArgvW` semantics; expand environment variables; launch with `ShellExecuteEx`.
- Run once per session; record it so a NeoShell restart, or Explorer after "Switch to Explorer", doesn't launch them
  again (Explorer checks the volatile `HKCU\...\Explorer\SessionInfo\<id>\StartupHasBeenRun` key — verify and set it).
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

## Taskbar (`Taskbar/`)

### Window

- One `TaskbarWindow` per monitor (setting: show on all displays), registered as an AppBar with `SHAppBarMessage`
  (`ABM_NEW`, `ABM_QUERYPOS`, `ABM_SETPOS`), bottom edge, 48 effective pixels high.
- Rect calculation (unit tested) from monitor bounds and DPI.
- Recreated on `WM_DISPLAYCHANGE` and DPI changes.
- `ABN_FULLSCREENAPP`: drop topmost / hide while a full-screen app is active on that monitor.
- Auto-hide (setting): `ABM_SETAUTOHIDEBAREX`, slide out when the cursor reaches the edge.
- Acrylic backdrop, light/dark following the system theme
  (`HKCU\...\Themes\Personalize\SystemUsesLightTheme`), updated on `WM_SETTINGCHANGE` (`ImmersiveColorSet`).

### Layout (left → right, or centred like Windows 11 by setting)

1. Start button.
2. Search button — opens the Start menu with the search box focused.
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
- Initial list from `EnumWindows`.
- **Which windows get a button** (unit tested on a `WindowInfo` snapshot), Explorer's rules:
  - visible, not cloaked (`DWMWA_CLOAKED`), and
  - `WS_EX_APPWINDOW`, or (no owner and not `WS_EX_TOOLWINDOW` and not `WS_EX_NOACTIVATE`).
  - Exclude NeoShell's own windows.
- **Grouping** (unit tested): by AppUserModelID from `SHGetPropertyStoreForWindow` (`PKEY_AppUserModel_ID`), falling
  back to the process image path (`QueryFullProcessImageName`). Pinned apps use the same key so running windows land
  on their pinned button.
- Icons: `WM_GETICON` (`ICON_BIG`/`ICON_SMALL2`) with timeout, `GetClassLongPtr(GCLP_HICON)`, then the AUMID/exe icon
  via `IShellItemImageFactory`. HICON → WinUI `SoftwareBitmapSource` in the app.

### Task buttons

- Indicators: running (short underline), active (long accent underline), multiple windows, flashing (orange background
  until activated).
- Left click: one window → activate it, or minimize if it's already foreground; several windows → show thumbnails.
  Pinned, not running → launch.
- Middle click or Shift+click: launch a new instance.
- Right click menu: app name (launch), Pin to taskbar / Unpin, Close window / Close all windows.
- Drag to reorder; order is persisted for pinned apps.
- Combine setting: always combine, combine when full, never combine (labels shown).
- Activation uses `SetForegroundWindow` after `AllowSetForegroundWindow`; restore minimized windows with
  `ShowWindow(SW_RESTORE)`.

### Pinned apps

- Stored in settings as a list of `{ AppUserModelId or Path, Arguments, DisplayName }`.
- Launch through `shell:AppsFolder\<AUMID>` when there is an AUMID, otherwise `ShellExecuteEx` on the path.
- Pinning from the Start menu and from a task button's context menu.

### Thumbnails

- Hovering a button opens a popup with one live DWM thumbnail per window (`DwmRegisterThumbnail` on the popup's
  HWND, `DwmUpdateThumbnailProperties` to place each one), title and close button.
- Hovering a thumbnail could later add aero peek; not planned.

### Progress, overlay badges (last milestone)

Apps call `ITaskbarList3`, which is implemented in `explorerframe.dll` inside the app process and talks to the
taskbar window over messages (`TaskbarButtonCreated` registered message and `WM_USER`-range messages to the window
owning `Shell_TrayWnd`/the task band). Implement as RetroBar/ManagedShell do: show a progress bar and overlay
icon on the task button.

### Hotkeys (shell mode only)

- Low-level keyboard hook (`WH_KEYBOARD_LL`): Win pressed and released alone → toggle Start; swallow it so it doesn't
  reach apps. Ctrl+Esc → Start.
- `RegisterHotKey`: Win+D (show desktop toggle), Win+T (focus taskbar), Win+1…9 (activate/launch the Nth button),
  Win+S (Start with search focus).

### Taskbar context menu

Task Manager, the taskbar settings toggles (alignment, combine, auto-hide, all displays, tray mode), Exit NeoShell
(alongside Explorer only).

## System tray (`Tray/`)

- `TrayHost` (Interop) creates a top-level window of class `Shell_TrayWnd` and a child `TrayNotifyWnd`, because
  `Shell_NotifyIcon` finds the tray by that class name.
- Handles `WM_COPYDATA`:
  - `dwData == 1`: `SHELLTRAYDATA` → `NIM_ADD`, `NIM_MODIFY`, `NIM_DELETE`, `NIM_SETFOCUS`, `NIM_SETVERSION`.
  - `dwData == 3`: `Shell_NotifyIconGetRect` — reply with the icon's screen rect.
  - `NOTIFYICONDATA` parsing is done from a byte buffer and handles both 32- and 64-bit callers
    (HWND/HICON fields are 32-bit handles in both). Unit tested with captured buffers.
- Icons are keyed by (`hWnd`, `uID`) or `guidItem`. `NIS_HIDDEN` is honoured. Tooltips from `szTip`; balloon
  notifications are ignored (toasts are out of scope).
- After `Shell_TrayWnd` exists, broadcast `RegisterWindowMessage("TaskbarCreated")` so running apps re-add icons.
- Remove icons whose owner window has died (`IsWindow` check on a timer and on mouse-over).
- **Mouse forwarding** with `NOTIFYICON_VERSION_4` semantics: `wParam` = anchor point (x, y), `lParam` low word =
  message (`WM_LBUTTONUP`, `NIN_SELECT`, `WM_CONTEXTMENU`, `NIN_POPUPOPEN`…), high word = icon ID; older versions
  get `wParam = uID`, `lParam = mouse message`. Call `AllowSetForegroundWindow` for the owner process first.
- **Display mode** (setting `TrayMode`): `ShowAll` (every icon in the taskbar) or `Overflow` (icons behind a chevron
  flyout).
- Alongside Explorer: Explorer owns `Shell_TrayWnd`, so the tray is best-effort/disabled. Fully supported as the shell.

## Indicators (`Tray/`)

### Network

- `NetworkInformation.GetInternetConnectionProfile()` + `NetworkStatusChanged`.
- States: Ethernet, Wi-Fi (`WlanConnectionProfileDetails`, `GetSignalBars()` 0–5), cellular, no internet access,
  disconnected. Each maps to a Segoe Fluent Icons glyph.
- Tooltip: network name and access status. Click opens `ms-settings:network`.

### Volume

- `IMMDeviceEnumerator` → default render endpoint → `IAudioEndpointVolume` with `IAudioEndpointVolumeCallback`.
- `IMMNotificationClient` to follow default-device changes.
- Icon reflects mute and level (0 / low / medium / high glyphs). Mouse wheel changes volume in 2% steps.
- Click opens NeoShell's own flyout: device name, slider, mute toggle, link to `ms-settings:sound`.

### Microphone in use

- For each active capture endpoint: `IAudioSessionManager2` → `IAudioSessionNotification` for new sessions and
  `IAudioSessionEvents.OnStateChanged` per session.
- Visible while any capture session is `AudioSessionStateActive`. Tooltip lists the processes
  (`IAudioSessionControl2.GetProcessId` → process name). Click opens `ms-settings:privacy-microphone`.

## Start menu (`StartMenu/`)

- Topmost popup window anchored above the Start button on the taskbar's monitor; acrylic; hides on deactivation,
  Esc, or Win.
- **Pinned** grid (persisted in settings) and an **All apps** alphabetical list with letter headers.
- **App catalog**: enumerate `shell:AppsFolder` (`SHCreateItemFromParsingName` → `BHID_EnumItems`), reading display
  name, parsing name (AUMID) and icon (`IShellItemImageFactory`). Covers Win32 and packaged apps. Refreshed in the
  background when the Start menu opens if stale.
- **Search** — typing anywhere in the open menu focuses the search box:
  - Apps: ranked exact > prefix > word-start > contains (case-insensitive, unit tested). Shown first, instantly.
  - Indexer: `ISearchManager` → catalog `SystemIndex` → `ISearchQueryHelper` (set `QuerySelectColumns`,
    `QueryMaxResults`, `QueryContentLocale`), `GenerateSQLFromUserQuery`, executed with `OleDbConnection`
    (`Provider=Search.CollatorDSO;Extended Properties='Application=Windows'`).
    Columns: `System.ItemNameDisplay`, `System.ItemUrl`/`System.ItemPathDisplay`, `System.Kind`,
    `System.DateModified`. Results grouped as Settings, Folders, Documents, Other.
  - Runs on a background thread, ~150 ms debounce, cancelled by the next keystroke.
  - Enter opens the top result; arrow keys move the selection.
- **Bottom row**:
  - User name/picture (from `HKCU\...\AccountPicture` or a placeholder).
  - Settings button → `ms-settings:`.
  - Switch to Explorer button (see lifecycle).
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
TrayMode             ShowAll | Overflow
PinnedTaskbarApps    list
PinnedStartApps      list
```

## Testing strategy

- **xunit** tests in `tests/NeoShell.Tests` for pure logic: taskbar window filter, grouping keys, NOTIFYICONDATA
  parsing (32/64-bit), app search ranking, indexer query building, startup entry parsing and `StartupApproved`,
  settings round-trip and corrupt-file handling, wallpaper style mapping, AppBar rect calculation.
- Logic that touches Windows is split so the decision is a pure function over a snapshot (e.g. `WindowInfo`) that
  tests can construct.
- **Live UI checks** through UI Automation (`AutomationId`s on all interactive controls), never global keystrokes.
- **Shell mode** only on a test account or VM, via `tools/set-shell.ps1`.
