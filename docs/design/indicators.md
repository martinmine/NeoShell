# Indicators

Network, volume and battery, the privacy and input indicators, the clock and notification bell. Part of the [NeoShell
design](../design.md).

## Indicators (`Tray/`)

### Network

- `NetworkInformation.GetInternetConnectionProfile()` + `NetworkStatusChanged`.
- States: Ethernet, Wi-Fi (`WlanConnectionProfileDetails`, `GetSignalBars()` 0–5), cellular, no internet access,
  disconnected. Each maps to a Segoe Fluent Icons glyph; airplane mode shows the plane instead, even with a cable
  still connected, as Explorer does.
- Tooltip: network name and access status ("Airplane mode" while it's on). Right-click menu, Explorer's (T39a),
  separated by lines: Diagnose network problems (U+E90F, Get Help's troubleshooter:
  `ms-contact-support:///?ActivationType=NetworkDiagnostics&invoker=SystemTrayIcon`), Perform speed test (U+F42F, Bing
  in the default browser: `https://www.bing.com/search?q=Internet%20speed%20test&form=wspeed2`), Network and Internet
  settings (U+E713, `ms-settings:network`; shell mode `ncpa.cpl`). Read from the processes Explorer started; both
  links work as the shell too. Glyphs matched by correlating Explorer's 16 px icons with Segoe Fluent Icons' glyphs.
- Wi-Fi's one to four bars are Explorer's U+EC3C to U+EC3F (NetworkIcon.dll's Wifi1Bar…Wifi4Bars), drawn on all four
  bars at `SystemBaseLowColor` (20%) so the missing ones show, as SystemTray's Underlay layer does.

### Volume

- `IMMDeviceEnumerator` → default render endpoint → `IAudioEndpointVolume` with `IAudioEndpointVolumeCallback`
  (`AudioEndpoint`).
- `IMMNotificationClient` to follow default-device changes.
- Icon reflects mute and level (0 / low / medium / high glyphs, U+E992 to U+E995) on all three waves (U+EBC5) at 20%,
  as Explorer's Underlay; muted is U+EA85 (the speaker with a crossed circle), no device U+E74F (SystemTray.dll's
  strings). Mouse wheel over the button changes volume in 2% steps.
- Right-click menu on the icon, as Explorer's (T39a): Troubleshoot sound problems (no glyph; Get Help's
  `ms-contact-support://windows-speaker-icon/`, which works as the shell too), a line, Open volume mixer (U+E713,
  `ms-settings:apps-volume`; shell mode the classic `sndvol.exe`) and Sound settings (U+E713).
- Both menus open at the right of the screen as Quick Settings does, not above the icon: their right edge 13 px from
  the screen's, their bottom 13 px above the taskbar (the same windows' places as Explorer's, to the pixel).

### Battery and energy saver

- `Battery.AggregateBattery` (WinRT) and its `ReportUpdated` (`BatteryMonitor`): shown only on PCs with a battery;
  tooltip "Battery: 54% remaining".
- The icon is Explorer's (Interop `Power/BatteryIcon`): `WindowsUdk.UI.Shell.PowerUX.BatteryIcon` in
  windowsudk.shellcommon.dll works out the outline (U+F8D0; with a bolt, U+F8DB, while charging or full on power; a
  plug, U+F8E4), the charge over it and the charge's colour: the text colour, or SystemTray's green (#9FD89F dark,
  #107C10 light), yellow (#EAA300) or red (#D92C2C, #C50F1F). Its `GetMobileIconData` has the glyphs of SystemTray's own
  font, SysBatt Fluent Icons (`SystemTray\Assets\Sysbatt.ttf`, 20 px wide at 16 px), which WinUI loads only from the
  app's folder: the build copies it from the PC's Windows into `Assets`. Without the class or the font, Segoe Fluent
  Icons' battery in tenths (U+EBA0…, with the plug U+EBAB…).
- Energy saver's leaf shows after the volume while energy saver is on and there's no battery icon (a desktop).

### The button

- Network, volume and battery are one flat button with one hover plate, as on the Windows 11 taskbar; each icon is
  a cell with its own tooltip and right-click menu (the cell under the pointer decides; from the keyboard, the
  speaker's). Clicking opens Quick Settings at the screen's right edge; clicking again closes it. While Quick
  Settings is open the button keeps its hover plate (`QuickSettingsOpenPlate`), as Explorer's (and its input
  indicator's, T8); hovered then, Explorer's plate gets a little lighter, NeoShell's doesn't (the open flyout's
  light-dismiss layer keeps the pointer from the button).
- **Spacing** (T39a, measured with hover boxes alongside and as the shell): Explorer's chevron (32), tray icons (32
  each), input indicator (44), this button (60 with network and speaker) and the clock sit right against each other
  and against Show desktop: 1506, 1538, 1570, 1614, 1674 and 1752 on this VM's 1764 px screen, which NeoShell now
  matches to the pixel (its buttons had 2 px margins, which put the tray 5 px left).
- **Cells** (re-measured at 150% with UI Automation and screenshots side by side): each glyph 4 in from its cell's sides
  and 4 between cells, so 24 per icon and 28 for the battery (its glyph is 20 wide); the plate 4 more each side (92 with
  network, speaker and battery), 40 high, corner radius 4, glyphs 16 below the taskbar's top. Hovered, pressed or open,
  the plate has a faint top edge 1 high (SystemTray's `ShellTaskbarItemStrokeColorQuinary`, #0AFFFFFF dark, #05000000
  light, over `SubtleFillColorSecondary` or `Tertiary`), drawn as a gradient in the plate's background.

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
- **Layout** (96 DPI, measured with UI Automation and screenshots; re-measured side by side in T39a): one hover box,
  40 px tall, around both parts, right against Show desktop (78 px wide for "09/10/2026" without the bell); the
  text 8 px in from the box's left and 7 from its right, right-aligned, a pixel above the middle (time ink 12–19 px, date 28–37 px below the taskbar's top); the bell 24 px wide, 4 px after the
  clock part, its 16 px glyph centred. One button in NeoShell (Explorer has two, the clock and the bell, sharing
  the hover box); each part has its own tooltip, and both open the notification center and calendar. Explorer's
  system XAML rounds text widths up where WinUI rounds them to nearest, so the box can be a pixel narrower and the
  time a pixel to the right.
- **Bell** (`ClockDisplay.Bell`): shown when `ShowNotificationIcon` is on, and always while Do not disturb is. Glyphs:
  U+F2A3 (outline) with no new notifications, U+F2A5 (filled) in the accent's text colour with some (Light 3 in dark,
  Dark 2 in light: `AccentTextFillColorPrimaryBrush`); with Do not disturb U+F285 / U+F2A8, in the text colour. No
  animation: the glyph swaps in one frame. Tooltip (and UIA name after "Notifications"): "No new notifications",
  "1 new notification", "N new notifications", plus " (Do not disturb on)".
- **Open**: while the notification center and calendar are open, the clock of the taskbar they opened from keeps its
  hover plate, as Explorer's (`Clock.ShowOpen`, from `ClockFlyout.OpenChanged`).
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
