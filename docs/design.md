# NeoShell design

This document describes what NeoShell does and how each feature is built. The milestones are in [plan.md](plan.md);
coding rules are in [CLAUDE.md](../CLAUDE.md).

## Scope

- Display the wallpaper and the desktop icons, with Explorer's context menus for icons and the desktop.
- A WinUI taskbar with feature parity with the Windows 11 taskbar (exceptions in the system tray area).
- System tray icons, each on the taskbar or behind the chevron as Explorer keeps it (`NotifyIconSettings`).
- Network, volume and battery indicators, and the privacy indicator (apps using the microphone or the location);
  network, volume and battery are one button, as in Windows 11, that opens Quick Settings.
- Quick Settings: tiles (Wi-Fi, Bluetooth, Airplane mode, Accessibility, VPN, Rotation lock, Energy saver, Live
  captions, Night light, Mobile hotspot, Nearby sharing, Cast, Project), the brightness and volume sliders with the
  Sound output page, battery and All settings.
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

## Contents

Each area has its own file in [design/](design/):

- [Application lifecycle](design/lifecycle.md) — Startup, the two run modes, shell mode, startup apps, shell service
  objects, switching to Explorer, and why UWP apps can't be the shell.
- [AutoPlay](design/autoplay.md) — AutoPlay for inserted media while NeoShell is the shell.
- [Wallpaper](design/wallpaper.md) — The wallpaper windows, Explorer's wallpaper state, the slideshow,
  `IDesktopWallpaper` and Windows spotlight.
- [Desktop icons](design/desktop-icons.md) — The desktop icons: contents, order and places, view settings, drag and
  drop, menus, undo and rename.
- [Taskbar](design/taskbar.md) — The taskbar window (placement, display changes, flyouts and menus, the Quick Link
  menu), its layout, window tracking and task buttons.
- [Taskbar: pinned apps, jump lists and thumbnails](design/taskbar-apps.md) — Pinned apps, jump lists, thumbnails,
  dragging over the taskbar, progress, overlay badges and thumbnail toolbars.
- [Hotkeys, taskbar search and context menu](design/hotkeys.md) — The Win+ hotkeys and keyboard hook (shell mode),
  search on the taskbar and the taskbar's context menu.
- [System tray](design/tray.md) — The system tray (`Shell_TrayWnd`, notify icons, hidden icons) and other apps' app
  bars.
- [Indicators](design/indicators.md) — Network, volume and battery, the privacy and input indicators, the clock and
  notification bell.
- [Notifications and calendar](design/notifications.md) — The notification center and calendar, Do not disturb, focus
  sessions and toasts.
- [Quick Settings](design/quick-settings.md) — Quick Settings: its tiles and pages.
- [Start menu](design/start-menu.md) — The Start menu: Pinned and folders of pins, All, app menus, the app catalog,
  search and the bottom row.
- [Start: account and power menus](design/account-and-power-menus.md) — Start's account menu and power menus.
- [Window switcher and Snap](design/windows.md) — Alt+Tab and Snap: layouts, window snapping, Snap Assist and snap
  groups.
- [Screenshots](design/capture.md) — Screenshots and snips (Win+PrtScn, Win+Shift+S, Print Screen).
- [Widgets](design/widgets.md) — The widget sidebar, floating widgets and each widget.
- [Settings](design/settings.md) — The settings record and its file.
- [Testing strategy](design/testing.md) — How NeoShell is tested: unit tests, live UI checks, shell mode.
