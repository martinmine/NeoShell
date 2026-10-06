# NeoShell

A replacement for the `explorer.exe` shell on Windows 11, built with WinUI 3 on .NET 10. It shows the wallpaper
and desktop icons, a Windows 11 style taskbar (with system tray, network, volume and microphone indicators, and Quick
Settings) and a Start menu with search and power options.

- Design and feature spec: [docs/design.md](docs/design.md)
- Milestones and progress: [docs/plan.md](docs/plan.md) — tick items off as they land

## Development environment

Development happens in a disposable virtual machine dedicated to this project. Nothing of value lives on it, so
Claude is free to install tools, change system settings, create test accounts and run NeoShell as the shell here
without asking. The Safety rules below still describe how NeoShell must behave on real machines.
The owner manages git (branches and commits); don't commit unless asked.

The VM has `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\AutoRestartShell = 0` (set 2026-10-03 for
shell-mode testing): a killed Explorer stays down, so start `explorer.exe` again yourself after a shell-mode test.

## Code principles

- **Clean, simple, readable.** Write the smallest readable thing that works. No speculative abstractions,
  no interfaces with a single implementation, no layers "for later". Follow standard C# conventions.
- **BCL first.** Approved packages, and no others without asking the user first:
  - `Microsoft.WindowsAppSDK` (WinUI 3)
  - `System.Data.OleDb` (Windows Search Indexer queries)
  - Tests only: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`
- No DI container — `App` creates and wires the few long-lived objects by hand.
- No MVVM framework (no CommunityToolkit) — implement `INotifyPropertyChanged` directly where binding needs it;
  plain code-behind is fine for view-only logic. Prefer `x:Bind`.
- Settings via `System.Text.Json`; logging via NeoShell's own small file logger.
- Nullable reference types and implicit usings are on. Treat warnings as errors.
- Comments explain *why*, not *what*. Match the surrounding code's style and comment density.

## The two projects

- **`src/NeoShell`** — the WinUI app: windows, controls, view models, app logic. It never declares a P/Invoke.
- **`src/NeoShell.Interop`** — every call into Windows that isn't the BCL or WinUI: Win32 (`[LibraryImport]`),
  COM (`[GeneratedComInterface]` source-generated COM), WinRT system APIs, OLE DB search.
  - Raw declarations (`Native/`, `Com/`) are `internal`.
  - The public surface is small classes with .NET types and .NET events (e.g. `AudioEndpoint.VolumeChanged`),
    using `nint` for window handles.
  - Hand-written interop only — no CsWin32.
  - `InternalsVisibleTo` the test project so parsing logic can be tested.

Callbacks from COM/Win32 that arrive on other threads (Core Audio, WinRT events) are marshalled to the UI thread
by the app with `DispatcherQueue.TryEnqueue`. Message-only windows are created on the UI thread so their messages
need no marshalling.

## Stack

- `net10.0-windows10.0.26100.0`, `TargetPlatformMinVersion` 10.0.22000.0 (Windows 11)
- WinUI 3, unpackaged (`WindowsPackageType=None`), `WindowsAppSDKSelfContained=true`
- Custom `Main` (`DISABLE_XAML_GENERATED_MAIN`) for single-instance handling and `/exit`

## Layout

```
NeoShell.slnx
Directory.Build.props
src/NeoShell/                 WinUI app
  Program.cs, App.xaml(.cs)   Custom Main, single instance, /exit, run-mode detection, crash fallback
  ShellSession.cs             Shell-mode duties: registration, ready event, startup apps, session end
  ShellWorkArea.cs            Shell-mode work area: what the taskbar and the widget sidebar reserve
  Logging/                    Small file logger
  Desktop/                    WallpaperWindow (one per monitor), desktop icons and their menus (primary monitor)
  Taskbar/                    TaskbarWindow, task list + grouping, pinned apps, thumbnail popup, clock
  Tray/                       Tray area, overflow flyout, network/volume/battery/mic indicators
  Notifications/              Notification center + calendar (two panel windows), toasts (shell mode), focus sessions
  QuickSettings/              Quick Settings: tiles, Wi-Fi, Bluetooth, Accessibility, Cast, Project and Sound output pages
  StartMenu/                  StartMenuWindow, app list, search results, power menu
  Switcher/                   Alt+Tab window switcher (shell mode)
  Snap/                       Win+Z Snap layouts, window snapping by dragging and Win+arrows (shell mode)
  Capture/                    Screenshots: Win+PrtScn, Win+Shift+S snip (shell mode)
  Widgets/                    Widget sidebar, floating widgets, and the widgets (profile, resources, pictures, media,
                              weather, notes, wireless devices)
  Settings/                   Settings record + JSON load/save (%LOCALAPPDATA%\NeoShell\settings.json)
src/NeoShell.Interop/
  Native/                     LibraryImport: User32, Shell32, Dwmapi, Kernel32, Advapi32, PowrProf, Comctl32, Pdh, Hid
  Com/                        Core Audio, IShellItem/IShellItemImageFactory, IPropertyStore, ITaskbarList, ISearchQueryHelper,
                              IQuietHoursSettings
  Windowing/                  MessageWindow, WindowSubclass, AppBar, ShellHook, DwmThumbnail, KeyboardHook, WindowInfo
  Imaging/                    IconBitmap (BGRA pixels), ScreenCapture (screen pixels, clipboard, PNG)
  Tray/                       TrayHost (owns Shell_TrayWnd), NOTIFYICONDATA parsing (32/64-bit)
  Audio/                      AudioEndpoint (volume/mute + events), CaptureMonitor (microphone in use), mixer, outputs
  Network/                    NetworkStatus (WinRT NetworkInformation), WifiNetworks (WinRT WiFiAdapter)
  Radios/                     RadioSwitches (Wi-Fi/Bluetooth radios), AirplaneMode (Radio Management API)
  Bluetooth/                  BluetoothDevices (paired devices)
  Power/                      EnergySaver, BatteryMonitor
  Display/                    DisplayProjection (Win+P modes)
  Accessibility/              StickyKeys
  Performance/                SystemUsage (CPU, disk and memory use through PDH)
  Media/                      NowPlaying (system media transport controls)
  Location/                   DeviceLocation (WinRT Geolocator)
  Wireless/                   WirelessDevices: battery of Bluetooth, Razer and Audeze devices (from WirelessStatus)
  Notifications/              UserNotifications (UserNotificationListener), DoNotDisturb (quiet hours)
  Search/                     IndexSearch (ISearchQueryHelper + OleDb against Search.CollatorDSO)
  Shell/                      AppCatalog (shell:AppsFolder), DesktopFolder, ShellContextMenu, ShellMenu, Launcher, Power,
                              ShellRegistration, StartupApps
tests/NeoShell.Tests/         xunit tests for logic that runs without UI
tools/                        start-shell.ps1 (this session), set-shell.ps1, restore-explorer.ps1
docs/                         design.md, plan.md
```

## Commands

```
dotnet build
dotnet test
dotnet run --project src/NeoShell
src/NeoShell/bin/Debug/net10.0-windows10.0.26100.0/win-x64/NeoShell.exe /exit   # ask the running instance to exit cleanly
```

## Two run modes

1. **Alongside Explorer (development default).** Detected when an Explorer shell is running at startup
   (`GetShellWindow()` returns a window). NeoShell's taskbar reserves its own AppBar space next to Explorer's.
   Do **not** show the wallpaper window, register the Win-key hotkeys, or run startup apps — Explorer owns those.
   The system tray is best-effort in this mode because Explorer owns `Shell_TrayWnd`.
2. **As the shell.** Detected when no shell window exists at startup. NeoShell then calls `SetShellWindow` /
   `SetTaskmanWindow`, signals the shell-ready event, owns `Shell_TrayWnd`, shows the wallpaper, runs startup apps,
   registers hotkeys, and handles session end.

## Safety — read before testing shell replacement

- **Never** set NeoShell as the shell on the main user account. Use a dedicated local test account or a VM.
- `tools/set-shell.ps1` writes only the **per-user** `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell`.
  Never write to HKLM. The one exception: `tools/start-shell.ps1` turns off `AutoRestartShell` (machine-wide), after
  asking the user and from an elevated PowerShell, because otherwise Windows brings Explorer back when it ends
  Explorer's leftover process.
- Recovery: Ctrl+Alt+Del → Task Manager → Run new task → `explorer.exe`, then run `tools/restore-explorer.ps1`.
- Unhandled exceptions must be logged, and when NeoShell runs as the shell its watchdog (`NeoShell.exe /watch <pid>`)
  must start `explorer.exe` after any abnormal exit, so the user is never left on a blank screen. An in-process
  handler alone can't do this: WinUI fail-fasts on exceptions in UI callbacks without raising any event.
- `tools/*.ps1` must work in Windows PowerShell 5.1 (they are run from Task Manager during recovery).

## Widgets (`src/NeoShell/Widgets/`)

A sidebar of widgets on the right of the primary monitor, as Vista's gadgets; widgets can be dragged out to float on
the desktop and back. Full spec in docs/design.md ("Widgets").

- **Who does what.** `Sidebar` (created by `App`) owns everything: the `SidebarWindow` (docked cards, add button and
  right-click menu, resize edge, drop slot), one `FloatingWidgetWindow` per floating widget, the drag/dock logic and
  the shared services (`ResourceMonitor`, `WirelessMonitor`). `WidgetFrame` is the card around a widget: hover
  settings/close buttons and the drag gesture. Each widget is a `WidgetView` subclass (`ProfileWidget`,
  `ResourcesWidget`, `PicturesWidget`, `MediaWidget`, `WeatherWidget`, `NotesWidget`, `WirelessWidget`).
- **State.** `ShellSettings.Widgets` lists every widget (`WidgetSettings`: id, kind, X/Y while floating, floating size,
  the kind's options; unset options take defaults). Docked ones show in list order. Only `Sidebar` writes the list;
  a view saves its own options through `SaveSettings`, and `Sidebar` keeps the stored position and size when it does.
- **Views are disposable.** A new view is made whenever a widget moves between sidebar and desktop, so a view keeps
  nothing that must survive: options go in its settings, text in a file (notes), samples in a shared service. Stop
  timers and events in `Close()`.
- **Adding a widget kind:** a `WidgetKind` value, a `WidgetView` (XAML + code), its title/glyph/`AllowsSeveral` in
  `WidgetView`, a case in `Sidebar.CreateFrame`; anything that talks to Windows goes in `NeoShell.Interop`.
- **Windows and layers.** Sidebar and floating widgets sit just above the desktop (`PinnedLayer.Desktop`); moves don't
  touch the z-order (that walk is too slow per pointer move). The sidebar reserves its space: an app bar alongside
  Explorer, `ShellWorkArea` as the shell (shared with the taskbar; changes go out off the UI thread).
- **Pitfalls found the hard way:**
  - Close widget windows with `Shut()`, never `Close()`: it detaches the subclasses and `WindowClosing.IgnoreMoves`,
    without which WinUI can crash handling a move of a window it's tearing down.
  - Resize a floating window through `AppWindow.Resize` as well as `PinnedWindow.Bounds`: WinUI otherwise restores the
    size it last knew. The widget sits top-aligned in a non-scrolling `ScrollViewer` so it takes its natural height.
  - A `ShellBackdrop` may serve several popups (a menu and its submenus); it shares one controller among them.
  - `PinnedLayer.Desktop` goes just below the lowest *showing* app window: minimized windows (moved to the very
    bottom, below the wallpaper) and cloaked ones are skipped, or a clicked widget sinks under the wallpaper and looks
    closed. Test z-order changes with a minimized window around.
  - Keep periodic work off the UI thread (resource sampling reads adapters and GPU engines on the thread pool): any
    hitch shows as a stutter while a widget is dragged.
  - A card being dragged stays in the tree (lifted: no height, invisible) or it loses the pointer; cards keep their gap
    in their own margin so a lifted card leaves none.
- **Testing live.** Every interactive control has an AutomationId (`Widget` + Name = title, `WidgetSettingsButton`,
  `WidgetCloseButton`, `AddWidgetButton`, `Add<Kind>WidgetMenuItem`, `ShowWidgetPanelMenuItem`, `WidgetResizeGrip`,
  `*ResourceRow*`, `NoteText`…). Widgets behind other windows aren't laid out or rendered: move one into view (its
  X/Y in settings.json, edited only while NeoShell is stopped) before judging its layout. Drags need real mouse input
  (`mouse_event`), not UIA.

## Testing

- Unit tests (xunit) cover non-UI logic: window filtering, grouping, NOTIFYICONDATA parsing, search ranking,
  indexer query building, startup entries, settings, wallpaper style mapping, AppBar rects, desktop icon
  filtering, sorting and grid places, snap zones and keys, thumbnail toolbar data and image lists, widget placement and order, MET forecast parsing, wireless device protocols.
- When testing the UI live, drive it through UI Automation (set `AutomationProperties.AutomationId` on interactive
  controls). Never use global keystrokes like SendKeys: they go to whichever window has focus.
- Always stop a running NeoShell with `/exit` (or its taskbar menu), never by killing the process. A kill leaves the
  AppBar space reserved and, in shell mode, the user without a shell.

## Out of scope

Editing Quick Settings' tiles, Windows 11's Widgets board (Win+W), Task View, pinning items in jump lists, toast
images, buttons and inline replies.
