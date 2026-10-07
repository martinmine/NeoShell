# NeoShell design

This document describes what NeoShell does and how each feature is built. The milestones are in [plan.md](plan.md);
coding rules are in [CLAUDE.md](../CLAUDE.md).

## Scope

- Display the wallpaper and the desktop icons, with Explorer's context menus for icons and the desktop.
- A WinUI taskbar with feature parity with the Windows 11 taskbar (exceptions in the system tray area).
- System tray icons, each on the taskbar or behind the chevron as Explorer keeps it (`NotifyIconSettings`).
- Network, volume and battery indicators, and the privacy indicator (apps using the microphone or the location);
  network, volume and battery are one button, as in Windows 11, that opens Quick Settings.
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
- `ShellBackdrop` overrides `OnDefaultSystemBackdropConfigurationChanged` with an empty body. `base.OnTargetConnected`
  makes WinUI (3.2.3, App SDK 2.5.1) track each target for `GetDefaultSystemBackdropConfiguration`, listening to its
  content's `ActualThemeChanged`; on a theme change it calls that method with the target resolved from a weak
  reference, which for a window is already null, and the base implementation fails `E_INVALIDARG` ("target"). The
  `ArgumentException` comes from a XAML callback, so WinUI fail-fasts: NeoShell crashed whenever a surface's theme
  changed (starting in, or switching to, the light theme). NeoShell's backdrops follow their own `Theme`/`Tint`, set
  by each window, so the default configuration isn't needed.

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
4. Start the shell service objects (below), then run startup apps (below), queued at low priority after the tray
   exists.
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

### Shell service objects (shell mode only; `ShellSession`, Interop `Shell/ShellServiceObjects.cs`)

Windows' own tray items that aren't apps come from COM objects Explorer starts with its taskbar: without them there is
no Safely Remove Hardware icon, no Bluetooth pairing prompts, no Sync Center or offline files, and so on. How Explorer
does it (Windows 11 25H2, `explorer.exe` and `stobject.dll` read with symbols; checked live with cdb and the modules
loaded):

- `CTray::_StartSSO1` is one of the taskbar's parallel startup tasks ("SSO1", with `PrelaunchAtLogon`,
  `DesktopApiSurface`, `TaskbarApiSurface`), run on the taskbar's UI thread once `Shell_TrayWnd` exists. It does
  `CoCreateInstance(CLSID_SysTray {35CEC8A3-2BE6-11D2-8773-92E220524153}, CLSCTX_INPROC_SERVER, IOleCommandTarget)`
  and `Exec(CGID_ShellServiceObject {000214D2-0000-0000-C000-000000000046}, SSOCMDID_OPEN = 2, 0, pvaIn, NULL)`.
  `pvaIn` is a `VT_UI4` startup cookie (task index | 0x10000) that SysTray only posts back to `Shell_TrayWnd` as
  message 0x574 once it has loaded everything, so Explorer can finish its startup tasks; NeoShell passes none (SysTray
  then posts nothing). `CTray::_HandleDestroy` (the taskbar's `WM_DESTROY`) sends `Exec(…, SSOCMDID_CLOSE = 3, …)`
  and releases it.
- SysTray's `Exec(OPEN)` returns at once: it starts its own thread, "SSO Main" (`SysTrayMain`, `SHCreateThreadWithHandle`
  without flags; the thread calls `CoInitializeEx(COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE)`). That thread
  creates the hidden window `SystemTray_Main`, starts KeepAwake, power (battery icon, Windows 11 shows its own) and
  hot plug (Safely Remove Hardware), then `CShellServiceObjectMgr::LoadObjects`, and runs a message loop. `Exec(CLOSE)`
  sends `WM_CLOSE` to `SystemTray_Main` and waits for the thread, pumping messages, with no time limit; on its way out
  the thread closes every object it loaded.
- `LoadObjects` reads `HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\ShellServiceObjects\{CLSID}`: an object
  starts if it has an `AutoStart` value (unless the same key in HKCU has `NoAutoStart`), or HKCU's key has `AutoStart`,
  **and** its CLSID is in stobject's built-in list of 20 (third-party objects registered there never load). The list
  includes a flag per object (shared or own thread) and, for Windows To Go, a check that it is running from one.
  Objects without `AutoStart` load on demand (`SHEnableServiceObject`, e.g. hcproviders.dll after Security and
  Maintenance asks for it). The old `ShellServiceObjectDelayLoad` key (here only WebCheck) is read by nothing any more.
- On this VM, Explorer and NeoShell end up with the same objects loaded: SndVolSSO (volume service), dxp (Devices and
  Printers), Windows.CloudStore, Windows.FileExplorer.Common (OneDrive network states), wpdshserviceobj (portable
  devices), cscui (offline files), srchadmin (Windows Search), shdocvw (WebCheck), SyncCenter, bthprops (Bluetooth
  authentication agent, `BluetoothNotificationAreaIconWindowClass`), Actioncenter (Security and Maintenance), and later
  hcproviders. Not loaded by either: pwsso (Windows To Go only) and hgcpl (HomeGroup: not in stobject's list).
- Tray icons they add, all with `NIF_GUID` (the "system control area" GUIDs `7820AE7x-23E3-4229-82C1-E41CB67D5B9C`):
  hot plug `…AE78` (version 4, callback 0x4CA; it acts on `NIN_SELECT` and `WM_CONTEXTMENU`, both opening the eject
  menu), power `…AE75` (none without a battery), volume `…AE73` and microphone `…AE82` (SndVolSSO). Explorer's
  `Taskbar.dll` maps volume, network `…AE74`, power, microphone and Meet Now `…AE83` to its own buttons
  (`c_scaidToResourceMap`) and never shows those as tray icons; NeoShell, which has its own indicators, accepts them
  but doesn't show them either (`TrayIconState.IsSystemIcon`, unit tested). Without that the classic speaker icon
  ("Speakers: 75%") showed in the overflow.
- NeoShell does the same calls, but creates SysTray on an STA thread of its own (pumping messages, so broadcasts to
  its COM window don't hang) rather than the UI thread: a DLL that hangs while loading can't hold up the shell, and on
  exit `ShellSession` waits at most 3 s for `CLOSE` (logged if it takes longer; the thread is a background thread and
  ends with the process). Their code runs in NeoShell's process, as in Explorer's: an object that crashes takes the
  shell down, and the watchdog then starts Explorer.
- Compared on this VM (VMware marks its virtual PCI devices ejectable, so the icon shows without removable media):
  Explorer's and NeoShell's overflow both show "Safely Remove Hardware and Eject Media" with the same glyph, and a left
  or right click opens the same menu (SysTray draws it: Open Devices and Printers, the disks, USB root hub, the
  `VIRTMACHINE` group of controllers and the DVD drive, the network adapter) at the same place, its bottom at the
  pointer. On `/exit` the power, hot plug and volume icons are deleted ~50 ms into shutdown and NeoShell exits in
  under a second. Not run: actually ejecting a device (each would remove a virtual controller, disk or the NIC from
  the running VM).

### AutoPlay (shell mode only; `AutoPlay/`, Interop `Shell/AutoPlay*`, `VolumeArrivals`, `OpticalDrives`)

**Why NeoShell has its own.** The Shell Hardware Detection service only reports arrivals; the AutoPlay work runs in
the shell process. `windows.storage` registers for hardware notifications only in a process in explorer server mode 3
(Explorer's desktop process); on arrival `shell32!CMountPoint::DoAutorun` → `CAutoPlayParams::PromptUser` creates
twinui's `CAutoPlayUI` (`HKLM\...\Explorer\AutoplayExtensions\ShellUI`) in-process. Without Explorer nothing reacts.
Putting NeoShell in that mode (`windows.storage!SetExplorerServerMode(3)` + its change notification server, ordinal
1002) brings the arrival in, but twinui's toast fails with `E_ACCESSDENIED` (it needs `CreateWindowInBand` in the
notification band, Explorer only) and the prompt waits invisibly; so NeoShell reimplements it from shell32's and
twinui's code (Windows 11 25H2, read with symbols in Ghidra) and Explorer's behaviour, measured on this VM with ISOs
mounted by `Mount-DiskImage` (no admin needed; the drive is a DVD drive).

**Explorer's behaviour**
- **Toast.** Banner only (never in the notification center), app `Windows.SystemToast.AutoPlay`, named "AutoPlay"
  (twinui `-9914`) with no logo: the notification UI draws its default app glyph (Segoe Fluent `ECAA`). Title: the
  drive's name ("DVD Drive (F:) NEOPICS"); body: twinui `-9992` "Select what happens with %1." with shell32's
  description of the content (see below). It shows ~0.4 s after the volume, stays as long as other toasts (~6 s) and
  goes when the media is removed. Notifications off for AutoPlay (or Do not disturb) means no toast and no prompt.
  Clicking it opens the flyout; its close button or timing out drops the prompt.
- **Flyout** (twinui `CAutoPlayHandlerChooser`, the DirectUI chooser of Windows 8, class `Shell_Flyout`, topmost,
  takes the foreground): white whatever the theme, 387×(content) px with a 1 px `#CCCCCC` border, 5 px from the top
  right of the screen (the policy `DisplayToastAtBottom` moves it to the bottom), square, no shadow, appears and goes
  without animation. Inside (effective px): the drive's name in Segoe UI Light 20 pt in a 66 px band (wrapped in
  U+202A/U+202C), "Choose what to do with %1." (twinui `-9978`, Segoe UI Semilight 11 pt) on a 20 px line, then the
  list: 10 px above, 20 below, 60 px rows. A row: a 40×40 tile at (20, 10) behind the 32 px icon, in the immersive
  colour `ImmersiveStartDesktopTilesBackground` (`#0060B7` for accent `#0063B1`; a packaged handler with an
  `AppUserModelID` uses its tile colour), the action in black and the provider in `#666666` on 20 px lines from x 73.
  Hover: row `#DEDEDE`, provider black. Pressed: the row tilts in (Windows 8's pointer-down). Keyboard: focus starts on
  the choice picked last (`EventHandlersDefaultSelection`), else the first, shown (after a key) as 2 px black lines
  at the row's top and bottom; Up/Down and Tab move without wrapping, Enter chooses, Esc or a click elsewhere closes
  without saving anything. Removing the media closes it. Headers (Semibold 11 pt, a 40 px block):
  "Install or run program from your media" (twinui `-9926`) or "Run enhanced content" (`-9927`) over a disc's
  program, "Other choices" (`-9904`) over the rest. The list is `60n+30` px for n ≤ 5 rows, else 280 (four and a half
  rows, scrolling), plus 80 with headers (`_CalculateScrollViewerHeight`).
- Seen live: a data disc of any content (pictures, music, video, mixed, documents, empty) → "removable drives":
  Configure storage settings (Settings), Open folder to view files (File Explorer), Take no action. `VIDEO_TS` →
  "DVD films": Play DVD movie (VLC), Find a new DVD app (Store), Take no action (no Open folder). `autorun.inf` →
  "this disc", its `label=` as the drive's name, "Run NeoShell test" (its `action=`) / "Publisher not specified"
  under the program header, then Other choices. With "Choose what to do with each type of media" for removable drives:
  "pictures" → Import Photos and Videos (Photos); mixed → Play audio files (VLC), Play (Windows Media Player), Play
  video files (VLC), Import Photos and Videos, then the general choices, scrolling.

**What it does** (shell32 `CAutoPlayParams`, `CAutoplayContentHandler`, `CAutoplayHandler`; twinui `CAutoplayDialog`)
- Skipped when `HKCU\...\AutoplayHandlers\DisableAutoplay` = 1, the drive is barred by policy (`NoDriveTypeAutoRun`
  bit per drive type, `NoDriveAutoRun`/`NoDrives` bit per letter; network drives never), a full-screen game runs,
  the window in front answers the registered `QueryCancelAutoPlay` message (wParam drive index, lParam `ARCONTENT_*`)
  with non-zero, or an `IQueryCancelAutoPlay` in the running object table returns `S_FALSE` (3 s at most).
- **Content** (shell32's `CT_*` values): from the volume, as shsvcs' `_UpdateSpecialFilePresence` looks: on optical
  drives `video_ts\video_ts.ifo`/`dvd_rtav\vr_mangr.ifo` (DVD movie), `audio_ts\audio_ts.ifo` (DVD audio),
  `VCD\entries.vcd`, `SVCD\entries.sv[cd]`, `BDMV`/`BDAV` on UDF (Blu-ray), audio tracks; on any drive `DCIM`, `AVCHD`,
  `PRIVATE\AVCHD` (memory card); `autorun.inf` with `open=`/`shellexecute=` on optical drives (not with policy
  `NoAutorun` = 1). Autorun with audio or a film is an enhanced CD/DVD. Then `CAutoPlayParams::Init`: unless the user's
  choice for `StorageOnArrival` is `MSUseAdvancedStorageOptions`, nothing else found means a **removable drive**
  (`StorageOnArrival`), whatever the files; with it, the files are walked (4 levels) and perceived types give music /
  pictures / videos, several = mixed, none = unknown content. Event names and descriptions are shell32's table
  (`AutorunINFLegacyArrival` "this disc", `PlayDVDMovieOnArrival` "DVD films", `StorageOnArrival` "removable drives",
  memory cards `ShowPicturesOnArrival` with choices under `CameraAlternate`…).
- **Saved choice**: `HKCU\...\AutoplayHandlers\UserChosenExecuteHandlers\[CameraAlternate\]<event>` (default value).
  None or `MSPromptEachTime` asks; `MSTakeNoAction` does nothing; a handler that still exists runs without a toast
  (also written to `EventHandlersDefaultSelection`); policy `NoAutorun` = 2 runs a disc's program. Not implemented:
  twinui's "You have new choices" prompt when a handler was installed after the choice was saved.
- **Choices**: `EventHandlers\<event>` value names, HKCU then HKLM, each read from `Handlers\<name>` (HKCU first; a
  user's needs `InvokeProgID`+`InvokeVerb`): `Action`, `Provider` (none for `MSTakeNoAction`, `MSPromptEachTime`,
  `MSAutoRun`), `DefaultIcon` (none: the drive's icon), resource strings resolved by `SHLoadIndirectString`. Each
  list is newest first by the `Handlers\<name>` key's last write time (`CAutoplayHandlerList::Add` inserts before the
  first older one), a name once. Groups in order: a disc's program; the content's choices (mixed content: each kind's,
  never `MixedContentOnArrival`'s; memory cards add the video ones; blank media add Take no action); the general
  ones, `UnknownContentOnArrival` + Take no action. Media discs (audio CD, DVD, VCD, Blu-ray) drop Open folder. With
  mixed content twinui adds each kind's choices as the walk finds that kind, after those listed, so the order follows
  the files (on this VM: audio VLC, Media Player, video VLC, Photos). No prompt unless there's more than taking no
  action.
- **Choosing**: `EventHandlersDefaultSelection\<event>` = the choice; `UserChosenExecuteHandlers\<event>` = the
  choice, except for a disc's program, mixed or unknown content and when the setting was "Ask me every time", where
  it's set to `MSPromptEachTime` (both confirmed in Explorer). Then it runs, on a thread of its own: `InvokeProgID` +
  `InvokeVerb` on the drive's root (`ShellExecuteEx` with the class; `Folder`/`open` opens the drive, VLC's
  `VLC.OPENFolder` plays it), a `CLSID` handler through `IHWEventHandler(2)` (`Initialize(InitCmdLine)`, then the
  drive, "" and "DeviceArrival", as `CAutoplayHandler::Invoke`), the disc's program from the drive's root.
- **Drive names.** Outside Explorer's process the shell can't use the hardware service's volume data and calls every
  optical drive "CD Drive" and ignores `autorun.inf`'s label. NeoShell asks the drive its MMC features
  (`IOCTL_CDROM_GET_CONFIGURATION`, allowed to users) as shsvcs' `_UpdateMMC2CDInfo` and names it as
  `CMtPtLocal::_GetCDROMName` (BD-RE/BD-R/BD-ROM, DVD RW/R, DVD/CD-RW, DVD/CD-R, DVD RAM, DVD, CD-RW, CD-R, CD Drive;
  `windows.storage.dll` strings), and puts the autorun label in place of the volume's.

**NeoShell.** `VolumeAutoPlay` (created by `ShellSession`) listens for `WM_DEVICECHANGE` volume arrivals and removals
on a hidden top-level window (broadcasts don't reach message-only windows), works the content and choices out off the
UI thread (`AutoPlayRules`, unit tested), shows the toast through `ToastPopups.ShowBanner` and the flyout
(`AutoPlayFlyout` + `AutoPlayChooser`). Compared side by side with Explorer at 100 %: flyout bounds identical
(1372,5–1759,303; with headers to 383; mixed to 373), text rows identical, columns within 1 px (WinUI lays text out
with fractional advances; `CharacterSpacing` 9 and 5 make up DirectUI's whole-pixel widths), colours, hover, focus
lines, keys, registry writes and toasts the same. Differences: the pressed tilt is WinUI's
`PointerDownThemeAnimation`; Explorer keeps the row grey when the pointer leaves it pressed; the scroll bar is WinUI's.
Not run live (no hardware or admin on the VM): USB sticks and memory cards (a VHD needs admin to attach), audio CDs,
Blu-ray, VCD and blank discs (blank media isn't detected at all: it needs IMAPI), `CLSID` handlers (none registered
for volumes here), packaged handlers' tile colours, WPD devices (phones, cameras over MTP), which Explorer handles
through the hardware service's device events, not volumes.

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
  - Building the menu, the handlers show the wait cursor and then put back the UI thread's cursor from before, which
    is the wait cursor too (WinUI shows its own over its windows); `ShellMenu` sets the arrow once it's built, or the
    spinner stayed over the menu until the pointer moved.
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
  dialog (`ShutDownDialog`), as Explorer does: "What do you want the computer to do?" with the power menus' choices
  for it (Switch user, Sign out, Sleep, …; see Start's **Power menus**), chosen first as in Explorer, a line on
  what the choice does, OK and Cancel (Enter and Esc). shell32's own dialog
  (`ExitWindowsDialog`, ordinal 60) hands the request to Explorer's taskbar and shows nothing without it. A WinUI
  window with Mica, in the middle of the primary monitor; one at a time.

## Taskbar (`Taskbar/`)

### Window

- One `TaskbarWindow` per monitor (setting: show on all displays), bottom edge, 48 effective pixels high, kept in
  place and topmost by `PinnedWindow`, frameless (`FramelessWindow`), `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
- Screen space:
  - Alongside Explorer: registered as an AppBar with `SHAppBarMessage` (`ABM_NEW`, `ABM_QUERYPOS`, `ABM_SETPOS`,
    `ABM_REMOVE`); Explorer places it above its own taskbar and sends `ABN_POSCHANGED` when it must move.
  - As the shell: `SHAppBarMessage` is served by the shell's own `Shell_TrayWnd`, so the taskbar can't use it.
    NeoShell sets the monitor's work area itself (`SPI_SETWORKAREA`) and restores it on exit. `ShellWorkArea` keeps
    what the taskbar, the widget sidebar and other apps' app bars (see "App bars" under System tray) reserve per
    monitor, and sets each change from the thread pool, one at a time, each going out with the latest reservation:
    `SPIF_SENDCHANGE` waits for every window, and apps that answer by calling the shell (re-adding tray icons) would
    wait for the UI thread in turn (startup hung that way). What's reserved during one turn of the UI thread goes out
    as one change, after it, and only when the area differs from the last one sent (Explorer defers its changes the
    same way, `DeferWorkAreaChangesGuard`): a taskbar made again (alignment, auto-hide, all displays) gives its strip
    back and takes it again, which would otherwise make maximized windows grow under it and shrink back. App bars'
    changes go out at once, without waiting, as Explorer sets them (a bar may read the work area right after
    `ABM_SETPOS`). `ShellWorkArea.Changed` tells the sidebar and the app bars of each change.
  - **Maximized windows follow the work area** (`WorkArea.Set`). Explorer doesn't move them itself: cdb on
    explorer 26200 showed no `SetWindowPos`/`ShowWindow`/`SetWindowPlacement` from it; `CTray::RecomputeAllWorkareas`
    → `MonitorEnumProc` → `CTray::SetWorkArea` calls `SystemParametersInfoW(SPI_SETWORKAREA, uiParam = TRUE, rect,
    0)` and then, unless it's starting up, `SendNotifyMessage(HWND_BROADCAST, WM_SETTINGCHANGE, SPI_SETWORKAREA, 0)`.
    The nonzero `uiParam` (undocumented) has win32k maximize every maximized window again for the new work area
    (each gets `WM_GETMINMAXINFO` and `WM_WINDOWPOSCHANGING` with `SWP_STATECHANGED` (0x8000) |
    `SWP_FRAMECHANGED` | `SWP_NOZORDER` | `SWP_NOACTIVATE`, then `WM_SETTINGCHANGE`); with 0, from any process and
    whatever the flags, nothing moves. NeoShell passes 1 too, so every source (taskbar, auto-hide, sidebar shown,
    hidden or resized, app bars, NeoShell starting and exiting) behaves as Explorer's. Measured on the VM under both
    shells, the same: elevated windows (Task Manager) follow, about 0.1 s after the others (UIPI doesn't apply in
    the kernel); a hung window follows once it answers, without holding up the caller; a cloaked one
    (`DWMWA_CLOAK`, as on another virtual desktop) is skipped and fitted when it's uncloaked; a minimized one keeps
    its maximized state and maximizes to the new area when restored; a borderless window maximized to the whole
    monitor (full screen) stays so; conhost and WinForms windows the same. Other monitors weren't tried (one monitor).
    The order: Explorer's auto-hide slides the taskbar away (about 270 ms) and then sets the work area, so windows
    grow once it's gone; turned off, the taskbar slides in over the windows and they shrink as it lands. An app bar's
    `ABM_SETPOS` returns after the windows were resized (about 140 ms with five maximized windows).
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
  Settings, File Explorer, Search, Run, Shut down or sign out (the power menus' choices, filled as it opens), Desktop. As the
  shell, Settings pages are Control Panel applets. Run is shell32's `RunFileDlg` (ordinal 61) on a thread of its
  own, moved above the taskbar's left end by a thread CBT hook as it activates.
- Show desktop minimizes every minimizable window of other processes (`SW_SHOWMINNOACTIVE`) and the next click
  restores those still minimized; Explorer's own toggle isn't available as the shell. `ShowDesktop` also does
  Win+M (minimize all, adding to those minimized before), Win+Shift+M (restore them) and Win+Home (all but the
  window in front; again restores them without activating, though some apps, Chromium's, activate themselves).

### Layout (left → right, or centred like Windows 11 by setting)

1. Start button.
2. Search — the icon, a search box, or the icon with a label, or nothing: Explorer's search setting (see Search on the
   taskbar). Opens the Start menu with the search box focused.
3. Pinned and running apps.
4. Tray area: chevron/overflow, tray icons.
5. Indicators: privacy (microphone or location in use), the input method (with more than one: an IME's mode, then the language),
   then network, volume and battery as one button (Quick Settings).
6. Clock: time (with seconds by setting) and short date, then the notification bell; tooltips with the full date
   and the day and time, and the count of new notifications; click opens the notification center and calendar (see
   Clock and notification bell, and Notifications and calendar).
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
- Right click menu: the app's jump list (below), then app name (launch), Pin to taskbar / Unpin, End task (below),
  Close window / Close all windows (`SC_CLOSE`). No separator between these system items, as in Explorer (only one
  under the jump list).
- **End task** (`TaskEnding`, tested), Settings > System > For developers > End task: `TaskbarEndTask` under
  `HKCU\…\Explorer\Advanced\TaskbarDeveloperSettings`. Explorer's jump list (JumpViewUI.dll,
  `TaskbarJumpListFrameViewModel::Initialize`) reads it with `RegGetValueW` each time a menu opens, so a change
  applies at once; only a DWORD of exactly 1 turns it on. Shown only on buttons with windows (where Close window
  is), just above it, never for the AppID `Microsoft.Windows.Explorer` (File Explorer's windows live in the shell's
  process). Text "End task", or "End all tasks" with several windows (`JumpView_EndTaskAction`/`…AllAction`), glyph
  U+F140 (`VerbGlyphs::SegoeMDL2Assets::EndTask`, a circle with a slash), always enabled.
  A click goes to the jump view broker (windows.internal.shell.broker.dll `CJumpViewBroker::EndTask`), by AppID:
  - If the immersive shell knows applications with that AppID (`SID_ImmersiveApplicationArrayService`, i.e. UWP
    CoreWindow apps such as Calculator, whose windows are ApplicationFrameHost's frames), it ends them with
    `IImmersiveAppCrusher`. NeoShell ends the package with `IPackageDebugSettings::TerminateAllProcesses`
    (`PackagedApps.EndAll`), for windows of class `ApplicationFrameWindow`; ApplicationFrameHost and the other apps
    it hosts keep running.
  - Otherwise (desktop apps, packaged ones such as Paint too) it sends `WM_COPYDATA` (7) to `Shell_TrayWnd`, and
    Taskbar.dll's `CTaskBand::HandleJumpViewEndTask` calls `EndTask(hwnd, FALSE, TRUE)` for each of the group's
    windows on a thread of its own (`_EndTaskThreadProc`). user32's `EndTask` asks CSRSS to end the window's
    process: at once, no `WM_CLOSE` first, no prompt, no error shown. NeoShell does the same on the thread pool.
  Measured with a test app: the window's process ends within ~0.2 s, hung or not, without `WM_CLOSE`; its child
  processes keep running; an elevated app (Task Manager) ends too, from a medium-integrity caller, since CSRSS ends
  it (no UAC prompt).
- Pressed, the icon shrinks to 0.8 (only the icon, as in Explorer); dragged, it grows to 1.2 and loses its plate and
  pill (`IconPress`, a `ScaleTransition` on the icon). The Start and search icons shrink too (not the box's).
- Drag to reorder (by hand, `TaskReorder`: the button slides along the row and its neighbours make way). The order
  is persisted for pinned apps and kept for the session for the others (`TaskOrder`, unit tested): buttons keep the
  order last shown, a new window goes next to its app's others and a newly started app at the end. Shared by the
  taskbars and kept when they're recreated.
- Combine setting: always combine, combine when full, never combine (labels shown). "When full" compares the labeled
  buttons' width with the space left beside the right-hand panel (on both sides when centred); the list is capped at
  that width so it never covers the clock.
- Activation uses `SetForegroundWindow` (allowed: the user's click on the taskbar was the last input); minimized
  windows are restored with `ShowWindow(SW_RESTORE)`. Clicking the active window's button minimizes it.
  A window of an app running as administrator refuses `ShowWindow`/`ShowWindowAsync` from NeoShell (UIPI: access
  denied, NeoShell runs at medium integrity, as Explorer does), so a minimized one could never be brought back;
  `TopLevelWindows` then posts the system menu's command (`WM_SYSCOMMAND` with `SC_RESTORE`, `SC_MINIMIZE` or
  `SC_MAXIMIZE`), which gets through. This covers the taskbar, previews, Show desktop, Alt+Tab and snapping.

### Pinned apps

- Stored in settings as a list of `{ AppUserModelId or Path, Arguments, DisplayName }`.
- Launch packaged apps (AUMID `<family>!<app>`) with `IApplicationActivationManager::ActivateApplication`, on a
  background thread as it waits for the app, recording the start in UserAssist as Explorer does (see "How Explorer
  records a packaged app's start" under Start); other apps through `shell:AppsFolder\<AUMID>` when there is an AUMID,
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
  without categories of its own (no list, or tasks only, as Edge's) gets Recent, as in Explorer's taskbar and Start;
  a known category other than 1 or 2 shows nothing (Settings writes one of -1, and Explorer shows it no Recent).
- Not done: a web page's short name (Explorer shows Edge's `https://…/login/device` as "device"; every `SIGDN` and
  `System.ItemNameDisplay` give the whole URL).
- **Opening**: a link through its own `IContextMenu` default command, which keeps its arguments, working directory and
  a packaged app's identity, as Explorer does; a recent file with `ShellExecuteEx` on its ID list (the default verb,
  which may not be the app the list belongs to).
- Icons: a link's icon location (`SHDefExtractIcon`), else its target's icon; `ms-appx:` icons of packaged apps
  aren't read, so those entries show the target's.
- Pinned entries (kept in `AutomaticDestinations`) aren't shown, and entries can't be pinned or removed. Explorer
  shows them under Pinned, first (File Explorer's are Quick access, then left out of its Frequent), on the taskbar and
  in Start's app menus alike.

### Thumbnails

- Hovering a button (500 ms) opens a popup with one live DWM thumbnail per window (`DwmRegisterThumbnail` on the
  popup's HWND, `DwmUpdateThumbnailProperties` to place each one over a XAML placeholder), title and close button.
  It closes 400 ms after the pointer leaves both the button and the popup; once open, it follows the pointer along
  the taskbar.
- The popup slides up out of the taskbar (200 ms) and back into it on closing, and slides sideways to the next button
  (`WindowSlide`, as Start): it's the window that moves, since DWM draws the thumbnails into the window. It sits
  just below the taskbar in the topmost band (`PinnedWindow.SetLayer(Topmost, above)`), so the taskbar covers it.
- Peek: hovering a thumbnail for 400 ms shows only its window (`DwmpActivateLivePreview`, dwmapi ordinal 113: the
  undocumented call Explorer's taskbar makes, as there is no public one); moving to the next thumbnail moves the peek
  straight away, and it ends when the pointer leaves the thumbnails, on a click (after switching) or when the popup
  closes. DWM ignores a second peek while one is on, so moving it ends the first, which crossfades. The taskbar,
  popup and wallpaper windows set `DWMWA_EXCLUDED_FROM_PEEK` to stay visible, as Explorer's do.

### Dragging over the taskbar (`TaskbarDrop`, unit tested)

What Explorer's taskbar does with a drag from any app (files from File Explorer or the desktop, text, images),
measured on 25H2 with `mouse_event` drags and frame recordings, and read from `Taskbar.View.dll`
(`ExternalTaskListDragDropController`, `TaskbarFrame::OnDragEnter`…: the XAML taskbar's own drop events, which take
the drag's `DataPackageView`; `Taskbar.dll`'s `CTaskListWnd`/`CTrayDropTarget` OLE targets only draw the drag image):

- **Nothing opens by dropping on a button.** A file over a running or pinned app's button, Start, the empty taskbar,
  the tray or the clock is refused (⊘, no caption); dropping does nothing. Windows 10's "Open with <app>" is gone.
- **Hovering a button brings its window forward**, ~410 ms after the drag enters it, moving or not (the hover
  time); the button shows its hover plate meanwhile. With several windows the previews open instead; hovering a
  preview ~1.1 s brings that window forward (no peek first) and closes them. A pinned app that isn't running does
  nothing.
- **Pinning by drag.** Exactly one program (`.exe`) or a shortcut to one, not pinned already, is "Link" (the system's
  default caption for the effect, with its glyph) anywhere from Start to the tray: the buttons open a one-button gap
  where it would go (before the first button whose middle is past the pointer; over Start, first) and it's pinned
  there on the drop, growing in. Over the tray and the clock it's refused. Anything else (a document, a shortcut to
  one, a `.bat`, a folder, several files, an app pinned already) is refused everywhere and only hovers buttons; a
  pinnable drag doesn't bring windows forward. A program is named by its file description (`charmap.exe` →
  "Character Map"), a shortcut by its file name; a shortcut's arguments go with its program, whose windows then
  share the button (as Explorer groups them after a restart).

NeoShell does the same with WinUI's drop events on the taskbar's root and on the previews' window, as Explorer's XAML
taskbar: the system then draws the drag as over Explorer's (the dragged image small, with the effect's glyph and
caption). An OLE target with `IDropTargetHelper` (as the desktop has) was tried first: it drew the source's big
image with the classic cursors instead. WinUI's events also see drags of NeoShell's own desktop icons. The dragged
items are read once on entering (`GetStorageItemsAsync`, under a deferral); a shortcut through its property store
(`ShellItems.ReadShortcut`). Pointer events don't come during a drag, so the hover plate, hover timers and the
previews' hide timer are driven from the drag events. The drag's input went to its source, so a window is brought
forward as Alt+Tab does (`TopLevelWindows.SwitchTo`): a plain `SetForegroundWindow` is refused. The gap moves the
buttons by `Translation` (150 ms, as reordering); centred, the row recentres once the app is in it. Not compared: a
centred Explorer taskbar (Explorer's here is left-aligned), a second monitor, and drags of packaged apps from Start
(Explorer pins those too, by their AppID).

### Progress, overlay badges, thumbnail toolbars

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
| `WM_USER+76` | `ThumbBarAddButtons` | window | shared memory: the buttons |
| `WM_USER+77` | `ThumbBarUpdateButtons` | window | shared memory: the buttons |
| `WM_USER+78` | `ThumbBarSetImageList` | window | shared memory: the image list |
| `WM_USER+81` | `SetThumbnailClip` | window | (ignored) |

Apps only start once told their button exists: the `TaskbarButtonCreated` registered message, sent with
`SendNotifyMessage` when a window is added to the task list (shell mode). The task button shows the first window's
progress (bar along the bottom; indeterminate, error and paused states) and overlay icon. The overlay sits where the
badge goes (below): 16 px over the icon's top-right corner, 6 px right of the icon and 7 above it (Explorer's
`OverlayIcon`, measured through UI Automation), and is hidden while the app has a badge.

**Badges** (an app's count or glyph from `BadgeUpdateManager`; `AppBadges`, `TaskBadge`, `BadgeLook`, unit tested).
Only apps with package identity can set one: for an unpackaged app with an AppUserModelID from a Start menu shortcut,
`BadgeUpdater.Update` fails with `ERROR_NOT_FOUND`. Any process may set a packaged app's badge, and Windows keeps it
whether or not the app runs, so a pinned app shows it too.

- **Reading them.** Explorer's taskbar (Taskbar.dll, `CTaskBand::UpdateBadgeAsync`) asks the undocumented Windows Runtime
  class `WindowsUdk.UI.StartScreen.BadgeProvider` (windowsudk.shellcommon.dll, in-process, base trust) for each task
  group's AppUserModelID: `GetForUser(user)` (null works: the process's user), `GetRegisteredBadge(appId)` → a `Badge`
  with `Kind` (0 none, 1 number, 2 glyph), `Number`, `Glyph` (`BadgeGlyphKind`: 1 activity, 2 alert, 3 alarm,
  4 available, 5 away, 6 busy, 7 newMessage, 8 paused, 9 playing, 10 unavailable, 11 error, 12 attention) and a
  `Changed` event. IIDs and method order are from the DLL's symbols (`Com/IBadgeProvider.cs`). The value arrives a
  moment after the first call (the provider subscribes to the notification platform's badge updates for that app).
  It works from a medium-integrity unpackaged process, alongside Explorer and as the shell. A count of 0 and
  `value="none"` show nothing.
- **Threads.** The provider raises `Changed` while holding its lock; a handler registered on the UI thread (an STA) is
  marshalled back to it, and a UI thread then calling `GetRegisteredBadge` waits on that lock forever (found the hard
  way). Explorer calls it from its task pool; `AppBadges` likewise only touches it on thread-pool threads, reads each
  change after the event returns, and the taskbar reads its cache.
- **"Show badges on taskbar apps"** is `TaskbarBadges` (DWORD) under `HKCU\…\Explorer\Advanced`, on unless 0
  (Taskbar.dll `IsTaskbarBadgingEnabled`). Settings just writes the value and Explorer follows within a moment without
  any message, so NeoShell watches the key (`RegistryWatcher`, `RegNotifyChangeKeyValue`).
- **Look** (Taskbar.View.dll `BadgeConfiguration`; sizes from UI Automation and screenshots at 96 DPI): a plate 16 px
  high, at least 16 wide, corner radius 8, its right edge 6 px right of the 24 px icon and its top 7 px above it (the
  overlay's spot), inside the icon's panel so it shrinks with the icon when pressed. Counts in Segoe UI Variable 11,
  padding 4 on each side, tight line bounds, centred: 1 → 16 wide, 10 → 19, 99 → 21, above 99 "99+" → 28. Glyphs in
  Segoe Fluent Icons 12: activity EDAB, alarm EDAC, alert EDAD, error EDAE, attention EDB1, newMessage EDB3, paused
  EDB4, playing EDB5. Colours: the plate is `SystemAccentColorLight2` with black text on a dark taskbar,
  `SystemAccentColor` with white text on a light one (accent on the taskbar or not); alert, attention and error are
  white on #D73B02; available, away, busy and unavailable are plain dots of #008117, #FFC20A, #D82128 and #999999.
  The button's help text is "Status N items" / "Status 1 item" / "Status Alert" (Explorer's `BadgeStatusText` strings).
- **Animation** (`SharedAnimations`): appearing, the plate's `Scale` goes 0 → 1.1 at a third (cubic-bezier 0.85,0,
  0.75,1) → 1 (0.35,0,0,1) over 500 ms about (8, 8), the centre of its left end; disappearing, → 0 over 167 ms
  (0,0,0,1) about its centre. A new count or glyph of the same width just swaps. A width change moves the left edge
  (the right one stays): Explorer's implicit `Offset` animation slides it there over 333 ms (0.55,0,0,1); NeoShell
  animates `Translation` from the old place, only for a badge already shown (a new one pops in where it belongs).
- Explorer once missed the badge of a UWP app whose button existed when Explorer started, until the app restarted;
  NeoShell shows it. A focus session hides Explorer's badges (T35).

**Thumbnail toolbars** (a player's previous, play/pause and next under its preview; `ThumbBarCall`, `ThumbBar`, unit
tested). The thumbnail toolbar calls carry their data in shared memory from `SHAllocShared`, its handle duplicated
into the taskbar's process: `SHLockShared` with NeoShell's own process ID maps it, and it's copied within the message
(the app frees it afterwards). Only what's mapped is read (`VirtualQuery`): the data's own counts are checked against
it, as a bad count read past the mapping would end the shell.

- Buttons: a 32-bit count (at most 7), then packed `THUMBBUTTON`s of 540 bytes: mask, ID, image index, the `HICON`
  as 32 bits (copied at once), 260 characters of tooltip, flags. Adding sets the buttons; updating changes the masked
  parts of the buttons with the same IDs.
- The image list: a 32-bit size, then what `ImageList_Write` writes (`ImageListStream`, unit tested): `ILHEAD`, the
  image strip as a BMP file (any depth, palette or `BI_BITFIELDS`; images left to right, then down), and for
  `ILC_MASK` the mask as another BMP. `ImageList_Read` would need common controls 6, which NeoShell doesn't load.
- The previews give every window a row of 32×28 buttons under its preview when one of them has a toolbar: the
  button's own icon or its image from the list (16 epx), its tooltip, disabled, hidden and non-interactive as asked.
  They follow the app's updates while open (VLC's play button turning to pause). A click posts `WM_COMMAND` with
  `THBN_CLICKED` and the ID to the window, as Explorer sends; `THBF_DISMISSONCLICK` closes the previews. A tap on a
  button doesn't count as a tap on its preview (which would switch to the window).

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
    calls in apps that support it; the endpoint's mute is the nearest without them). Explorer's privacy indicator
    doesn't show the endpoint's mute (checked on 25H2), so NeoShell's doesn't either.
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
- Win+Space switches the input method (see Input indicator): nothing answers it without Explorer (Win+Space
  stays registered, but Explorer's input switcher behind it is gone). The hook takes it (`InputSwitchKeys`, unit
  tested): each Space while Win is held is swallowed and moves on (back with Shift; not with Ctrl or Alt), the
  first opens the switcher with Win masked as for Quick Settings' keys, and letting go of Win switches. Alt+Shift
  needs no shell: Windows switches by itself.
- Win+Comma peeks at the desktop while Win is held (`PeekKeys`, unit tested, through the hook: a hotkey can't see
  Win let go of): Aero Peek at the taskbar, a window left out of peeking like the wallpaper, so only those show.
  The comma is swallowed and Win masked as for Quick Settings' keys.
- Start closed because another window took the foreground (deactivation) doesn't hand the foreground back to the
  previous app — that would take it from the window being activated, e.g. the taskbar for Win+T.
- Not done: Task View and virtual desktops (Win+Tab, Win+Ctrl+D/F4/arrows; out of scope, and the desktops live in
  Explorer), Widgets (Win+W), the Snipping Tool video (Win+Shift+R: Snipping Tool shows nothing without Explorer),
  and the clipboard history, emoji panel and voice typing (Win+V, Win+Period, Win+H), which Explorer passes to
  Windows' text input host through no public API.

### Search on the taskbar (`TaskbarSearch`, unit tested)

Settings → Personalization → Taskbar → Search: Hide, Search icon only, Search icon and label, Search box (Settings'
labels and order on 25H2). Explorer keeps it in `HKCU\Software\Microsoft\Windows\CurrentVersion\Search`,
`SearchboxTaskbarMode`: 0 hide, 1 icon, 2 box, 3 icon and label; missing or anything else shows the box. It follows a
plain write to the value at once, with no message, so NeoShell watches the key (`RegistryWatcher`; Windows Search
writes other values there constantly, so only a changed mode counts) and changes the look in place. The taskbar
menu's Search submenu writes the same value, so Explorer follows NeoShell's choice too. The former `ShowSearchButton`
setting is read once: hidden, it writes 0; then it's cleared.

Explorer's look (Taskbar.View.dll `SearchBoxButton`/`SearchBoxLaunchListButton`, `SearchItemViewModel`; measured at
96 DPI, dark, light and accent-coloured):
- **Icon** — a 44 px slot, the usual 40x40 hover plate. The icon is an animated icon (Lottie, not a glyph), 24 px:
  a ring about 19x18.5 px with a 2.5 px stroke, lighter at the top left (dark #FAFAFA → #D2D2D2, light #444 → #1F1F1F),
  round a tinted lens (dark white ~22% → 17%, light black 6% → 0), and a round-capped handle. NeoShell draws it
  (`SearchIconRingStyle`/`SearchIconHandleStyle`); it shrinks when pressed like the other icons. Tooltip "Search".
- **Box** — 220x32 at 2 px margins (a 224 px slot), corner radius 16, 1 px border. Fill: dark #25FFFFFF, hovered
  #2BFFFFFF (Explorer's own, stronger than WinUI's); light `ControlFillColorDefault`/`Secondary`; pressed
  `ControlFillColorInputActive` (it looks like a focused text box). Border: dark #4EFFFFFF on the top row and
  #2EFFFFFF elsewhere, light #0F000000 with #29000000 on the bottom row; pressed, all of it #2EFFFFFF / #0F000000.
  Inside, 10 px in: the same icon smaller (about 13 px), then 10 px on "Search" at 14 px in
  `TextFillColorSecondary` (pressed `Tertiary`). Hovered or pressed, the icon cross-fades in 133 ms to a slightly
  larger one in Windows Search's colours: a ring from green (#58DC7E, top right) through teal to blue (#0078D3,
  bottom left) and a blue handle. Fills change over 133 ms (Explorer's press is instant). Tooltip "Search" at the
  pointer.
- **Icon and label** — a 106 px slot as tall as the taskbar holding a 100x32 pill (radius 16):
  `ControlFillColorDefault`, hovered `Secondary`, pressed `Tertiary`, over 150 ms; border dark #18FFFFFF on the top
  row and #12FFFFFF elsewhere, light #05000000 / #0B000000. Centred in it, the bold magnifier U+F78B (Segoe Fluent
  Icons, 16 px, a pixel below the label's centre) and "Search" at 12 px, no gap, `TextFillColorPrimary`; pressed
  both turn `Secondary` and the icon shrinks. No tooltip.
- **Full taskbar** — the box and the label collapse to the icon (`CanCollapse`; `UpdateEffectiveSearchMode` falls
  back to 1 when there's no room, `IsSpaceAvailableForSearchBox`) and expand again when there's room. Explorer first
  narrows labelled buttons to their minimum (~98 px), then collapses the search, then shrinks icons; NeoShell's
  buttons don't narrow, so it collapses the search as soon as the buttons don't fit beside it
  (`TaskbarSearch.Shown`).
- **Search highlights** (the "gleam" picture at the box's end, `IsDynamicSearchBoxEnabled`) are Bing content served
  to Windows Search through no public API, and are off on this VM; NeoShell doesn't show them.
- Centred, the slot sits between Start and the task buttons as left-aligned (the centred group lands 1 px left of
  Explorer's in every mode: Explorer's centred Start slot is 45 px). NeoShell switches looks without animating.
- A click opens Start with its search box focused, as before, in both run modes; Explorer opens its
  own Search window instead (T21: Start's search stays).

### Taskbar context menu

Task Manager, the taskbar settings toggles (alignment, search, combine, backdrop, auto-hide, hidden icon menu, all displays),
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
  - `dwData == 0`: `SHAppBarMessage` from other apps' app bars (see App bars below).
  - `NOTIFYICONDATA` parsing is done from a byte buffer and handles both 32- and 64-bit callers
    (HWND/HICON fields are 32-bit handles in both, sign-extended) and the V1/V2/V3/current sizes. Unit tested.
- Icons are keyed by (`hWnd`, `uID`) or `guidItem` (`TrayIconStore`, unit tested): adding an existing icon or
  changing a missing one fails, as in Explorer; only flagged fields change. `NIS_HIDDEN` is honoured. Icon pixels
  are copied when they arrive, as the app may destroy its HICON. Windows' own volume, network, power, microphone and
  Meet Now icons (see Shell service objects) are kept but not shown, as in Explorer. Tooltips from `szTip` (version
  4 icons without `NIF_SHOWTIP` get `NIN_POPUPOPEN`/`NIN_POPUPCLOSE` instead). Balloon notifications become toasts
  (below).
- **Balloon notifications** (`NIF_INFO`; `TrayBalloon`, `NotificationArea`, Interop `BalloonIcons`,
  `NotifyIconSettings`). How Explorer does it (Windows 11 25H2, `Taskbar.dll`: `NotificationAreaIconManager2::ModifyIcon`
  → `NotificationAreaIcon2::ShowBalloon` → `BalloonToast2::SendAsync`, read with symbols and Ghidra):
  - An empty `szInfo` takes the icon's balloon away (`HideBalloon`), as does deleting the icon; the app hears nothing.
    Otherwise `NIN_BALLOONSHOW` goes to the app at once, before the toast is even posted, and a new balloon from the
    same icon replaces its last without a message for the old one. `uTimeout`, `NIF_REALTIME`,
    `NIIF_RESPECT_QUIET_TIME` and `NIIF_LARGE_ICON` are ignored. (`NIIF_USER` with an `hBalloonIcon` of the wrong
    size is refused by shell32 itself: `Shell_NotifyIcon` returns FALSE and the tray never sees it.)
  - It posts a real toast through `ToastNotificationManager`: `<toast bannerOnly="true">` (a banner only: it never
    stays in the notification center) in the legacy template `ToastImageAndText02` (`…01` without a title,
    `ToastText02`/`…01` without a picture); the title (if 1–127 characters) is the first text, the text the second
    (or the first, without a title); `NIIF_NOSOUND` adds `<audio silent="true"/>`. The picture, written to
    `%TEMP%\{guid}.png`: `NIIF_INFO`/`WARNING`/`ERROR` → the stock icons `SIID_INFO`/`SIID_WARNING`/`SIID_ERROR`
    (79/78/80), `NIIF_USER` → `hBalloonIcon`, else the tray icon, each reloaded large from its file
    (`GetIconInfoEx` module and resource, `SHDefExtractIcon` at 256, 196, 128, 64, 48, 32, 24 or 16, the first
    that loads); `NIIF_NONE` has none.
  - The app: the AppUserModelID of the icon's window when `IApplicationResolver::GetAppIDForWindow` says it is
    explicit (and not a system app); otherwise `NotifyIconGeneratedAumid_<ID>`, `<ID>` being the icon's key in
    `HKCU\Control Panel\NotifyIconSettings` (a random 64-bit number Explorer gives each icon it sees, with its
    `ExecutablePath` and `UID` or `IconGuid`). For that one it registers `HKCU\Software\Classes\AppUserModelId\<AUMID>`
    (volatile) with `DisplayName` = the executable's file description (else its file name) and `IconUri` = the tray
    icon as `%TEMP%\<AUMID>.png`. The notification settings of that AUMID apply (Settings lists it under that name).
    Seen on this VM: the toast's header still shows the raw `NotifyIconGeneratedAumid_7884…` and no logo.
  - The toast's events become messages: `Activated` → `NIN_BALLOONUSERCLICK`; `Dismissed` (timed out, or the
    close button) → `NIN_BALLOONTIMEOUT`, often twice (a time-out raises both `TimedOut` and `UserCanceled`);
    `NIN_BALLOONHIDE` only when the platform hides it. With Do not disturb on the platform drops the banner:
    `NIN_BALLOONSHOW`, then `NIN_BALLOONTIMEOUT` ~65 ms later. Version 4 icons get `wParam` 0 (no anchor) and the
    message and ID in `lParam`; older ones `wParam` = ID, `lParam` = message.
  - NeoShell (shell mode) shows them itself through `ToastPopups`, as any toast (same window, place, stacking,
    timing, hover and slide), with the picture 40 epx large in a 48 epx place beside the text, which centres on it
    (measured on Explorer's: text 80 epx from the left, toast 108 high), and sends the same messages (one
    `NIN_BALLOONTIMEOUT`). It doesn't post them to the notification platform: nothing but NeoShell would show them,
    and `UserNotificationListener` can't describe Explorer's generated apps (`AppInfo` throws "not implemented"; the
    reader skips such notifications). It finds the same app as Explorer (`TrayBalloon.AppIdFor`, unit tested) from
    the window's own AppUserModelID or packaged app (not one set process-wide with
    `SetCurrentProcessExplicitAppUserModelID`, which only the private resolver sees), else Explorer's
    `NotifyIconSettings` key (see Hidden icons below; an icon without one gets the executable's implicit AppID), so the
    user's settings for it apply: no toast with Do not disturb on, banners off for all apps, or the app's
    notifications or banners off; it then times out at once as Explorer's. Names and logo as Explorer registers them
    (file description, tray icon) rather than the raw AUMID Explorer's header shows. A balloon plays the default
    notification sound (see Toast sound in Notifications and calendar), none with `NIIF_NOSOUND`.
- After `Shell_TrayWnd` exists, broadcast `RegisterWindowMessage("TaskbarCreated")` so running apps re-add icons.
- Remove icons whose owner window has died (`IsWindow` every 5 s and before forwarding input).
- **Mouse forwarding** with `NOTIFYICON_VERSION_4` semantics: `wParam` = anchor point (x, y), `lParam` low word =
  message (`WM_LBUTTONUP`, `NIN_SELECT`, `WM_CONTEXTMENU`, `NIN_POPUPOPEN`…), high word = icon ID; older versions
  get `wParam = uID`, `lParam = mouse message` (version 3 also gets `NIN_SELECT`/`WM_CONTEXTMENU`). Clicks call
  `AllowSetForegroundWindow` for the owner process first. A second press within the double-click time becomes
  `WM_LBUTTONDBLCLK`. Unit tested.
- **Hidden icons** (`NotificationArea`, `TrayIconOrder`, Interop `NotifyIconSettings`; primary taskbar only). Each
  icon is on the taskbar ("promoted") or behind the chevron, one by one, as in Explorer (Windows 11 25H2,
  Taskbar.dll's `NotifyIconSettingsDatabase` / `NotificationAreaIconManager2` and SystemTray.dll's `DragDropManager`
  / `ChevronSystemTrayIconDataModel2`, read with symbols and Ghidra, and watched live with test icons):
  - Explorer's record: `HKCU\Control Panel\NotifyIconSettings` (`Version` 3; below that, or missing, Explorer deletes
    the whole key on start), a key per icon named by a random 64-bit number (`GetRandom64BitInteger`, a
    `mt19937_64`). It's created the first time the shell sees an icon (`AddIcon` →
    `TryGetSettingsForExistingIcon`, else `CreateDefaultSettingsForNewIcon`): `UID`, `ExecutablePath` and
    `InitialTooltip` for an icon without a GUID; `IconGuid` (`{…}`, upper case), `ExecutablePath` (and `Publisher`,
    from the signature) for one with. No `IsPromoted`: **a new icon starts behind the chevron**. Its ID goes first in
    the root's `UIOrderList` (REG_BINARY, the IDs as little-endian 64-bit numbers), one order for all icons, those
    on the taskbar and those in the overflow alike; each row shows its own in that order, an ID missing from it
    counting as first (`GetUIOrderForIcon` returns 0). `IconSnapshot` (a PNG of the first icon) is written once by
    `SetIcon`, for Settings' list. A key matches an icon by executable and GUID, else executable and `UID`.
    `ExecutablePath` starts with a known folder's GUID for System, SystemX86, Windows and Program Files (x64, x86)
    — `{6D809377-…}\VideoLAN\VLC\vlc.exe` — and is a plain path elsewhere (OneDrive under AppData, test apps on E:).
    Windows' own system icons (volume, network…; see Shell service objects) get no key.
  - `IsPromoted` (DWORD 1/0) shows an icon on the taskbar. Settings > Personalization > Taskbar > "Other system tray
    icons" lists the keys with a switch each and writes it; each icon watches its own key (a wil registry watcher,
    `OnRegistrySettingsChanged` → `Promoted`), so a change made there (or anywhere) shows at once.
  - "Hidden icon menu" (same page): `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\
    TrayNotify\SystemTrayChevronVisibility`, on unless 0, watched live. Off, the chevron goes and the icons behind it
    show nowhere (not on the taskbar). The chevron also goes while the overflow has no icons, and an open overflow
    closes when its last icon leaves. Glyph by taskbar edge: `E974`/`E972`/`E973`/`E971` (left, top, right, bottom);
    on a bottom taskbar ChevronUpMed `E971` at 16 epx, 10 by 6 pixels lit at 100 %.
  - Dragging: a press on an icon (taskbar or overflow) that moves past the drag threshold starts a drag. The icon
    stays in its place at about 30 % opacity; a copy, 16 epx, follows with its bottom-right corner at the pointer;
    above it, centred, 12 px up, a 30 by 30 caption (rounded 4, about `#2F2F2F` in the dark theme, a 16 px glyph):
    a pin (`E718`) over the taskbar's row for an icon from the overflow, an unpin (`E77A`) over the overflow or the
    chevron for one from the taskbar, none within its own row, and ⊘ ("can't drop here") anywhere else, the gaps
    between icons and the empty part of the overflow included. On an icon, a 1 by 36 epx line
    (`ControlStrongStrokeColorDefault`, about 55 % white), vertically centred, marks where it goes: just left of
    that icon over its left half (`x <= middle`), at its right edge over its right half. Hit testing is by screen
    rectangles of the icons (the overflow's only while it's open), then the chevron's.
  - Dropping (`HandleNotifyIconDragDrop` → `NotificationAreaIconManager2::MoveIcon`): on an icon, the dragged one is
    promoted or not as that row is, and moved before or after it in `UIOrderList` (`NotifyIconSettingsDatabase::
    MoveIcon`: taken out, put back next to the target, or first if the target isn't in the list); into an empty row
    the order stays. On the chevron (overflow open or not): demoted and put first in the overflow (before its first
    icon). On itself or anywhere else: nothing. The moved icon grows into its place from nothing in about 4 frames;
    the others jump. The overflow stays open throughout, also when the drag starts on the taskbar (a press there
    doesn't dismiss it), and grows or shrinks (upwards) by rows as icons come and go. The app hears no click.
    Explorer also moves a focused icon with the keyboard (`MoveIconLeftOrRight`); NeoShell's tray icons don't take
    the keyboard.
  - The overflow: 5 icons a row, 40 by 40 cells, 4 from the edge; newest-first in its order; it opens centred over
    the chevron and doesn't follow it while open.
  - NeoShell as the shell does all this itself, with the same keys and values, so Explorer and Settings see the same
    state after it and the other way round, and follows changes to them while running (`RegistryWatcher` on the
    subtree, and on `TrayNotify`). It skips `Publisher` and `IconSnapshot` (Explorer fills them in when it next sees
    the icon). Its own process's icons (the shell service objects: Safely Remove Hardware…) are filed under
    `%windir%\explorer.exe`, as Explorer files them. Settings can't open in shell mode, so the taskbar menu has
    "Show hidden icon menu"; promoting is by dragging, as in Explorer. The drag picture and the marker are windowed
    popups of the taskbar; the overflow's icons are hit tested from its popup window's real place (TaskbarFlyouts
    moves that window behind WinUI's back). A release over another window reaches the icon only as
    `PointerCaptureLost`, which is then the drop; a press on a taskbar icon cancels the overflow's light dismiss.
  - NeoShell's former `TrayMode` setting (`ShowAll`: every icon on the taskbar) is read once as the shell and cleared:
    `ShowAll` promotes every icon Explorer knows and those added in that session.
- Alongside Explorer: Explorer owns `Shell_TrayWnd`, so NeoShell shows no tray. Fully supported as the shell.

### App bars (`Tray/AppBars.cs`, `Tray/AppBarLayout.cs`, Interop `Tray/AppBarMessage.cs`)

As the shell NeoShell serves other apps' `SHAppBarMessage` calls (docks, launcher bars, apps asking where the
taskbar is) as Explorer does. Found with cdb on shell32 (System32 and SysWOW64) and Ghidra with symbols on
explorer.exe 26200 (`CTray::_OnAppBarMessage`, `_AppBarQueryPos`, `_AppBarSetPos`, `_AppBarSubtractRect(s)`,
`_AppBarOutsideOf`, `StuckAppChange`, `WorkAreaMayHaveChanged`, `AppBarNotifyAll`, `AppBarSetAutoHideBar`,
`OnRudeWindowStateChange`), then compared live with a test bar (64-bit and SysWOW64 PowerShell WinForms) running
the same steps under Explorer and under NeoShell: every reply, rectangle, work area and maximized window matched.

- **Wire format.** shell32 finds `Shell_TrayWnd` and sends `WM_COPYDATA` with `dwData` 0 and 0x40 bytes, the same
  from 32- and 64-bit callers: `APPBARDATA3264` (`cbSize` = 0x28, `hWnd` as 32 bits, `uCallbackMessage`, `uEdge`,
  `rc`, `lParam` as 64 bits), `dwMessage` at 0x28, the shared memory handle at 0x30 (64 bits) and at 0x38 the id of
  the process that handle belongs to (the shell's: shell32 reads it from `Shell_TrayWnd`). A 32-bit caller
  sign-extends `lParam` and the handle and leaves the padding at 0x2C/0x3C uninitialized. For `ABM_QUERYPOS`,
  `ABM_SETPOS` and `ABM_GETTASKBARPOS` shell32 copies the `APPBARDATA3264` into `SHAllocShared` memory for the
  shell; the shell writes `rc` (and `uEdge` for the taskbar position) there with `SHLockShared`, and shell32
  copies it back to the caller's `APPBARDATA` and frees it. The reply is `SendMessage`'s result. shell32 itself
  rejects `cbSize` over 0x30, and calls `ChangeWindowMessageFilterEx` for the callback message on `ABM_NEW`/`REMOVE`.
- **Replies.** `NEW` 1, or 0 for a window already registered; `REMOVE`, `QUERYPOS`, `SETPOS` (even for an
  unregistered bar: its rectangle comes back unchanged), `GETTASKBARPOS`, `ACTIVATE`, `WINDOWPOSCHANGED` and
  `SETSTATE` 1; `GETSTATE` 1 (`ABS_AUTOHIDE`) when the taskbar hides itself, else 0 (never `ABS_ALWAYSONTOP`);
  messages above 12 return 0. `SETSTATE` is accepted but changes nothing in Explorer 26200 (checked live: auto-hide
  stays off), so NeoShell ignores it too. `GETTASKBARPOS` answers the primary taskbar's shown rectangle and
  `ABE_BOTTOM`, also when it's auto-hidden.
- **Placing (`QUERYPOS`).** The proposed rectangle's monitor (`MONITOR_DEFAULTTOPRIMARY`) counts. The bar stays
  clear of the taskbar there (unless it auto-hides), then of the other bars on that monitor that come first: top
  and bottom bars always take precedence over left and right ones; otherwise only bars on the edge asked for count:
  those already outside the bar when it stays on its edge (`_AppBarOutsideOf`, equal counts as outside), all of
  them when it moves to that edge. Only the facing side is clipped (`left = max(left, other.right)` and so on), so
  a rectangle can come out empty or crossed (left past right), and one hanging off the screen is left as it is.
  NeoShell's widget sidebar takes part as a bar on the right. `SETPOS` queries, stores the result and its edge
  (both only if the rectangle changed), and the monitor's work area is worked out again: the monitor less every
  placed bar on it, and less the taskbar and the sidebar (`AppBarLayout.FreeArea`, unit tested). The sidebar keeps
  to the work area's height, so it starts below a top bar.
- **Notifications** (posted with the bar's callback message): `ABN_POSCHANGED` to the other bars on the monitors a
  bar left and joined when one moves or is removed, and to every bar whenever a work area is set and broadcast
  (`WM_SETTINGCHANGE` with `SPI_SETWORKAREA`, from NeoShell (taskbar, sidebar, bars) or any app). Explorer sends
  that second one twice per broadcast (measured: other bars get 3 per move, the mover 2); NeoShell sends it once
  (other bars 2, the mover 1). `ABN_STATECHANGE` to every bar when auto-hide is switched; `ABN_FULLSCREENAPP` (1/0)
  to the bars on a monitor when a full-screen app comes to or leaves it, once per change per bar (Explorer
  remembers what it told each; nothing on `ABM_NEW`). NeoShell's full-screen test is the taskbar's (the foreground
  window covering its monitor, or marked with `MarkFullscreenWindow`); Explorer's rude-window manager also counted
  a covering window that wasn't in front. `ABN_WINDOWARRANGE` goes out around Cascade/Tile, which Windows 11's
  taskbar menu no longer has, so never.
- **Gone bars.** As in Explorer, a bar is dropped (and its space given back) when its thread ends
  (`WindowThread`: a thread-pool wait on the thread handle), and when a notification finds its window gone.
- **Auto-hide bars** (`GET/SETAUTOHIDEBAR(EX)`): one per edge and monitor; `SET` fails when another live bar has
  the edge, `SET` off clears the edge whoever has it. The plain messages use the taskbar's monitor, the `EX` ones
  `MonitorFromRect(rc, MONITOR_DEFAULTTONEAREST)`. Like Explorer, NeoShell also publishes them as properties of
  `Shell_TrayWnd`: `LastAutoHideBarStuckMonitor` (the taskbar's monitor) and `WindowOnEdge:%08x:%1u` (monitor,
  edge) = the bar, or 1 for none. shell32 answers `GETAUTOHIDEBAR(EX)` from them without asking and only sends the
  message when there's none. Explorer's quirk is kept: a bar setting itself again publishes 1, so shell32 then
  answers 0 for that edge. `ACTIVATE`/`WINDOWPOSCHANGED` from a bar lift the auto-hide bar on its edge to the top
  of its band (`SetWindowPos(HWND_TOP)`, no activation; not compared live).
- Alongside Explorer nothing changes: Explorer owns `Shell_TrayWnd` and serves app bars, NeoShell's own included.

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

### Privacy indicator (Interop `Privacy/CapabilityUsage`)

Windows 11 25H2's taskbar has one privacy button for the microphone and the location (SystemTray.dll,
`PrivacySystemTrayIconDataModel`; no camera: SystemTray asks only about "microphone" and "location", and has no camera
strings). Explorer learns who uses them from the capability access manager, not from audio sessions:
`WindowsUdk.Security.Authorization.AppCapabilityAccess.CapabilityUsageInfo` (windowsudk.shellcommon.dll, in-process,
activatable by an unpackaged full-trust app; it checks the client for the `shellExperience` capability, which a
non-AppContainer process passes). It wraps camsvc's `Windows.Internal.CapabilityAccess.Management.CapabilityUsage` and
its WNF state, so it covers packaged and unpackaged apps (the same records as the consent store's
`LastUsedTimeStart/Stop`), and for the microphone also the voice assistant (`WNF_AUDC_CAPTURE`). No metadata; from the
symbols: factory `ICapabilityUsageInfoFactory` `{2135ec12-5eb8-5f7b-89a3-dbe27b6cebc7}` `CreateInstance(HSTRING
capability)`; `ICapabilityUsageInfo` `{d494ab35-5e4a-5533-a0a6-ac684a2feea2}`: `IsAnyAppUsingCapability`,
`GetDisplayNamesForAppsUsingCapability` (`IVectorView<String>`), `UsageChanged` (`TypedEventHandler` `{48a0f8bb-b057-
5aae-8439-e917492e4e88}`). `ICapabilityUsageInfo2.GetMultiLineDescriptionOfUsage`, which Explorer's tooltip uses, is the
same names joined with line feeds. NeoShell's `CapabilityUsage` creates one per capability and reads the names on the
thread pool after each `UsageChanged` (it comes on a WNF thread), as `AppBadges` does.

- Measured on this VM (a waveIn recorder for the microphone, a `Geolocator` app for the location, alongside and in
  shell mode): Explorer shows and hides the button as the usage starts and stops, in the same 150 ms poll as a client
  of the API, with no delay of its own. The microphone part replaced NeoShell's Core Audio session watch
  (`CaptureMonitor`): Explorer's tooltip names ("rec") are the API's, not the processes', and the two disagreed.
- Button: 32 wide, before the input indicator; UIA name "Privacy" in Explorer. Glyph (Segoe Fluent Icons, 16 px) in
  the accent's text colour (`AccentTextFillColorPrimaryBrush`, (156,235,255) with this VM's blue accent on the dark
  taskbar): U+E720 microphone, U+E37A location arrow, U+F47F both in one glyph (pixel-identical to Explorer's).
- Tooltip: "Location in use by:" then each app on its own line, a blank line, then "Microphone in use by:" and its
  apps; two apps of the same name are both listed. Not affected by the microphone's mute.
- Click: the glyph decides, as in Explorer's `OnIconClicked`: `ms-settings:privacy-microphone`,
  `ms-settings:privacy-location`, or `ms-settings:privacy` for both. As the shell: Sound's Recording tab
  (`mmsys.cpl,,1`) for the microphone, otherwise Control Panel (no privacy or location pages there).
- Right-click: a menu with a settings gear (U+E713) on each item, "Microphone privacy settings" then "Location privacy
  settings", for what's in use. Like all of Explorer's tray menus (network and volume included) it opens right-aligned
  at the tray's right edge, 12 px from the screen's, not above the icon.
- Explorer hides the microphone part while its call-mute microphone button (`MicrophoneSystemTrayIconDataModel`, apps
  that support muting calls) is showing; NeoShell has no such button, so it always shows it. The Recall indicator
  (`RecallPrivacyIndicatorExtension`) isn't on this VM.

### Input indicator (`Tray/InputSwitchPanel`, Interop `Input/InputMethods`)

Explorer's taskbar shows the input method of the app in front while more than one is enabled, and opens a switcher
on a click. It asks Windows' input switcher for both: `InputSwitch.dll`'s `CInputSwitchControl` (CLSID
`{B9BC2A50-43C3-41AA-A086-5DB14E184BAE}`, `IInputSwitchControl` `…A082…`, callback `IInputSwitchCallback` `…A083…`),
created by windowsudk.shellcommon's `InputMethodConversionIndicator` with `Init(7)` (client type DESKTOP_XAML; 0
DESKTOP, 1 TOUCHKEYBOARD, 2 LOGONUI, 3 UAC, 4 SETTINGSPANE, 5 OOBE, 6 OTHER), and SystemTray.dll's
`LanguageSystemTrayIconDataModel` / `ImeSystemTrayIconDataModel` for the two buttons. The switcher follows the
foreground window (per-window input methods or not) and switches for it. NeoShell creates the same control:
- State: `GetProfileCount`, `GetCurrentProfile` (a 0x70-byte struct of `CoTaskMem` strings: HKL; "ENG"; "English
  (United Kingdom)"; "NO"; "Norwegian keyboard"; a text-service flag at 0x2C; "en-GB"; the icon file at 0x60) and
  `GetCurrentImeModeItem` (tooltip "Right-click to open IME options", HICON, the mode as a Segoe Fluent Icons glyph
  at 0x18: U+E986 あ, U+E97E A). `OnUpdateProfile`, `OnImeModeItemUpdate`, `OnProfileCountChange` and
  `OnContextFlagsChange` come on the creating (UI) thread; `Indicators` reads the state again.
- Switching: `ActivateInputProfile(tip)` with the language list's tip ("0809:00000414", "0411:{clsid}{profile}").
  It's scheduled, not immediate: Windows switches the app holding the focus once the shell's popup has given it
  back. NeoShell's switcher takes the focus while open, so the switch is made from its `Closed` (switching while it
  was still open lost the switch whenever the focus had moved inside it).
- The list: the text services framework's enabled profiles (`ITfInputProcessorProfileMgr.EnumProfiles(0)`,
  `TF_IPP_FLAG_ENABLED`), in the user's order; language name from `Windows.Globalization.Language`, letters from
  the language's ISO 639-2 code ("ENG", "JPN"), keyboard from the layout's "Layout Display Name" ("Norwegian",
  "US") or the text service's description ("Microsoft IME"). A layout variant's HKL (0xFnnn device word) maps to
  its ID through the registry's "Layout Id".
- `ShowInputSwitch` (Explorer's flyout) and the IME's right-click menu fail with E_ACCESSDENIED in any process but
  Explorer, as the shell too: InputSwitch creates them in a window band (`CreateWindowInBand`) and XAML island
  only Explorer may have (client types 2, 3 and 5 show the old Windows 10 list, square and accent-filled). So
  NeoShell draws the switcher itself; the IME's menu isn't offered (a right-click does nothing, not the taskbar's
  menu).

The indicator, as Explorer's (measured side by side at 100%):
- A 44-wide flat button left of Quick Settings' with no gap to the tray icons; its hover plate 44 by 40. A keyboard
  layout shows the language's letters over the keyboard's ("ENG" / "NO", 12 px, lines 16 apart, centred, the
  first cap 12 below the plate's top). A text service shows its glyph instead (16 px, Segoe Fluent Icons):
  windowsudk.shellcommon holds a table of profiles and glyphs; Japanese MS-IME's Ⓙ U+E614 was seen, the others
  (拼 Pinyin, 五 Wubi, 行 Array, ㄅ Bopomofo, 倉 ChangJie, 易 DaYi, 速 Quick, 한 Korean, 옛 Old Hangul) are paired
  by meaning. Others show the switcher's letters ("日本") alone.
- Tooltip: language, keyboard (the list's name), a blank line, "To switch input methods, press Windows key +
  space." While the switcher is open the button keeps its hover plate.
- An IME with a mode adds a 32-wide button left of it (no gap) with the mode glyph (16 px); a click is passed on
  (`ClickImeModeItem(0, pointer, button rect)`: action 0 click, 1 right-click) and the IME switches あ/A.

The switcher (`InputSwitchPanel` in a flyout at the screen's right edge, as Quick Settings, 12 above the taskbar):
- 360 wide outside its border. Header 42: "Keyboard layout" (14 px, 16 from the left, cap 16 below the top) and
  Win+Spacebar as two 16-high keycaps (tertiary text and stroke, 11 px). Header and list are a shade lighter than
  the acrylic (`LayerOnAcrylicFillColorDefault`); the footer is the acrylic itself under a darker line (#1A000000):
  "More keyboard settings" (12 px, secondary) in a 48-high flat button (Language & region; as the shell Text
  Services and Input Languages, `control input.dll,,{C07337D3-DB2C-4D0B-9A93-B722A6C106E2}`).
- Items are WinUI's list items (plate inset 4 by 2, the selection pill): 59 high with letters, 53 with a glyph
  (Explorer's extra 6 is above the text); letters 13 and text 45 from the plate's left, the title 14 px and the
  keyboard 12 px secondary beneath, the letters on the title's baseline. The one in front is chosen; a click
  switches and closes.
- Win+Space shows the list alone (with the line above the missing footer), the next input method chosen, and no
  focus rectangle; the indicator keeps its plate.
- Explorer's switcher slides in faster than its other flyouts, about 67 ms (58 %, 90 %, 99.5 % in successive
  frames), and out in about 100 ms. A NeoShell popup reaches the screen some 40 ms after it starts sliding, so it
  slides in over 100 ms for the same look (`TaskbarFlyouts.ShowAtRight` takes the durations).

### Clock and notification bell (`Taskbar/Clock`, `ClockSettings`, `ClockDisplay`, unit tested)

Explorer's clock area is SystemTray.dll (Client.Core): `ClockSystemTrayIconDataModel2` for the clock,
`NotificationBadgeSystemTrayIconDataModel` for the bell, read with Ghidra and checked live (Windows 11 25H2).

- **Settings**, all under `HKCU\…\Explorer\Advanced`, followed live (Explorer watches the key; NeoShell's `Taskbars`
  has a `RegistryWatcher` on it and on the additional clocks):
  - `ShowSecondsInSystemClock` (Settings → Time & language → Date & time → "Show seconds in system tray clock"),
    off unless set.
  - `ShowSystrayDateTimeValueName` ("Show time and date in the System tray"), on unless 0. Off, the time and date
    go; the bell stays if it's shown, alone in a 30 px box; with neither, nothing is left.
  - `ShowNotificationIcon` (Settings' "Show notification bell icon"), off unless set.
  - Additional clocks (`HKCU\Control Panel\TimeDate\AdditionalClocks\1` and `\2`: `Enable`, `DisplayName`,
    `TzRegKeyName`) add a line each to the tooltip.
  - `ShowShortenedDateTime` ("Show abbreviated time and date") is behind a feature flag (`SystrayPBDT`) that's off on
    this build: no such option in Settings and the value does nothing, so NeoShell ignores it. "Show time in
    Notification Centre" (Date & time) is the notification center's, not the clock's; not done. Group policy
    `DisableNotificationCenter` (hides the bell) isn't followed.
- **Time**: `GetTimeFormatEx` with the user's own format, which is Region's *long* time format with
  `TIME_NOSECONDS` unless seconds are on (Interop `RegionalTime`), not .NET's short time pattern. When the user
  locale's `LOCALE_SSCRIPTS` is exactly "Latn;", every ':' becomes U+2236 (ratio), which sits centred between the
  digits. The date is the short date, the tooltip "long date, blank line, `ddd time (Local time)`", then
  `ddd time (name)` per additional clock, with seconds when the clock has them; the UIA name is "Clock", the time
  (with colons) and the date. Digits are tabular (the "1" has a
  foot), so the seconds don't make the text wobble and the date is 2 px wider than with proportional digits.
- **Ticking**: one timer per update, re-armed each time: with seconds 1000 − ms after the second, otherwise at the
  next minute. Recorded at 60 fps side by side, NeoShell's second ticks land within 1–3 frames of Explorer's.
- **Layout** (96 DPI, measured with UI Automation and screenshots): one hover box, 40 px tall, around both parts,
  3 px wider than them on either side and ending a pixel short of Show desktop (76 px wide for "07/10/2026" without
  the bell, 104 with it); the text 4 px in from the clock part's left and 2 from its right, right-aligned, a pixel
  above the middle (time ink 12–19 px, date 28–37 px below the taskbar's top); the bell 24 px wide, 4 px after the
  clock part, its 16 px glyph centred. One button in NeoShell (Explorer has two, the clock and the bell, sharing
  the hover box); each part has its own tooltip, and both open the notification center and calendar. Explorer's
  system XAML rounds text widths up where WinUI rounds them to nearest, so the box can be a pixel narrower and the
  time a pixel to the right.
- **Bell** (`ClockDisplay.Bell`): shown when `ShowNotificationIcon` is on, and always while Do not disturb is. Glyphs:
  U+F2A3 (outline) with no new notifications, U+F2A5 (filled) in the accent's text colour with some (Light 3 in dark,
  Dark 2 in light: `AccentTextFillColorPrimaryBrush`); with Do not disturb U+F285 / U+F2A8, in the text colour. No
  animation: the glyph swaps in one frame. Tooltip (and UIA name after "Notifications"): "No new notifications",
  "1 new notification", "N new notifications", plus " (Do not disturb on)".
- **New notifications** are the notification platform's count, not NeoShell's: WpnUserService's
  `IndicatorController` (NotificationController.dll) counts notifications that came since a notification center
  was last open and publishes it in the WNF state `WNF_SHEL_NOTIFICATIONS` (`0x0D83063EA3BC1035`, a DWORD), which
  Explorer's bell subscribes to. Opening or closing a notification center calls `INotificationController::
  SetNocenterStatus` (Explorer passes 1 on open, 0 on close, traced with cdb in WpnUserService); any change marks
  every notification seen and the count drops to 0. NeoShell reads the state with the notifications once a second
  (`NewNotifications.ReadCount`) and calls `SetNocenterStatus` as its flyout opens and closes, so either shell's
  notification center clears both bells. The platform keeps counting without Explorer (checked as the shell).
- **Menu**: right-click opens Explorer's clock menu, at the right as the other tray menus: "Adjust date and time"
  (as the shell `timedate.cpl`) and "Notifications settings", each with the settings gear.

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
  The listener gives each one's app (AppUserModelID and name), time and texts, not the toast's XML (its sound,
  arguments, images, buttons): a click is carried out by the notification platform itself (see Toast activation),
  and the sound comes from the app's toast history (see Toast sound). App icons as the taskbar's (packaged logo, else the `shell:AppsFolder` item's icon).
  Per-app settings from `HKCU\...\Notifications\Settings\<AppUserModelID>` (`Enabled`, `ShowBanner`,
  `ShowInActionCenter`) and the global `PushNotifications\ToastEnabled` are honoured. "Turn off all notifications
  for <app>" writes `Enabled = 0` there, Settings' own store; the platform only notices it later (Settings tells it
  through a private channel), so NeoShell hides that app's notifications itself meanwhile.
- **Notification center** (`NotificationPanel`): "Notifications" with Do not disturb and Clear all; groups by app,
  the app with the newest notification first (`NotificationDisplay.Group`, unit tested). A group shows its newest
  notification with "+N notifications"; expanded, all of them and "See fewer". Cards show the time (with the date
  for older days), the title (2 lines) and the body (1 line, with Explorer's chevron to show all of a trimmed one),
  and "…" (turn off the app, notification settings) and Clear under the pointer; the group header has the same.
  "No new notifications" when there are none.
- **Do not disturb** (Interop `DoNotDisturb`): no public API; it's the notification platform's quiet hours profile,
  switched through the undocumented `IQuietHoursSettings` (CLSID `f53321fa-…`), as Explorer's bell button does:
  `Microsoft.QuietHoursProfile.PriorityOnly` is on, `…Unrestricted` off. Read with the notifications, so a change
  made elsewhere shows within a second; the clock shows the bell while it's on, and no toasts pop up.
- **Calendar** (`CalendarPanel`): today's long date without the year (`NotificationDisplay.DayHeading`, unit tested
  for several cultures) and a button folding the month away (remembered in settings); a `CalendarView` restyled as
  Explorer's (no borders or backgrounds, other months' days dimmed, today in the accent circle), starting the week
  on Windows' regional first day rather than the display language's; the footer with the focus length (−/+: 5
  minutes at a time to 30, then 15, between 5 and 240; remembered) and Focus.
- **Focus** (`FocusSession`): Windows' own focus sessions (`Windows.UI.Shell.FocusSessionManager`) are a limited
  access feature only Microsoft's apps can unlock ("Access is denied"), so NeoShell runs its own: Do not disturb for
  the chosen time with a countdown and Stop focus in the footer, then Do not disturb as it was before (also on exit).
  Unlike Windows', it doesn't hide taskbar badges or flashing, and there's no chime at the end.
- **Toasts** (`ToastPopups`, shell mode only): without Explorer no toasts show at all — they belong to the
  ShellExperienceHost that Explorer runs — though the notifications are still stored. Each notification that
  arrives while NeoShell runs (not those already there at start, not while Do not disturb is on or the flyout is
  open) shows as Explorer's: its own acrylic window, 364 epx wide, 16 epx from the screen's right edge and 12 above
  the taskbar, with the app's icon and name, "…" and close, the title and up to three lines of body. It slides in
  from the edge; the newest is lowest and older ones move up, three at most. It leaves after the system's "Dismiss
  notifications after" time (`SPI_GETMESSAGEDURATION`, 5 s by default), later while the pointer is on it. Closing
  it only puts it away (it stays in the notification center, as in Explorer); clicking it activates the app (see
  Toast activation), and it plays the toast's sound (see Toast sound). Alongside Explorer, Explorer shows toasts.
  Tray icons' balloons show the same way (see System tray), with a picture beside the text, and are never stored.
- **Toast activation** (`NotificationCenter.Activate`, Interop `UserNotifications.Activate`). Explorer's toast host
  (ShellExperienceHost's `Windows.UI.ActionCenter.dll`) doesn't start apps itself: it hands the click to the
  notification platform's controller in WpnUserService (`NotificationController.dll`, `CLSID_MainController`
  `1ffe4ffd-…`, undocumented `INotificationController` `2537d644-…`), whose `ActivateNotification(AUMID, ID, data)`
  runs the toast's activation: `NitroActivator` creates an unpackaged app's COM activator (the CLSID from the Start
  shortcut's `System.AppUserModel.ToastActivatorCLSID`, or `AppUserModelId\<AUMID>\CustomActivator`) and calls
  `INotificationActivationCallback::Activate(AUMID, launch, inputs)` (first through Explorer's `CLSID_ImmersiveShell`
  service, for the foreground; without Explorer straight from the service); other activators handle packaged apps
  (foreground, background), `protocol` launches and system actions; then the notification is removed. An unpackaged
  app without an activator gets nothing (the toast just goes, as in Explorer). Any user may create the controller,
  so NeoShell calls the same method with the listener's ID in decimal and no activation data (a click on the body),
  having let the app take the foreground (`AllowSetForegroundWindow(ASFW_ANY)`: NeoShell had the click). If that
  fails, the app opens as from Start and the notification is removed. Verified as the shell with a test app with a
  COM activator (arguments delivered, its window took the foreground), a `protocol` toast (Calculator) and a
  packaged app's toast (Calculator, posted under its AUMID), from a toast and from the notification center, and
  alongside Explorer that the same calls behave as its clicks. Buttons, inputs and context menu items aren't shown
  (out of scope), so only the body's activation is used.
- **Toast sound** (`ToastSounds`, Interop `ToastAudio`, `NotificationSound`). The controller picks the sound
  (`SoundPropertiesFactory::Create`) and the toast host plays it (`AudioHelper`, a media player in the alerts
  category) as the toast starts to show; only the newest toast's sound plays (`ManageToastAudio` stops the others'),
  and a sound that won't play falls back to `ms-winsoundevent:Notification.Default`. The rules, from the code:
  - none for a silent toast (`<audio silent="true"/>`), a ghost toast, when Settings' "Allow notifications to play
    sounds" is off (`Notifications\Settings\NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND` = 0) or the app's "Play a
    sound when a notification arrives" is off (`Settings\<AUMID>\SoundFile` = ""), and of course with no toast (Do
    not disturb, focus);
  - else the `src`: `ms-winsoundevent:Notification.*` (the user's sound scheme, `AppEvents\Schemes\Apps\.Default`),
    or a file (`ms-appx:///` or `ms-appdata:///local/` in the app's package, `file:///`); none means
    `Notification.Default`;
  - an `alarm` / `incomingCall` scenario rings `Notification.Looping.Alarm` / `.Call` unless the `src` is already one
    of those; the scenario counts only for a toast with a button (measured: an alarm without one plays the default);
  - it loops only with `loop="true"`, for as long as the toast shows (a scenario alone plays its sound once);
  - an app's `SoundFile` other than `*default*` replaces it all.

  The listener doesn't give the XML, so NeoShell reads it from the app's toast history
  (`ToastNotificationManager.History.GetHistory(AUMID)`, newest first, which works for any app), matching the
  notification by its texts; some older entries fail to give their XML (0xC00CE558) and are skipped; not found means
  the default sound. Played with a WinRT `MediaPlayer` (alerts category, no media session, opened ahead so the first
  sound isn't late), started before the toast's window is made. Measured with the render endpoint's peak meter
  against a pixel near the toast's right edge: Explorer's sound starts 35–60 ms before that pixel changes,
  NeoShell's 0–55 ms before (60–130 ms after, now and then); lengths match (Default 1.16 s, Mail 1.39 s, Reminder
  1.38 s, Looping.Alarm 5.1 s, an unknown sound the default's). Not matched: a ghost toast (`SuppressPopup`) can't be
  told from the listener or the history, so it pops up with its sound; `duration="long"` toasts (25 s in Explorer,
  so a looping sound rings 25 s) and alarms with buttons (which stay until dismissed) time out as any other toast.

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
  reset (search, an open folder or category, the letters, scroll) once it's out of sight; opened again while closing, it turns back from where it is.
- Toggling: pressing the Start button deactivates Start before the button's click arrives, so a click within
  400 ms of a deactivation doesn't reopen it.
- Resizable by dragging a top corner (`ResizeGrip`, with the resize pointer): the bottom stays above the taskbar, a
  centred Start grows on both sides, a left-aligned one only has the top-right grip. Live while dragging; the size
  (as the monitor allowed it) is saved on release. 832 by 860 epx by default, at least 480 by 400, at most the
  monitor above the taskbar (`StartMenuLayout`, tests).
- Home is one scrolling page below the search box, as Explorer's 25H2 Start with Recommended off (Settings →
  Personalization → Start → Recent off, which T16 skips): **Pinned**, then **All**, each switched off by Settings'
  Pinned and All (`HKCU\…\CurrentVersion\Start` `ShowPinnedSection`, `ShowAllAppsSection`, 1 when missing; found with
  Procmon on SystemSettings.exe), followed live through a `RegistryWatcher` on the key. Without Pinned, All's heading
  takes Pinned's place (its text 93 from Start's top). The page scrolls from 64 below Start's top, as Explorer's.
  - **Pinned** grid (`ShellSettings.StartPins`: apps and folders of apps; the former apps-only `PinnedStartApps`
    is read once into it). Drag to reorder by hand (`GridReorder`, unit tested): a copy of the pin follows the
    pointer (in a layer above everything, so it can leave a folder's panel) and the icons between its old and new
    place shift one slot. Pinned icons shrink while
    pressed and a dragged one grows, as on the taskbar. The grid's own drag and drop keeps the
    dropped icon hidden until the drag operation winds down, and its item transitions animate the move as a removal
    and an addition, so both are off. Pinned is updated in place when Start opens, not refilled (which
    would replay every icon's entrance). The first time the catalog loads, Explorer's Start pins
    are added once (`ExplorerStartPinsImported`). Start keeps them encrypted in `start2.bin`, so they're read through
    `StartTileData.dll`'s `IStartLayoutCmdlet::ExportStartLayout` (the object behind `Export-StartLayout`), which
    writes `{"pinnedList":[{"packagedAppId":…},{"desktopAppLink":"%APPDATA%\…\x.lnk"}]}`. A shortcut is matched to
    the catalog by its `System.AppUserModel.ID`, else its `System.Link.TargetParsingPath`. The export flattens
    folders: a folder's apps are listed in its place, in its order, with nothing to say they were in one (checked
    with two folders, 25H2), so folders aren't carried over. Inside `start2.bin` they are there: caught in the
    debugger (`StartMenu!SlimObfuscationManager::Scramble`, called by `StartMenuPersistenceManager::SaveModel` →
    `StoreFileToDisk`), the plain text is JSON, `{"pinnedList":[{"id":"{guid}","pinType":2,"name":"","items":
    [{"id":"W~Microsoft.Windows.Explorer"},{"id":"P~Claude_…!Claude"}]},{"id":"P~windows.immersivecontrolpanel_…"}],
    …}` (an empty name is "Folder"; `W~` a desktop app's AUMID, `P~` a packaged app's). But the file is AES-CBC
    encrypted with a key Start derives in private code (`StableCryptoFunctions::GetSymmetricKeys`, from the
    FILETIME in the file's header and fixed seeds); reading it would mean re-implementing a private cipher any
    update can change, so it isn't done.
  - **No Recent row.** NeoShell's Start had a Recent row of the six apps started last (milestone 5); it's gone (T23).
    Explorer 25H2's "Recent" section is Recommended renamed (recently added apps, recent files, tips: T16, skipped),
    and with it off Start shows no recently started apps anywhere. UserAssist
    (`HKCU\…\Explorer\UserAssist\{CEBFF5CD-…}\Count`: ROT13 value names, AUMIDs or known-folder paths, the run count
    at offset 4, focus time in ms at 12, last run as a FILETIME at 60) is still read each time Start opens, for All's
    order of use (below). `Launcher` starts non-packaged apps with `ShellExecuteEx` + `SEE_MASK_FLAG_LOG_USAGE` so
    NeoShell's launches are recorded too, and records packaged apps' starts as Explorer does (below).
  - **How Explorer records a packaged app's start** (25H2; Procmon on the key, cdb breakpoints on
    `shell32!CUserAssist::FireEvent` in Explorer and Start): Explorer's process writes every value, always through
    shell32's UserAssist object (`CLSID_UserAssist` {DD313E04-FEFF-11D1-8ECD-0000F87A470C}, in-process, interface
    {49B36D57-5FD2-45A7-981B-06028D577A47}) as `FireEvent(&{CEBFF5CD-…}, UAE_LAUNCH = 0, AUMID, 0)`; shell32 bumps
    the value's run count and last run, the session totals in `UEME_CTLSESSION` (its second DWORD counts starts)
    and its own top lists, and skips it all when "Let Windows track app launches" is off.
    - From Start: `StartTileData!TileStoreTransformer::ActivateTileInternal` puts
      `AppActivation.ShouldNotifyUAOfLaunch = true` in the activation's property set and hands it to the shell
      broker in Explorer (`ImmersiveShellBroker`). A UWP app's value is written by twinui.pcshell's view manager
      when its view first shows (`CApplicationManager::_HandleViewNavigationRequested` →
      `AppActivationPropertySetHelpers::ConditionallyNotifyUAOfLaunch`); a packaged desktop app's by the broker
      (windows.immersiveshell.serviceprovider) after it starts it through daxexec. The broker's activation site also
      answers `SID_ExecuteLogUsage` {582B888F-80D5-4BC4-9A6D-5D7A58EFD60A}, so twinui.appcore's
      `DesktopAppXActivator` starts the desktop app with `ShellExecuteEx` + `SEE_MASK_FLAG_LOG_USAGE`, which adds a
      second value for the executable (`{6D809377-…}\WindowsApps\Microsoft.Paint_<version>_x64__…\mspaint.exe`),
      ~0.1-0.3 s before the app's. Each start adds one to each value's run count.
    - From the taskbar: `Taskbar!LaunchFromTaskbar` opens the pin's `shell:AppsFolder` item with `ShellExecuteEx`;
      windows.storage records the AUMID (`StoreHintsAndReportUserAssistInfo`) before the AppsFolder handler starts
      the app. One start, one count.
    - `IApplicationActivationManager::ActivateApplication` alone records nothing: no property set, no site.
    - NeoShell does the same (`PackagedApps.Activate(…, logUsage: true)` then `UserAssist.RecordLaunch`, for every
      launch through `Launcher`: Start's pins, All and search, taskbar pins and their menus, Win+1…9,
      toasts). The activation manager (twinui.appcore, in-process) takes an `IObjectWithSite` site; NeoShell's
      answers `SID_ExecuteLogUsage` as the broker's does, and UWP activation never asks for it. Compared value by
      value with the same app started from Explorer's Start: the same two values, counts and layout. Alongside
      Explorer, `UEME_CTLSESSION`'s totals don't move for NeoShell's starts (shell32 keeps the session per
      process, and Explorer's copy wins; the same holds for desktop apps NeoShell starts with
      `SEE_MASK_FLAG_LOG_USAGE`); as the shell they count as Explorer's do. Explorer's own Start doesn't reorder its
      category folders or drop "New" for an app only NeoShell started: those follow Start's own data, not UserAssist.
  - **All** (`AllApps`, unit tested; Interop `Shell/StartAppData`), as Explorer's 25H2 Start, on the page below
    Pinned. NeoShell's separate All apps page, its button beside Pinned and its Back button are gone (T23). Read
    from StartMenu.dll (`CategoryProvider`, `AllAppsViewModel`, `AppSortHelper`) and StartTileData.dll with their
    PDBs in Ghidra, Explorer's Start through UI Automation, and its registry and CloudStore data:
    - **Heading**: "All" (14 semibold) 31 below Pinned's grid, 32 tall, its text 63 in; at the right (its right edge
      52 from Start's) a flat `DropDownButton` "View: Category" (14 px, 32 tall; automation name "View selected,
      Category") with a menu below it, right-aligned: Category, Grid, List, the current one ticked. The view is
      Explorer's own `HKCU\…\Start\AllAppsViewMode` (0 Category, 1 Grid, 2 List), which NeoShell writes too, so both
      Starts show the same view; NeoShell follows it live, Explorer reads it as its Start starts.
    - **Category view**: cards 156 × 192, 27 apart and 12 between rows, four to a row from 64 in, 9 below the
      heading. A card is a 156 square (radius 8, `CardBackgroundFillColorDefault`, 1 px `CardStrokeColorDefault`) with
      four 68 places two by two from 10 in, the icon 32 in each, flat buttons with the app's name as tooltip; below it,
      from 160, a 156 × 32 flat button with the category's name (12 px). With more than four apps the fourth place
      shows the next four apps' icons (16, 2 apart, a 34 square) and opens the category, as the name does. Explorer's
      automation names ("Other category with 15 Items, Claude, Visual Studio Code, Microsoft News, and 12 others"). An
      open category is the folder panel (450 × 378, centred, its own acrylic) with the name as a 20 semibold title
      instead of the name box, the apps as 96 × 84 tiles four to a row; it grows out of the card, from the card's size,
      in 333 ms and shrinks back in 150 ms, as a folder (recorded at 60 fps: the same curves as the pin folders').
      The card isn't hidden behind it. A click beside it or Esc closes it.
    - **Categories**: Start's `AppCategory` values 0-31 (0 Other, 1 Accessibility, 2 Communication, 3 Games, 4 Travel,
      5 Security, 6 Personalization, 7 Photo & Video, 8 Social, 9 Utilities & Tools, 10 Kids & Family, 11 Medical,
      12 Health & Fitness, 13 Productivity, 14 Books & Reference, 15 Developer Tools, 16 Entertainment, 17 Music,
      18 Personal Finance, 19 Education, 20 Business, 21 Navigation & Maps, 22 Graphics, 23 Multimedia Design, 24
      Government & politics, 25 News & Weather, 26 Sports, 27 Lifestyle, 28 Shopping, 29 Food & Dining, 30
      Creativity, 31 Information & Reading; the names are its `AllApps_Category_N` strings). Where an app's comes
      from: StartTileData asks a web service ("APS") when a tile turns up and saves the answer in
      `HKCU\…\Start\TileProperties\<tile id>\Category` (a tile id is `P~` + a packaged app's AUMID, or `W~` + a desktop
      app's AppsFolder id; ids with backslashes are nested keys); StartMenu's `GetCategoryForTile` reads that
      (feature 55161678; else the mappings below), and an app without a saved category is Other — VS Code here,
      though Windows' mappings list it under Developer Tools. NeoShell reads the same keys. Only when Start has saved
      none at all (it never ran for the user) does it fall back, as `ExtendedProperties::FetchCategoryFromLocalMappingsAndStoreInRegistry`
      does, on Windows' own mappings: `%SystemRoot%\SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy\StartMenu\Assets\AllAppCategoryMappings`,
      LZMS-compressed with the Compression API's header (`cabinet.dll` `CreateDecompressor(COMPRESS_ALGORITHM_LZMS)`),
      4.4 MB of JSON `{"13": ["notepad", …], …}` with ~70 000 ids: a packaged app by its package family name, a
      desktop app by its target's path below `%systemroot%`, `%ProgramW6432%` or `%ProgramFiles(x86)%`
      (`\system32\narrator.exe`), else by its AUMID (`msedge`). The web service's answers differ (Paint 23 against the
      file's 7; osk 15 against 1).
    - **Merging** (`PruneAndCombineCategories`): 7, 23, 22 and 6 always go into Creativity; 2 and 5 into
      Productivity; 25, 24, 14 and 21 into Information & Reading; 3, 26 and 17 into Entertainment and 28, 12, 29, 11,
      18 and 4 into Lifestyle only while they have fewer than three apps; then every category but Other with two apps
      or fewer goes into Other (News & Weather's two apps end up there). (A category of one folder of three or more
      apps becomes that folder's apps; NeoShell has no folders in All, below.)
    - **Order**: apps in a category by `Rank` (Start's tile `Relevance` × 1000, rounded) then name
      (`AppSortHelper::CompareRanks`); categories by the sum of their two first apps' ranks (`RankCategories`,
      descending). Start's relevance is a decaying count of launches it tracked (StartTileData's
      `LocalStartVolatileTileProperties`: `Relevance` float, launch count, last launch, launches per day; in the
      CloudStore value `…\CloudStore\Store\Cache\DefaultAccount\$de${6af0fa78-…}$$windows.data.unifiedtile.localstartvolatiletilepropertiesmap`,
      Claude 0.183, File Explorer 0.072, Settings 0.056 here). Explorer's figure counts only starts its own shell saw,
      so NeoShell measures use itself, from UserAssist: an app's starts plus its minutes in front, over every value
      that is the app. On this machine that gives Explorer's order exactly (categories Other, Productivity, Utilities &
      Tools, Developer Tools, Creativity, Entertainment, Accessibility; Most used Claude, File Explorer, Settings,
      Notepad, Terminal, Edge) except for apps started outside Explorer (Camera and Calculator, started by NeoShell,
      rank above apps Explorer saw started once).
    - **Grid and List views**: the apps by name under their letters, Explorer's letters in its order: &, #, A to Z,
      then other scripts under a globe (E12B); accents dropped (É under E). Each letter's header is a flat 728 × 40
      button 52 in, its letter 12 in at 14 px, 9 below the previous group and 4 above its apps; it zooms out to the
      letters. Grid tiles are 96 × 84 from 32 in (eight to a row), the icon 32 at 12 down, the name (12 px) on two lines,
      or on one above "New" or "System"; List rows are 728 × 40 from 52 in, the icon 24 at 12, the name 20 after it
      at 12 px with "New"/"System" below. **Most used** (Settings → Start → All → "Show most used apps (grid and list
      views only)", `ShowFrequentList`): a first group, its header "Most used", of the six apps with the highest rank
      above 0 (`TryAddTileToFrequentApps`).
    - **New and System** under an app's name (12 px; "New" in `AccentTextFillColorPrimary`, "System" secondary; in
      the category panel and the name views, and in the cells' automation names): StartMenuProperties.HasNewBadge is
      `RoamedTileProperties.FirstSeenTime == 0` (windowsudk.shellcommon), and Start sets the first-seen time when the
      app is started from Start. NeoShell reads Start's roamed tile map from the CloudStore
      (`…$$windows.data.unifiedtile.roamedtilepropertiesmap\Current` `Data`: a 16-byte header, then Bond compact
      binary v1: a map of tile id to properties, field 0 `StartTileProperties` with field 10 FirstSeenTime and 20
      IsUserPinned): an app is new when it has no first-seen time there and hasn't been opened from NeoShell's Start
      (`ShellSettings.StartAppsOpened`; NeoShell never writes Explorer's store). On this machine that marks the same
      apps as Explorer's, of those both list. Nothing is new when the map is missing. "System" (`IsSystemComponent`) wins over "New": Start's
      own lists (`ShellLists::s_packagedAppSystemCategorizationOverrides`, `…unPackaged…`: Store, Game Bar, Phone
      Link, Get Help, Windows Security, Dev Home, Windows Backup, File Explorer, WSL… yes; Camera, Edge, Media Player
      Legacy, Remote Desktop no), else a packaged app of `PackageOrigin` Inbox (`SignatureKind.System`), else for a
      desktop app the app resolver's flag — read here as a program in the Windows folder, plus Windows Tools.
    - **Letters** (Explorer's `SemanticZoom` zoomed out): the page gives way to "All" (63 in, 93 down) with a "‹ Back"
      button at the right (69 × 32) and the letters in the middle, four to a row in 48 squares 4 apart: Most used
      (clock E823), &, #, A-Z, the globe; those without apps dimmed (disabled). A letter goes back to the page
      scrolled to its group; Back or Esc goes back where it was. Explorer zooms the page out and the letters in from
      larger (about 250 ms); NeoShell fades the letters in from 1.3× in 250 ms, decelerating, and fades the page back in.
    - **Not done**: Explorer's folders in All (Start Menu subfolders such as Accessibility, Python 3.13, Windows Kits
      shown as expandable folder items in Grid and List, and as items of their own categories, with "Python" and
      "Python 3.13" folder items in Other here): NeoShell's catalog is the flat AppsFolder, so its All lists a
      folder's apps by themselves and its categories have more apps (Other 40 against Explorer's 15). Also the
      `HideCategoryView` policy, arrow-key navigation between items as Explorer's, and the page's own zoom-out.
  - **Folders of pins** (Windows 11 23H2+; `StartPins` and `GridReorder`, unit tested), as Explorer's:
    - Holding an app over the middle of another app or a folder (the pointer within 28 px of the cell's centre,
      either way; Explorer's zone measured at about +31/-23 px in a 96 px cell) shows the drop will group them; off
      the middle, the drop goes before or after the cell under the pointer and the cells in between make way. There
      is no delay: Explorer previews as soon as the pointer is there. An app under the pointer shows the folder to
      be: the plate fades in, the app's icon shrinks to half and slides into the plate's first place (9 left, 8 up)
      and its name fades, in 150 ms (Explorer: ~8 frames at 52 fps). Folders don't nest: a dragged folder only
      moves. Dropped, an app on an app makes a folder in the target's place, the target first; on a folder, it goes
      last in it. Explorer then fades the old tile out (~230 ms), shows nothing for ~0.6 s and fades the folder in
      (~350 ms) while it saves and reloads its model; NeoShell swaps it at once.
    - The tile: a 40 px plate (1 px `CardStrokeColorDefault` edge, `CardBackgroundFillColorDefault`, radius 4)
      centred where an icon would be, with the first four apps' icons at 16 px two by two, 2 apart, the first 3 in
      and 4 down from the plate's edge. The name below: "Folder" until renamed (stored as "").
    - Opening it: a 450 × 378 panel centred in Start, radius 8, a 1 px `SurfaceStrokeColorFlyout` edge and an
      acrylic of its own (Start's content behind blurred, about 6/7 of Start's colour; NeoShell's in-app
      `AcrylicBrush` can't see the window's backdrop, so it starts from Start's tint, greyed a little when that's the
      accent). It grows out of the tile — from 38/450 of its size at the tile's centre to full size in 333 ms,
      `cubic-bezier(0,0,0,1)`, which fits Explorer's 0.48/0.65/0.84/0.92/0.99 at 33/67/133/200/267 ms — and
      shrinks back into it in 150 ms, accelerating. The tile is hidden while the panel stands for it; Start isn't
      dimmed. A click beside the panel, or Esc, closes it (Esc again closes Start). The name is a centred 20 px
      semibold `TextBox`, 320 × 40, 33 px from the top, flat until hovered or focused; Enter, leaving it or closing
      the folder renames the folder, and Esc in it puts the name back. The apps are 96 px cells, four to a row, from
      33, 94 (scrolling after three rows).
    - Apps are dragged within the panel to reorder them, or out of it onto Start behind it (the panel stays open)
      to take them out; they land before or after the cell under the pointer. A folder stays while it has an app
      (Explorer 25H2 keeps one-app folders, also after Start reopens); taking the last one out removes it and closes
      the panel.
  - **App menus** (right-click, or the menu key, on any app; `StartAppMenu`, unit tested), as Explorer's 25H2 Start
    (read through UI Automation, which sees inside Start's `CoreWindow` once found from its handle):
    - No Open item. The commands come in a fixed order, from `StartMenu.dll`'s `ContextMenuSorter` (the list its
      `GetContextMenuAsync` builds): Move to front, Move left, Move right, Create a new app folder, Move to app folder,
      Remove from app folder, Pin to / Unpin from Start ┃ Run as administrator, Open file location ┃ File Explorer's
      Manage, Properties, Map / Disconnect network drive ┃ Pin to / Unpin from taskbar, App settings, Uninstall.
      A separator parts groups that have something; then the jump list, without one. (The sorter also knows Rate and
      review and Share, only with extended verbs, Run as different user and Remove from list; NeoShell leaves them
      out, and Start has no More submenu in 25H2.)
    - Moves: in the grid Move to front only from the third place (from the second, Move left does it), Move left /
      right where there's room; a folder's own menu has only those. A pinned app also gets "Create a new app folder"
      (the app alone in a new folder in its place, not opened) and, with one folder, `Move to app folder "Name"`, with
      more a "Move to app folder ▸" submenu of the folders' names (text only); the app goes last in it. An app in a
      folder: Move left / right within it, "Remove from app folder" (it goes just after the folder).
    - Run as administrator, Open file location, Uninstall and File Explorer's verbs are the app's verbs in
      `shell:AppsFolder`, as Explorer's are (`AppResolverTransformer::GetVerbs` asks the desktop broker for the
      item's shell verbs): `ShellMenu.ForApp` binds the folder's item (by AppUserModelID, else full path, else
      known-folder path such as `{1AC14E77-…}\magnify.exe`; a full System32 path doesn't parse) to its
      `IContextMenu` (`BHID_SFUIObject`) and the verbs `runas`, `OpenFileLocation`, `Uninstall`, `Manage`,
      `ItemProperties`, `connectNetworkDrive`, `disconnectNetworkDrive` become items, run through that menu (Start
      closes first). So they're there exactly when Explorer shows them: no Uninstall for Edge or Settings, no Run as
      administrator for Settings, no Open file location for packaged apps. Open file location opens the shortcut's
      folder with it selected; Run as administrator elevates the shortcut, or a full-trust packaged app.
    - Uninstall: a packaged app gets Explorer's dialog in Start (`InterceptTileUninstallVerb`): a `ContentDialog`
      'Uninstall "Name"?' / "This app and its related information will be removed.", Uninstall and Cancel (the
      default), 374 wide, centred in Start, with a 1 px `SystemAccentColor` edge and no smoke over Start (WinUI moves
      the smoke into a popup of its own, collapsed on `Loaded`); then `PackageManager.RemovePackageAsync` for the
      user, the app's pins removed and the catalog reloaded. A desktop app's opens Settings the way Explorer's
      `SettingsUninstallVerb` does: Settings (`SystemSettingsAUMID`) activated with
      `page=SettingsPageInstalledApps&target=SystemSettings_StorageSense_AppSizesListFilter` (Installed apps, the
      search box ready).
    - App settings (packaged apps): Settings activated as Explorer's `SettingsVerb` does,
      `page=SettingsPageInstalledApps&target=SystemSettings_StorageSense_HiddenAppAdvancedPageLink&invoke=true&parameter=<family name>`,
      a system app's (`SignatureKind.System`, e.g. Settings) under `SettingsPageSystemComponents` /
      `…HiddenSystemComponentsAdvancedPageLink`: the app's advanced options page, as from Explorer's Start.
    - As the shell Settings can't show, so App settings is left out and a desktop app's Uninstall runs the folder's
      own verb, which opens Programs and Features (in an `explorer.exe /factory` window, which works without the
      shell); the folder's Run as administrator for a packaged app goes through Explorer and fails there, so it's
      left out too.
    - The jump list (`JumpLists`, as the taskbar's) below: each category under a 12 px secondary heading 16 in and
      32 tall, its entries with their icons; long names end in an ellipsis at Explorer's 290 px menu width.
    - Search results get the commands of Windows' search box instead: Run as administrator, Open file location (a
      folder glyph, E8B7), Pin to / Unpin from Start, Pin to / Unpin from taskbar, App settings, Uninstall, in one
      run without separators or jump list (the search box's Rate and review and Share, for Store apps, aren't done).
      Files in results keep Open.
    - Glyphs (Segoe Fluent Icons 16, read from Explorer's menus): Move to front E1AA, Move left E64E, Move right
      E64D, Create a new app folder E8F4, Move to app folder E8DE, Remove from app folder E8DA, Pin E718, Unpin
      E77A, Run as administrator E7EF, Open file location ED43, App settings E713, Uninstall E74D; File Explorer's
      verbs and folder names have none. Items are WinUI's (28 tall from the mouse, 32 apart, separators 35), which
      measure as Explorer's: menus of the same items come out the same size to the pixel.
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
  - The user: a flat 94 × 40 button (Explorer's `UserTileButton`, "User account for <name>", the name as tooltip),
    52 from Start's left edge, padding 12,4: picture 32, 12 apart, name (`GetUserNameEx(NameDisplay)`, else the
    account name) at 12 px. The picture is `HKLM\...\AccountPicture\Users\<SID>\Image<size>`, else Windows' grey
    silhouette (`%ProgramData%\Microsoft\User Account Pictures\user-32/40/48/192.png`, `user.png` above), as
    Explorer's (StartDocked.dll names `user.png`). It opens the **Account menu** (below).
  - Switch to Explorer button (see lifecycle), shell mode only, behind a confirmation dialog that defaults to Cancel.
  - **Folders beside the power button** (Settings → Personalization → Start → Folders; Interop
    `Shell/StartPlaces`, unit tested), read each time Start opens: Explorer too shows a change the next time it
    opens. Settings keeps them in `HKCU\Software\Microsoft\Windows\CurrentVersion\Start\VisiblePlaces`, a
    REG_BINARY of 16-byte GUIDs in the order they were switched on (empty or missing: none). Start shows them in its
    own fixed order whatever the order in the value. The MDM policies
    `HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Start\AllowPinnedFolder<Name>` (0 hidden, 1 shown,
    65535 not set; the names are in `StartDocked.dll`) override the choice.

    | Folder | GUID | Glyph | Opens |
    |---|---|---|---|
    | Documents | `2D34D5CE-FA5A-4543-82F2-22E6EAF7773C` | E8A5 | `shell:Personal` |
    | Downloads | `E367B32F-89DE-4355-BFCE-61F37B18A937` | E896 | `shell:Downloads` |
    | Music | `B00B0620-7F51-4C32-AA1E-34CC547F7315` | EC4F | `shell:My Music` |
    | Pictures | `383F07A0-E80A-4C80-B05A-86DB845DBC4D` | EB9F | `shell:My Pictures` |
    | Videos | `42B3A5C5-7D86-42F4-80A4-93FACA7A88B5` | E714 | `shell:My Video` |
    | Network | `FE758144-080D-42AE-8BDA-34ED97B66394` | EC27 | `shell:NetworkPlacesFolder` |
    | Personal folder | `74BDB04A-F94A-4F68-8BD6-4398071DA8BC` | EC25 | `shell:UsersFilesFolder` (the profile) |
    | File Explorer | `148A24BC-D60C-4289-A080-6ED9BBA24882` | EC50 | as Win+E |
    | Settings | `52730886-51AA-4243-9F7B-2776584659D4` | E713 | `ms-settings:`; in shell mode Control Panel (`control.exe`), as Settings is a UWP app |

    Each is a flat 40 × 40 button with a 16 px glyph (Segoe Fluent Icons, matched against Explorer's pixels) and its
    name as tooltip. Explorer's buttons, the power button's too, touch, 40 apart, the power glyph's right edge 65 px
    from Start's. Right-clicking them gives "Personalise this list" (`ms-settings:personalization-start-places`;
    Control Panel in shell mode). NeoShell's Settings button, always shown before, is now this list's.
  - Power button menu: see **Power menus** below. Its items are filled as the menu opens. The power button hides
    when the `HidePowerButton` MDM policy is set (`PolicyManager\current\device\Start`). While Windows Update waits
    for a restart (`WNF_USO_REBOOT_REQUIRED` ≠ 0, the state StartDocked.dll's `PowerOptionsViewModel` subscribes
    to) and Shut down or Restart is offered, its glyph becomes F1B1 with the orange dot. That look is inferred from
    StartDocked's resources and wasn't seen live: the WNF state can't be published without SYSTEM. Its accessible name
    then is `PowerButtonDisplayNameWithUpdate`, a string not found in any resource file, so NeoShell keeps "Power".
  - **Not here on 25H2:** Sign out, and switching to another account, are in the **Account menu** on the user.

### Account menu (`StartMenuWindow` `AccountFlyout`, Interop `Shell/AccountMenu`)

On 25H2 the user in Start's bottom row opens an account card, not StartDocked's own `UserTileView` menu (Change
account settings, Lock, Sign out, the other users) that it falls back to without the account control. The card is
`AccountControl`, a React Native for Windows view (Hermes bytecode in
`SystemApps\MicrosoftWindows.Client.Photon_cw5n1h2txyewy\Public\wsxpacks\AccountControl\index.windows.bundle`,
decompiled with hermes-dec; strings in its `assets\strings\<locale>.json`). It gets its actions and the other
accounts from `WindowsUdk.System.UserProfile.AccountSwitcherAction/AccountSwitcherUser` (windowsudk.shellcommon.dll),
which wrap `Windows.Internal.Shell.StartUI.UserTileMenu` in windows.internal.shell.broker.dll (read with its PDB in
Ghidra). NeoShell reads the same things itself each time the card opens (`AccountMenu.Read`).

- **Layout** (measured on Explorer's with UI Automation and screenshots; NeoShell's matches to the pixel): a Flyout,
  placement Top, centred over the user button and kept on the monitor (a left-aligned Start's card sits at the
  screen's left edge), 4 above it; 362 × 141 with its border, corner 8, Windows' grey acrylic even on an
  accent-coloured Start.
  - Header, 8 below the top, 40 high: Microsoft's logo (20 square, the four squares inset 1 with 1 between, as its
    SVG) 16 from the left, 5 apart from "Microsoft" (14 px semibold, secondary text). On the right, 8 from the edge
    and 8 apart: "Sign out" (flat button 78 × 40, 14 px) and "More options" (40 × 40, E712 16 px). Without the
    second, Sign out moves to the edge.
  - The account, 12 below: picture 58 (circle) 16 from the left; 12 apart, centred on it: the name (18 px
    semibold), the Microsoft account's email or "Local account" (12 px secondary), and 3 below a hyperlink
    "Manage my account" ("My Microsoft account" for a Microsoft account; 12 px, padding 1); 18 below it.
  - "…" opens a second Flyout, bottom edge aligned left, 4 below the button, 243 × 91: "Other users" (12 px
    secondary, 16 from the left, 13 down), then 8 below the rows, 4 in from each side: 233 × 48 flat buttons with
    the picture 30 at 9, the name 12 to its right (14 px; "Signed in" under it at 12 px secondary), the name as
    tooltip. Signed-in accounts first, then by name (`CompareUsers`; unit tested). A plain "Switch user" row
    (E748) follows them where Windows offers it.
  - Hover: the subtle fill on Sign out, "…", the rows and the link, as on the user button.
  - Opens with the XAML popup transition (a short fade and a 5 px slide), closes at once, as Explorer's.
    Explorer's card shows its content fading in before its acrylic; WinUI's windowed popup shows the acrylic a few
    frames before the content.
  - Esc closes "…" and then the card, leaving Start open; Tab moves between Sign out, "…" and the link.
- **What shows** (`UserTileCommand::Is…Enabled`):
  - Sign out: not the `StartMenuLogOff` or `NoLogoff` policy, nor MDM `Start/HideSignOut`.
  - Manage my account: not a guest, nor the `UseDefaultTile` or `NoControlPanel` policy, nor MDM
    `Start/HideChangeAccountSettings`.
  - Plain Switch user: fast user switching as for the power menus, and only on a PC joined to a domain or
    Microsoft Entra ID (`IsOS(OS_DOMAINMEMBER)` or `DsrIsDeviceJoined`), nor MDM `Start/HideSwitchAccount`. On a
    workgroup PC Explorer lists the accounts instead.
  - Other users (`SwitchUserList::PopulateUsers`): Windows' user list for the sign-in screen, without
    `DontEnumerateForLogon` accounts and this one; NeoShell takes the enabled local accounts (`NetUserEnum`)
    that `Winlogon\SpecialAccounts\UserList` doesn't hide, named by full name, else account name. On a domain the
    local ones show only while signed in. Signed in = a session of that account (`WTSEnumerateSessions`).
  - "…" (`isMoreOptionsButtonVisible`): other accounts to list, or Switch user and Sign out both offered.
  - A Microsoft account: `NetUserGetInfo` level 24 (`usri24_internet_identity`, the principal name as email).
- **Actions**:
  - Sign out → `Power.SignOut` (`ExitWindowsEx(EWX_LOGOFF)`; the broker calls shell32's `LogoffWindowsDialog(0)`,
    which ends there). Start closes first.
  - Manage my account → `ms-settings:accounts` (`ManageAccountLink`); as the shell Settings can't start, so it
    opens Control Panel's User Accounts (`control.exe /name Microsoft.UserAccounts`).
  - Another account (`UserTileSwitchUser::SwitchTo`): writes its SID to
    `HKLM\...\Authentication\LogonUI\UserSwitch` `UserSID` and `Enabled` = 1, which LogonUI reads to choose it on
    the sign-in screen (Authenticated Users may set values there; .NET needs `ReadWriteSubTree` to write). Signed
    in: `WinStationConnectAndLockDesktop(server, its session)` (winsta.dll, not in the SDK) goes straight to its
    lock screen. Otherwise, or if that fails: `Power.SwitchUser` (`WTSDisconnectSession`; the broker calls shell32's
    `DisconnectWindowsDialog`, which does the same). The broker signs out instead when
    `IsLogoffRequiredForUserSwitching` (low-resource devices, `RtlQueryResourcePolicy` < 11, under a hypervisor
    without its feature bit); not mirrored.
  - Plain Switch user: `Enabled` = 1, then the same disconnect.
  - Verified with cdb breakpoints in a medium-IL NeoShell that skipped the call: Sign out passed
    `ExitWindowsEx(0, 0x80000000)`; a local test account not signed in got `UserSID`/`Enabled` written and
    `WTSDisconnectSession(0, 0xFFFFFFFF, FALSE)`.
- **Not done**: a Microsoft account's extras (Microsoft 365 / Copilot / Game Pass subscription and OneDrive storage
  cards, "View my benefits", the offline banner), which the account control fetches online; a work account's
  (Entra ID) tenant name in place of Microsoft's logo.

### Power menus (`PowerItems`, Interop `Shell/PowerOptions`, `Shell/Power`)

Start's power button, the Quick Link menu's "Shut down or sign out" and the Shut Down Windows dialog all list
shutdownux.dll's choices (`CShutdownChoices`, read with its PDB in Ghidra). Each reads them again as it opens
(`PowerOptions.Read`); `PowerOptions.Choices(menu)` puts them in order (unit tested).

- **Order**: the account choices for that menu, then Sleep, Hibernate, (Update and) Shut down, (Update and) Restart.
  - Start: Lock. Explorer's power menu on 25H2 is Lock, Sleep, Shut down, Restart, with no separator. Sign out is
    under the user picture.
  - Quick Link (twinui.pcshell's `CLauncherTipContextMenu::_EnumerateAndBuildShutdownMenu`, which ORs Sign out into
    the default mask): Sign out.
  - Shut Down Windows dialog (`_BuildShutdownOptionArray`): Switch user, then Sign out. Explorer shows Switch user
    even with only one account on the PC.
- **What decides each choice**:
  - Sleep: S1–S3 or modern standby (`GetPwrCapabilities`), `ShowSleepOption` (default on), MDM `HideSleep`. This
    VM has only S1 and still shows Sleep.
  - Hibernate: `ShowHibernateOption` (default off; Power Options → "Show in Power menu"), S4, and a *full*
    hibernation file (`HiberFileType` 2; Fast Startup alone keeps a reduced one), MDM `HideHibernate`.
  - Lock: `ShowLockOption` (default on), unless `DisableLockWorkstation` is set in Winlogon or in either Policies\System.
  - Sign out: not the `StartMenuLogOff` (=1) or `NoLogoff` policy.
  - Switch user: a console session that isn't remote (`SM_REMOTESESSION`, `SM_REMOTECONTROL`), fast user switching
    (`IsOS(OS_FASTUSERSWITCHING)`), no `HideFastUserSwitching`.
  - `HidePowerOptions` (HKLM) or `NoClose`: no Sleep, Hibernate, Shut down or Restart anywhere. MDM `HideShutDown` and
    `HideRestart` take away just those. Start shows "There are currently no power options available." when nothing
    is left.
  - The `Show…Option` settings are read from the policy (`HKLM\Software\Policies\Microsoft\Windows\Explorer`) first,
    then `HKLM\…\Explorer\FlyoutMenuSettings`, then the default.
- **Updates**: Windows Update writes `HKLM\SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\ShutdownFlyoutOptions`.
  Bit 8 is Update and shut down and bit 2 is Update and restart; each takes the place of the plain choice unless
  bit 4 or bit 1 keeps it beside it. Explorer ignores the value if it has any other bit set. `…\UX\RebootDowntime`
  holds `DowntimeEstimateLow` and `DowntimeEstimateHigh` in minutes, capped at 60. With them the names read
  "Update and restart (estimate: 5 - 15 min)", or "up to …", "more than …", "… min - 1 hr" (`PowerItems.Estimate`,
  tested). With updates waiting, the dialog picks Update and shut down. Otherwise it picks the power button action
  (`PowerButtonAction` policy, else `Start_PowerButtonAction`, default Shut down). Checked live by setting
  `ShutdownFlyoutOptions` to 0xA for a while (then back to 0). Explorer's Start, Quick Link menu and dialog
  (shutdownux's dialog object shown in a test process) all offered Update and shut down / Update and restart.
- **Start's look**: glyphs Lock E72E, Sleep E708, Hibernate E823, Shut down E7E8, Restart E777. The update choices
  use F1B1 and ED21 (each with a gap at the top right) under an orange `#FF9900` dot (EC83, the same colour in light
  and dark). A menu item takes one icon, so `PowerItems.Icon` adds the dot into the glyph's own grid. Every item
  except Lock has shutdownux's description as its tooltip, as Explorer's.
- **Actions** (`Power`, shutdownux's `_InitiatePowerTransition`, `ShutdownOption::Invoke`, `_GetEnhancedShutdownFlags`):
  - Lock → `LockWorkStation`. Sign out → `ExitWindowsEx(EWX_LOGOFF)`. Sleep / Hibernate → `SetSuspendState(false|true, …)`.
  - Switch user → `WTSDisconnectSession(current)`. The session keeps running and the sign-in screen lists the
    accounts. Explorer's dialog does this through AuthUI's `CLSID_AuthUISessionControl` (shell32 `_Disconnect`),
    and shutdownux's Disconnect calls `WinStationDisconnect` directly.
  - Shut down / Restart → `InitiateShutdown` with a planned reason, after enabling `SeShutdownPrivilege`:
    - Shut down: `SHUTDOWN_POWEROFF | SHUTDOWN_HYBRID` (Fast Startup; Windows ignores it when that's off). With Shift
      held, no hybrid. Update and shut down: `SHUTDOWN_POWEROFF | SHUTDOWN_INSTALL_UPDATES`.
    - Restart: `SHUTDOWN_RESTART`. Update and restart: `| SHUTDOWN_INSTALL_UPDATES`. With Shift held, any restart
      becomes `SHUTDOWN_RESTART_BOOTOPTIONS` (Advanced startup) without updates.
    - "Use my sign-in info to automatically finish setting up after an update" (Settings → Sign-in options; ARSO):
      when `LsaIsUserArsoEnabled` and `LsaIsUserArsoAllowed` (advapi32, not in the SDK headers) both say yes,
      updates add `SHUTDOWN_ARSO`. A plain restart or shut down adds it too unless `DsrIsDeviceJoined` (dsreg)
      says the PC is joined. ARSO is on here, so Update and restart is 0x2044.
    - Before shutting down, `…\WindowsUpdate\Orchestrator\InstallAtShutdown` (default value) is set to whether this
      shut down installs updates, as `ShutdownOption::Invoke` does. Authenticated Users may set values there, so the
      key is opened with `SetValue` rights only.
  - Not mirrored: with `Orchestrator\EnhancedShutdownEnabled` set, shutdownux passes the flags through USO's private
    `UsoCommitHelper` COM interface. That value doesn't exist on this VM. Also not mirrored: Start's confirmation
    when other users are signed in, and its `RestartApps` and sign-in options link; the Quick Link menu and dialog
    going through shell32's `ExitWindowsEx` path (`CommonRestart`) instead of `InitiateShutdown`. The effect is the
    same.
  - Verified with cdb breakpoints in a medium-IL NeoShell that skipped the call: Update and restart passed
    `InitiateShutdownW(null, null, 0, 0x2044, 0x80000000)`, and Switch user passed
    `WTSDisconnectSession(0, 0xFFFFFFFF, FALSE)`.

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
  area's shorter side from a corner a quarter, against the top edge maximized (a quarter near its corners). During a
  move Windows keeps the pointer inside the work area (`ClipCursor`), which left windows unable to go over the widget
  sidebar; as a move starts NeoShell widens the clip over the sidebar's strip (`ShellWorkArea.DragArea`: the work
  area without the sidebar's share), and Windows frees it when the move ends. The edges are that area's: the
  taskbar's edge at the bottom, the screen's beside the sidebar. While the pointer is in a zone, `SnapPreview` shows it: an acrylic,
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
  is lifted out of the column (invisible and kept in the tree at its size, so it keeps the pointer, with a negative
  bottom margin so it takes no room; each card keeps its gap below itself rather than the panel's spacing, so a lifted
  one leaves none). While a widget is over the sidebar, the others
  make room for it (moves of a desktop-level window leave its z-order alone, so dragging stays smooth): a
  card-shaped gap opens where it would go (before the first card whose middle is below the
  pointer, measured as if the gap weren't there), and the cards slide (`RepositionThemeTransition`). Off the sidebar
  the widget follows the pointer in its own window, held where it was grabbed, and stays where it's let go. Let go
  over the sidebar it takes the gap's place at once.
- Moving between the sidebar and the desktop makes a new view in the other window (WinUI can't move an element between
  windows), so the move hands over without showing it empty and filling in: a picture of the widget is taken as it's
  pressed (`RenderTargetBitmap`, its pixels copied into a `WriteableBitmap`, as a render target shows only in its own
  window; on the press as it takes 60-200 ms on the VM), and the new view is hidden under it, at its height, until the
  view is ready (`WidgetView.Ready`: once laid out, or for the profile picture, weather, pictures and now playing once
  they show their content) and two more frames are drawn, or 2 s at most. A floating widget dropped on the sidebar
  jumps into the gap, and its window closes once the new card is drawn. The pictures widget goes on with the picture
  it showed.
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
- **About Windows**: Windows' flag (four white panes, as tall as the text) on the left; on the right the
  edition, version, OS build with its revision, architecture and install date, read once from
  `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`. That key's ProductName still says "Windows 10" on Windows 11,
  so builds from 22000 are named "Windows 11", as winver does. One at a time.
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
ShowSearchButton     (former; read once, see Search on the taskbar)
TrayMode             (former; read once, see Hidden icons under System tray)
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
  settings round-trip and corrupt-file handling, wallpaper style mapping, AppBar rect calculation, other apps' app
  bar messages (32/64-bit) and their placing and work area, which desktop
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
