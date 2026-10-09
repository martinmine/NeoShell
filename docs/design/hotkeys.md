# Hotkeys, taskbar search and context menu

The Win+ hotkeys and keyboard hook (shell mode), search on the taskbar and the taskbar's context menu. Part of the
[NeoShell design](../design.md).

## Hotkeys (shell mode only)

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
  - Shift+S the screen snip (also Print Screen alone, through the hook), PrtScn a screenshot of the whole screen, Z
    Snap layouts (see [Screenshots](capture.md#screenshots-capture), [Snap
    layouts](windows.md#snap-layouts-snap)). A window opened from a hotkey is brought to the front with `SetForegroundWindow` after WinUI shows it:
    WinUI's `Activate` leaves the foreground with the app the keys went to.
- Quick Settings' shortcuts — Win+A (tiles), Win+Ctrl+V (Sound output), Win+K (Cast), Win+P (Project) — can't be
  registered: Windows' own Quick Settings host (ShellHost) keeps them after Explorer has gone, and would open its
  panel. The hook takes them instead (`PanelKeys`, unit tested): the letter is swallowed (down, repeats, up)
  and an unassigned key (vkE8, as AutoHotkey's menu mask key) is injected while Win is still down, so letting go of
  Win doesn't count as Win alone (which Windows sends to the shell as `SC_TASKLIST`, opening Start). Pressing the
  shortcut of the page shown closes Quick Settings; another page's switches to it. Win+N (the notification center
  and calendar, toggled) and Win+X (the Quick Link menu) are taken the same way. With Shift or Alt held the letters
  are left alone: Win+Alt+K is the microphone's.
- Win+Space switches the input method (see [Input indicator](indicators.md#input-indicator-trayinputswitchpanel-interop-inputinputmethods)): nothing answers it without Explorer (Win+Space
  stays registered, but Explorer's input switcher behind it is gone). The hook takes it (`InputSwitchKeys`, unit
  tested): each Space while Win is held is swallowed and moves on (back with Shift; not with Ctrl or Alt), the
  first opens the switcher with Win masked as for Quick Settings' keys, and letting go of Win switches. Alt+Shift
  needs no shell: Windows switches by itself.
- Win+Comma peeks at the desktop while Win is held (`PeekKeys`, unit tested, through the hook: a hotkey can't see
  Win let go of): Aero Peek at the taskbar, a window left out of peeking like the wallpaper, so only those show.
  The comma is swallowed and Win masked as for Quick Settings' keys.
- Start closed because another window took the foreground (deactivation) doesn't hand the foreground back to the
  previous app — that would take it from the window being activated, e.g. the taskbar for Win+T.
- The Copilot key (`CopilotKey`, unit tested): keyboards send it as Win+Shift+F23; Win+C does the same (Settings says
  "the Copilot key or Windows logo key + C"). Both are hotkeys Windows keeps for the shell whether Explorer runs or
  not (`RegisterHotKey` fails with 1409 without Explorer), and delivers to the immersive shell's hotkey service in
  Explorer (`IMMERSIVE_HOT_KEY_ID` 0x70 and 0x6F), so the hook takes them as Quick Settings' keys (`PanelKeys`: F23
  or C swallowed, Win masked). What Explorer does (twinui.pcshell `CopilotHotkeyManager::InvokeCopilotOrCustomOption`
  and `TryInvokeCopilotOrCustomAppFromHardwareKeyAsync`, read in Ghidra) is set in Settings → Bluetooth & devices →
  Keyboard → "Customise Copilot key on keyboard", stored in `HKCU\Software\Microsoft\Windows\Shell\BrandedKey`
  (a protected key: only Settings may write it, other processes get access denied):
  - `BrandedKeyChoiceType` "App" (or "AppEnforcedByPolicy", from the `SetCopilotHardwareKey` policy) with `AppAumid`:
    that app. If it has a window, the window comes to the front (`SwitchToThisWindow`), or is minimized
    (`SC_MINIMIZE`) when it's in front already; otherwise the app is started (`IApplicationActivationManager`, no
    arguments). Apps declaring the `com.microsoft.windows.copilotkeyprovider` extension get a URI and press-and-hold
    signals instead; NeoShell starts them as any app.
  - "Search": Windows Search with `QuerySource=HWCoPilot`, or closed if it's open. NeoShell opens Start with its
    search box (as Win+S), or closes it.
  - No BrandedKey key at all: the Copilot app (`Microsoft.Copilot_8wekyb3d8bbwe!App`).
  - Anything else, or the app failed to start: in regions whose policy asks for it (the EEA, this VM's Norway)
    Settings opens on the key's setting ("The Copilot key isn't connected to an action"), elsewhere search. Settings
    is a UWP app and can't show without Explorer, so NeoShell searches. Compared on the VM with the choice left at
    an uninstalled Microsoft 365 Copilot ("None selected"): Explorer opened Settings' page for both keys, NeoShell
    opens and closes Start's search; neither types F23 or C into the app in front.
- Not done: Task View and virtual desktops (Win+Tab, Win+Ctrl+D/F4/arrows; out of scope, and the desktops live in
  Explorer) and Widgets (Win+W). These can't be done without Explorer (studied on 25H2 for T30):
  - **Win+V, Win+Period / Win+Semicolon, Win+H** (clipboard history, emoji panel, voice typing). They are ordinary
    hotkeys of Explorer's (`CTray::_HandleClipboardViewerHotKey`, `_HandleExpressiveInputHotKey`,
    `_HandleDictationHotKey`), and free without it. Each asks the immersive shell (`QueryService` on Explorer's
    service provider, SID = IID {9516D866-CA0F-40CA-8997-EFA618B50F99}, `ITouchKeyboardExperienceManager`) to show a
    view of the input app: method 4 with 1 (emoji), 2 (dictation) or 3 (clipboard). That manager, twinui.pcshell's
    `TouchKeyboardExperienceManager2`, starts TextInputHost.exe (`MicrosoftWindows.Client.CBS_cw5n1h2txyewy!InputApp`,
    a composable CoreWindow app) and shows its panels inside frames of its own: the panel seen on screen is an
    `ApplicationFrameWindow` of explorer.exe in window band 3 (`ZBID_IMMERSIVE_IHM`), while TextInputHost's own
    `Windows.UI.Core.CoreWindow` stays full-screen and shell-cloaked (DWM cloak 2). Without Explorer nothing hosts
    them: Win+Period just types a period, activating the InputApp directly hangs (`ActivateApplication` never
    returns, TextInputHost doesn't start), and band 3 is Explorer's alone (`CreateWindowInBand` from another
    medium-integrity process: band 1 and 16 work, bands 2, 3, 4, 7 and 13 fail with access denied). The public
    `CoreInputView.TryShow(Emoji/Clipboard/Dictation)` asks the same manager.
  - **Win+Shift+R** (Snipping Tool's screen recording). Explorer (`CScreenClippingExperienceManager::LaunchSnippingToolApp`)
    only opens `ms-screenclip://?source=ScreenRecorderHotKey&type=recording` when Snipping Tool is the `ms-screenclip`
    handler; Snipping Tool draws its overlay itself (an ordinary topmost `XamlWindow`). Without Explorer it starts,
    shows nothing and idles: watched with cdb, its `GraphicsCaptureItem.TryCreateFromDisplayId` fails with access
    denied and `CreateForMonitor` with "Could not capture the given monitor" (E_INVALIDARG). Monitor capture through
    Windows.Graphics.Capture needs Explorer for every app: CaptureService asks the `Windows.Internal.CaptureItemProvider`
    contract (Windows.Internal.CapturePicker.Desktop.dll, `CaptureDesktopItemProviderImpl`), which finds monitors
    through the immersive shell's `IImmersiveMonitor` services in Explorer. A test app's
    `IGraphicsCaptureItemInterop::CreateForMonitor` returned E_INVALIDARG with NeoShell as the shell and an item
    ("Display 1 1764x988") with Explorer. Snips and recordings of the screen by Snipping Tool (Win+Shift+S too, see
    Screenshots) therefore can't work as the shell; Win+Shift+R is left unregistered rather than start a Snipping
    Tool that idles.

## Search on the taskbar (`TaskbarSearch`, unit tested)

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
  back to 1 when there's no room, `IsSpaceAvailableForSearchBox`) and expand again when there's room: once labelled
  buttons don't fit beside it even at their narrowest, 84 px (see [Task buttons](taskbar.md#task-buttons); `TaskbarSearch.Shown`).
- **Search highlights** (the "gleam" picture at the box's end, `IsDynamicSearchBoxEnabled`) are Bing content served
  to Windows Search through no public API, and are off on this VM; NeoShell doesn't show them.
- Centred, the slot sits between Start and the task buttons as left-aligned; the centred Start slot is 45 px, as
  Explorer's (T39a; the group sat a pixel left before). NeoShell switches looks without animating.
- A click opens Start with its search box focused, as before, in both run modes; Explorer opens its
  own Search window instead (T21: Start's search stays).

## Taskbar context menu

Task Manager, the taskbar settings toggles (alignment, search, combine, backdrop, auto-hide, hidden icon menu, all displays),
Exit NeoShell (alongside Explorer only).
