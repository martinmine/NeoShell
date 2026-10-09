# Taskbar

The taskbar window (placement, display changes, flyouts and menus, the Quick Link menu), its layout, window tracking
and task buttons. Part of the [NeoShell design](../design.md).

## Taskbar (`Taskbar/`)

### Window

- One `TaskbarWindow` per monitor (setting: show on all displays), bottom edge, 48 effective pixels high, kept in
  place and topmost by `PinnedWindow`, frameless (`FramelessWindow`), `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
- Screen space:
  - Alongside Explorer: registered as an AppBar with `SHAppBarMessage` (`ABM_NEW`, `ABM_QUERYPOS`, `ABM_SETPOS`,
    `ABM_REMOVE`); Explorer places it above its own taskbar and sends `ABN_POSCHANGED` when it must move.
  - As the shell: `SHAppBarMessage` is served by the shell's own `Shell_TrayWnd`, so the taskbar can't use it.
    NeoShell sets the monitor's work area itself (`SPI_SETWORKAREA`) and restores it on exit. `ShellWorkArea` keeps
    what the taskbar, the widget sidebar and other apps' app bars (see [App bars](tray.md#app-bars-trayappbarscs-trayappbarlayoutcs-interop-trayappbarmessagecs) under System tray) reserve per
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
  - **Display changes.** When monitors come or go, and when a monitor's resolution changes, win32k makes new monitor
    objects (new `HMONITOR`s, the primary's too) whose work area is the whole monitor, so nothing stays reserved
    (seen on the VM with no shell running). Explorer copes because `CTray::RecomputeWorkArea` builds each area from
    `GetMonitorInfo`'s `rcMonitor` less the taskbar and the bars, and compares it with that monitor's *current*
    `rcWork` (`EqualRect`), not with what it set last; on top of that 25H2 has a feature-flagged private path
    (`CTray::FillDisplayChangeData`, `CanPredictWorkAreas`, `TaskbarSyncWorkAreaCalculation`) that hands win32k the
    work areas during the mode change, which NeoShell can't use. NeoShell does the same comparison: a change going
    out is checked against Windows' work area right before it's set (`WorkArea.Get`), skipped if it's already so,
    and skipped for a monitor that's gone (setting one fails with `ERROR_INVALID_PARAMETER`; a removed monitor's
    taskbar gives its strip back after the monitor left). Every reservation goes out again after the taskbars are
    remade for the change (`ShellWorkArea.SendAgain`), not before: a bar told by the broadcast would otherwise place
    itself while no taskbar strip is reserved (between the old taskbar closing and the new one opening) and end up
    over the taskbar. Measured on the VM with a maximized window and a left test bar: unplugging and plugging the
    second monitor, resolution changes on either and DPI changes (100/125/150%) on either leave every work area,
    bar and maximized window as Explorer's (plus the sidebar's strip); Explorer has the primary's area right at once
    and refits windows in about 2 s, NeoShell in about 1 s after the change. DPI changes don't reset work areas.
    Sometimes (with the test app running) NeoShell's windows got `WM_DISPLAYCHANGE` up to 9 s late, its UI thread
    idle meanwhile; not explained.
- Rect calculation (unit tested) from monitor bounds and DPI.
- Recreated on `WM_DISPLAYCHANGE`, on `WM_DPICHANGED` to a DPI other than the monitor's, and on settings changes.
  An old taskbar window closes as widget windows do (`TaskbarWindow.Shut`, see [Widgets](widgets.md#widgets-widgets)): its open menus and
  flyouts are closed at once first and forgotten (`TaskbarFlyouts.CloseAll`: a slide would go on moving a closed
  window's popup, and the flyouts were kept, with each old taskbar, for good), then its subclasses go and moves no
  longer reach WinUI (`WindowClosing.IgnoreMoves`), its preview window likewise. T1 saw an access violation in
  Microsoft.UI.Xaml.dll when auto-hide was switched through UI Automation with the menu still open; not seen again in
  T40 (the menu, a submenu, the overflow, Quick Settings, a jump list, the network and volume menus and the previews
  open while the taskbars were remade, alongside and as the shell, with and without auto-hide).
- Auto-hide's timer starts with the window, and as the shell its first tick can come before the content's first
  layout: asking WinUI for the open popups of a null `XamlRoot` threw, and an exception in a timer's tick fail-fasts
  (NeoShell ended at every start as the shell with auto-hide on, found in T40 from the dump's stowed exception). The
  check waits for the `XamlRoot`.
- Full-screen apps: while the foreground window covers its monitor (or the app marked it with
  `ITaskbarList2::MarkFullscreenWindow`), that monitor's taskbar leaves the topmost band and sits just below it.
  Maximized windows, minimized ones, NeoShell's own and the desktop (shell window, `Progman`, `WorkerW`) don't
  count. Re-checked on foreground changes, minimize/restore and the foreground window moving (`EVENT_OBJECT_LOCATIONCHANGE`).
  Explorer would send `ABN_FULLSCREENAPP`; NeoShell is the one deciding, alongside Explorer too.
- Auto-hide (setting): no screen space is reserved (no AppBar, no work area change). The taskbar slides down until
  2 pixels show; the pointer on them slides it back, whatever window is over them (T39d): Explorer's taskbar goes by
  where the pointer is, not by its own window getting the mouse (Taskbar.dll's `TrayUI::SetUnhideTimer` takes the
  pointer's position and starts a 50 ms unhide timer), so it comes back under a topmost window over the bottom edge
  too (measured: a topmost 900x400 window reaching past the bottom; the window stays above the revealed taskbar
  there, NeoShell's taskbar goes above it), but not under a topmost window covering the whole monitor (a full-screen
  one, or with auto-hide a topmost maximized one), nor for a full-screen app. NeoShell polls the pointer every 50 ms
  while hidden (`TaskbarLayout.RevealsAt`, tested; `TopLevelWindows.IsTopmostWindowCovering`). With the widget
  sidebar shown a maximized window doesn't cover the monitor, so the taskbar comes back over it. It hides again after ~0.75 s with the pointer off it,
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
- **They close when another app is clicked**, as Explorer's (T39a). Explorer's take the foreground: a jump list, the
  notification center and the calendar are ShellExperienceHost windows, Quick Settings ShellHost's
  `ControlCenterWindow`, the tray overflow its own `TopLevelWindowForOverflowXamlIsland`, and for the network and
  speaker menus `Shell_TrayWnd` itself becomes the foreground window; each closes when it loses the foreground (a
  click on any other app, the one that was in front too). Explorer leaves the foreground with the taskbar when a menu
  closes some other way, and the app's button loses its active look meanwhile. NeoShell's taskbar is no-activate,
  so `TaskbarFlyouts` activates it for each flyout and menu it opens (`SetForegroundWindow`: the click on the taskbar
  was the last input; after a hotkey such as Win+A, where Windows' foreground lock refuses, as Alt+Tab does,
  `TopLevelWindows.SwitchTo`), and the taskbar's `Activated` handler closes them (`TaskbarFlyouts.HideAll`, sliding
  out) when it's deactivated, and goes back to no-activate. Clicks inside a flyout's popup window don't activate
  anything, so they keep it open. The notification center and calendar (`ClockFlyout`) already closed on losing the
  foreground; Win+N now takes it the same way. Opened from the keyboard, WinUI puts the focus on the flyout's first
  control and shows that control's tooltip; Explorer shows the focus without one, so tooltips open at that moment
  are closed.
- **Moved by the popup's offset, not only by moving its window.** WinUI places a flyout inside the monitor's work
  area (which leaves out the taskbar, and the widget sidebar as the shell), but it doesn't keep a popup's own
  `HorizontalOffset`/`VerticalOffset` inside it: a flyout is moved where it belongs by adding to its `Popup`'s offset
  once WinUI has placed it. WinUI moves the window there itself only 50-170 ms later, so the window is moved there at
  once too. Moving only the popup window with `SetWindowPos` (as NeoShell did until T40) leaves WinUI's idea of where
  it is behind: UI Automation reported the content where WinUI had put it (T4 saw the tray overflow's icons 360 px
  left of where they were drawn; measured in T40, a jump list's items about half its width right, the taskbar menu's
  38 px too high). Measured with UIA against the popup windows' rectangles, alongside and as the shell with the
  sidebar open: they now match to the pixel.
- The taskbar's own menu is the exception, as in Explorer: its bottom-left corner is at the pointer, over the taskbar,
  and it opens with WinUI's own animation instead of sliding. WinUI places it inside the work area, so it opens at the
  taskbar's top edge and its popup window is subclassed to go the rest of the way down whenever WinUI places it
  (`PopupWindows.Offset`), until it stays put (WinUI places it twice on a menu's first opening): then its popup's
  `VerticalOffset` takes the rest and WinUI places it, and its submenus beside it, itself. (Submenus go inside the
  work area, above the taskbar; Explorer's menu has none.)
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
  A flash of a flyout's first opening for one frame (noticed in T40) didn't show in 60 fps recordings of the first
  opening of Quick Settings, a jump list and the network menu after a start (T39a).
- The taskbar's menus (jump lists, the taskbar menu, network and speaker menus, the Quick Link menu) get a
  `ShellBackdrop` of their own with a see-through presenter: WinUI's menu backdrop turns solid while the menu's
  window is inactive, as a menu of the no-activate taskbar always is, where Explorer's stay acrylic. Submenus keep
  WinUI's (no way to give them a backdrop).
- A right-click on a task button stops its previews: none open while its menu is, and the button shows none again
  until the pointer has left it (a hover that began before the click would otherwise open them over the menu).
- **Tooltips** (`TaskbarToolTips`, T39a), as Explorer's (measured on its clock, speaker, chevron, input indicator
  and Start tooltips): centred on the pointer (a half pixel going left), their bottom edge 12 px above the taskbar's
  top wherever the pointer is on it, and kept inside the monitor, not the work area, so they show over the widget
  sidebar's space. WinUI keeps a tooltip inside the work area and puts it by the pointer's height, so each of the
  taskbar's is a `ToolTip` whose popup's offsets are set once it has its size (not yet at `Opened` on a first
  showing) and whenever the size changes (the clock's seconds); WinUI doesn't keep popup offsets inside. Not matched:
  Explorer's tooltips are acrylic in the taskbar's colours, NeoShell's are WinUI's own.
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
- Title bar shake and Win+Home are one command in Explorer (`CTray::_ShakeTriggered`, trigger 1 the key, 2 the
  shake). Windows itself has no shake: uxtheme, in every themed app's own process (`CShakeWnd`,
  `OnPreWindowMovingShakeHandler` on `WM_MOVING`), spots it and posts `0x4F2` (lParam: the window) to
  `FindWindow("Shell_TrayWnd")`, after `AllowSetForegroundWindow` for it; Explorer lets that message through UIPI
  (`ChangeWindowMessageFilterEx`). As the shell NeoShell owns `Shell_TrayWnd`, so it gets the message and does the
  same (`TrayHost.WindowShaken`). uxtheme's detection (constants read from its data): strokes start above 600 px/s
  between `WM_MOVING`s and end below 600 px/s; a stroke 1–2000 px long, at least 157.5° from the one before
  (cos² ≥ cos²(157.5°)) and within 5× its length counts; three such strokes within 1 s of the first's start trigger;
  a pause of 0.25 s between moves starts over, and after a trigger nothing more until a 0.4 s pause. Esc while the
  window is still held posts the message again (a GetMessage hook), undoing it. Both uxtheme and Explorer read
  `DisallowShaking` (HKCU …\Explorer\Advanced) with `SHRegGetBoolUSValue` defaulting to true: absent means off,
  Windows 11's default; Settings' "Title bar window shake" writes 0 (on) or 1. The policy
  `NoWindowMinimizingShortcuts` (Software\Policies\Microsoft\Windows\Explorer) turns off the shake but not
  Win+Home. Explorer then checks the window with uxtheme's `IsValidShakeWindow` (ordinal 86: a top-level window
  with no owner or `WS_EX_APPWINDOW`, not the taskbar or the desktop) — the foreground window for Win+Home, so Win+Home
  with the desktop in front does nothing — minimizes every window but its root owner, and remembers it: the same
  again for the same window restores the others behind it (it stays on top; NeoShell restores them with
  `SWP_ASYNCWINDOWPOS` just below it); for another window it minimizes all but that one. Win+D/Win+M forget it.
  Both minimize and restore with Windows' own animation to and from the taskbar button (recorded at 60 fps, ~150 ms).
- Minimize and restore animations need the shell: win32k sends `WM_KLUDGEMINRECT` (0x8B) to every shell hook window
  (100 ms timeout), whose `DefWindowProc` turns it into `HSHELL_GETMINRECT` with a `SHELLHOOKINFO`; user32 reads the
  rectangle back as four 16-bit values, which Explorer's `CTaskBand::_HandleGetMinRect` writes (not the RECT the
  documentation has). Without an answer windows minimize and restore with no animation; as the shell NeoShell
  answers with the window's taskbar button (`ShellHook.MinimizeRect`, `TaskbarWindow.TaskButtonBounds`).

### Layout (left → right, or centred like Windows 11 by setting)

1. Start button.
2. Search — the icon, a search box, or the icon with a label, or nothing: Explorer's search setting (see [Search on the
   taskbar](hotkeys.md#search-on-the-taskbar-taskbarsearch-unit-tested)). Opens the Start menu with the search box focused.
3. Pinned and running apps.
4. Tray area: chevron/overflow, tray icons.
5. Indicators: privacy (microphone or location in use), the input method (with more than one: an IME's mode, then the language),
   then network, volume and battery as one button (Quick Settings).
6. Clock: time (with seconds by setting) and short date, then the notification bell; tooltips with the full date
   and the day and time, and the count of new notifications; click opens the notification center and calendar (see
   [Clock and notification bell](indicators.md#clock-and-notification-bell-taskbarclock-clocksettings-clockdisplay-unit-tested), and [Notifications and calendar](notifications.md#notifications-and-calendar-notifications)).
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
- Sized as Explorer's: 44×48 buttons with a 40×40 plate behind the 24 px icon, shown when hovered or active (with
  several windows a 35 px plate, 2 px, then the 3 px edge of a second card; the edge's gap is its own margin, as a
  grid's column spacing took 2 px off a single window's plate, which drew the centred group a pixel left).
- **Labelled buttons** (never combined, or combined when full and there's room), measured on Explorer with UI
  Automation and screenshots (T39a): the icon 10 px in, the label 8 px after it and 10 px short of the button's
  right, so a button is 52 px wider than its label (`TaskbarFit`, unit tested), up to 180; the pills under the icon,
  not the button's middle. As the taskbar fills, all labelled buttons narrow together, each keeping the same share
  of what it has above 84 px (Explorer's widths at each step were matched within UIA's whole pixels: 174/90/95/99/113
  for eight windows, then 143, 122, 106, 94 as windows were added); the labels are cut off, not ended in an
  ellipsis. When they don't fit even at 84, the search box (or icon and label) collapses to the icon and they widen
  again; at 84 once more, Explorer shrinks its icons (16 px icons in 32 px slots, labelled buttons from 76 px:
  "smaller taskbar buttons when full") and then moves buttons into an overflow menu ("..."); NeoShell does neither
  and cuts the row off. "Combine when full" combines once the uncombined buttons don't fit even at their narrowest
  beside the search icon; Explorer combines group by group, after shrinking its icons (not done).
- **Room** (measured): left-aligned, the Start slot is 11 px in and the buttons go up to 12 px short of the
  right-hand panel (the chevron); centred, the row is in the middle of the screen, moved left as far as it must for
  its right end to stay 58 px short of that panel, and from the screen's left edge once it fills that room; the
  centred Start slot is 45 px (the button's extra pixel on its right). NeoShell places the row by its left margin
  (`PlaceAppsPanel`) and caps the list at the room. Side by side as the shell, NeoShell's row matched Explorer's
  positions and widths at every step up to Explorer's smaller buttons.
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
  `TopLevelWindows` then posts the system menu's command (`WM_SYSCOMMAND` with `SC_RESTORE` or `SC_MINIMIZE`), which
  gets through. This covers the taskbar, previews, Show desktop and Alt+Tab.
- **What gets through to an elevated window, and what can't** (T40, measured from medium integrity against Task
  Manager, and read in win32kfull's `CheckForMessageAccessCrossIL`): `WM_SYSCOMMAND` passes UIPI only when `wParam`
  is exactly `SC_MINIMIZE`, `SC_CLOSE` or `SC_RESTORE` (`(wParam - 0xF020) & ~0x140 == 0`, `0xF160` excluded);
  `SC_MAXIMIZE` (and `0xF032`, the caption double-click's), `ShowWindow(Async)(SW_MAXIMIZE)`, `SetWindowPos`,
  `SetWindowPlacement` and a posted `WM_NCLBUTTONDBLCLK` are refused with access denied. Of the other messages,
  `IsMessageAlwaysAllowedAcrossIL` lets `WM_POPUPSYSTEMMENU` (0x313) through: Explorer's Shift+right-click on a task
  button makes the window show its *own* system menu, so Maximize there works (the menu belongs to the elevated
  process, and input to it from a lower level is dropped too). Explorer snaps and maximizes elevated windows (Win+Up,
  Snap) through user32's `ShellSetWindowPos` (imported by twinui.pcshell), which win32k allows only to the immersive
  broker (`NtUserShellSetWindowPos` checks `IAMThreadAccessGranted`), so not to NeoShell (see [UWP (CoreWindow) apps
  as the shell](lifecycle.md#uwp-corewindow-apps-as-the-shell-not-possible-t36) for the immersive broker). T39d found that the shell window's process can take that access itself
  when no Explorer holds it (see Snap's "Window motion" in [Window snapping](windows.md#window-snapping-windowsnapping-shell-mode)), which NeoShell now does as the shell for Snap's animation;
  with it `ApplyWindowAction` skips the integrity check, so elevated windows could be snapped too — not done yet:
  Snap still leaves them out.
  (`NtUserMinMaximize` in win32u does no UIPI check at all, but an undocumented system call that sidesteps UIPI isn't
  something to build on.) So NeoShell can't maximize, size or move an elevated window: Snap leaves such windows out
  (`TopLevelWindows.IsOfHigherIntegrity` compares the process's mandatory label with NeoShell's) — no snap preview,
  Snap bar, layouts flyout or Win+arrow for one, and none in Snap Assist or the layouts' suggestions — rather than
  promise a snap that doesn't happen. Windows itself still maximizes one dragged to the very top of the screen
  (within 6 px, which Explorer suppresses), and its own title bar and system menu work as ever. Only a NeoShell with
  `uiAccess` (signed and installed under Program Files) could do more. NeoShell's keyboard hook doesn't see the keys
  while an elevated window has them anyway (UIPI), so Win+arrow never reached one.
