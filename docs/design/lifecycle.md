# Application lifecycle

Startup, the two run modes, shell mode, startup apps, shell service objects, switching to Explorer, and why UWP apps
can't be the shell. Part of the [NeoShell design](../design.md).

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
2. Create `Shell_TrayWnd` (see [System tray](tray.md#system-tray-tray)) and broadcast `TaskbarCreated`.
3. Signal the shell-ready events (`Local\ShellDesktopSwitchEvent`, `msgina: ShellReadyEvent`, whichever exist) so
   logon completes. Also set `ARW_HIDE` in `SPI_SETMINIMIZEDMETRICS` (not saved, as Explorer does), otherwise
   minimized windows are drawn as small title bars along the bottom of the screen.
4. Start the shell service objects (below), then run startup apps (below), queued at low priority after the tray
   exists.
5. Register hotkeys and the low-level keyboard hook (see [Hotkeys](hotkeys.md#hotkeys-shell-mode-only)).
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

### Switch to Explorer

Start menu button → confirmation dialog → delete the per-user `Winlogon\Shell` value (HKCU only) → start
`explorer.exe` → exit NeoShell cleanly (so the AppBar space and `Shell_TrayWnd` are released first, then Explorer starts).

### UWP (CoreWindow) apps as the shell: not possible (T36)

As the shell, UWP apps (Settings, Calculator, Clock…) and `ms-settings:` links can't work, so NeoShell keeps its
Control Panel fallbacks (`Launcher.OpenSettings`). Packaged desktop apps (Notepad, Terminal, Paint) and WinUI 3 apps
are unaffected. Found on 25H2 (twinui.pcshell 10.0.26100.9444) with cdb, Ghidra and test hosts:

- **What fails.** `ActivateApplication` for Calculator starts `CalculatorApp.exe`, which never creates a window and
  is gone within seconds; the call returns `0x80040900` after ~45 s and TWinUI/Operational logs event 5961
  ("Activation phase: COM App activation"). The app's main thread sits in twinapi.appcore
  `CoreApplication::ActivateForeground` → `GetWindowFactory`, which `CoCreateInstance`s
  `ShellServiceHostBrokerProvider` {3480A401-BDE9-4407-BC02-798A866AC051} (AppID RunAs Interactive User, no server
  on disk: the running shell registers it) and asks it (`QueryService`) for `IApplicationActivationBroker` to get
  the factory for its `CoreWindow`. Nothing serves it without Explorer.
- **Who serves it.** Explorer's immersive shell: twinui.pcshell's `CImmersiveShellBuilder` (CLSID
  {C71C41F1-DDAD-42DC-A8FC-F5BFC61DF957}, `IImmersiveShellBuilder` {1C56B3E4-E6EA-4CED-8A74-73B72C6BD435}:
  `CreateImmersiveShellController`; `IImmersiveShellBuilder2` {2EB59B15-1487-40CE-916E-EF65330DD224}:
  `SetShellScenario`) makes windows.immersiveshell.serviceprovider's `CImmersiveShellController`
  ({23650F94-…}; `Start`, `Stop`, `SetCreationBehavior`), whose components thread creates `CApplicationManager`
  (view management, frames with ApplicationFrameHost, PLM), `CLSID_ImmersiveShell` and the broker provider. Explorer
  starts it from `CTray::_StartImmersiveShell`; the only other callers are Microsoft's own shell hosts:
  CustomShellHost.exe (Shell Launcher v2 and Assigned Access: it runs only when
  `CustomShellExperienceRepository.ActiveExperience` finds one of those configured, otherwise it starts Explorer;
  scenario 0), ShellAppRuntime.exe (Windows 365 Boot) and rdpinit.exe (RemoteApp). Each creates its own `Progman`
  desktop window, and optionally `Shell_TrayWnd`, in the same process before `Start`.
- **Two gates keep it out of NeoShell.** Tried with a test host calling the builder exactly as CustomShellHost does:
  1. `CImmersiveShellController::Start` fails with `RPC_E_WRONG_THREAD` (immersiveshellcontroller.cpp line 926)
     unless `GetShellWindow()` belongs to the calling process, so it can't live in a helper next to NeoShell; with
     no shell window at all it fails the same way.
  2. Given a shell window of its own (`SetShellWindow` in the host), `Start` succeeds, but the components thread
     fail-fasts the process (`CCriticalFailureHandler`, `0x80270233`) in `CApplicationManager::
     _InitializeIAMSubcomponents`: `CFallbackWindow` creates `ApplicationManager_ImmersiveShellWindow` with
     `CreateWindowInBand(…, ZBID_IMMERSIVE_BACKGROUND = 12)`, and the view manager needs `SetWindowBand` and the
     shell cloak (`DwmSetWindowAttribute(DWMWA_CLOAK)` with user32 ordinal 2510 around it) for every app frame.
     win32k allows these only to an immersive broker (`win32kbase!IsImmersiveBroker`: DWM, CSRSS, or a process
     `UserProcessImmersiveType` marks at creation because its image has a `.imrsiv` PE section and a Windows
     signing level). explorer.exe, CustomShellHost, ShellAppRuntime, rdpinit, ShellHost and ApplicationFrameHost
     have that section; NeoShell can't (it needs Microsoft's signature). Measured from a medium-integrity test
     process: `CreateWindowInBand` with bands 2, 3 and 12 and `SetWindowBand(…, 12)` fail with access denied (5).
- **Rejected alternatives.** Configuring Shell Launcher/Assigned Access so CustomShellHost hosts it (Enterprise/
  Education/IoT features, machine-wide kiosk policy, and the host would own `Progman` and `Shell_TrayWnd`);
  starting ShellAppRuntime (the Windows 365 Boot shell); `explorer.exe /factory` (the controller still needs the
  shell window in that process); serving `IApplicationActivationBroker` and ApplicationFrameHost's frame interfaces
  from NeoShell (the app's `CoreWindow` is created shell-cloaked, and uncloaking, banding and placing it are the
  broker-only calls above). What does work is Explorer as the shell with NeoShell alongside.
