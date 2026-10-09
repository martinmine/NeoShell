# Quick Settings

Quick Settings: its tiles and pages. Part of the [NeoShell design](../design.md).

## Quick Settings (`QuickSettings/`)

What Windows 11 opens from the network and volume icons (what earlier Windows called the action center).
`QuickSettingsPanel` is the content of the primary taskbar's flyout, 360 effective pixels wide with its 1-pixel
border (`SurfaceStrokeColorDefaultBrush`, ControlCenter's `ControlCenterPanelBorderBrush`), laid out as Windows':
tiles, the volume slider, and a footer with the battery (when there is one) and All settings (`ms-settings:`; shell
mode Control Panel). Its state comes from `Indicators` and is only read while it's open.

Windows' own layout and resources were read from ControlCenter's compiled XAML (T39b): the `.xbf` files are embedded
in `SystemResources\Windows.UI.ControlCenter\Windows.UI.ControlCenter.pri` (`makepri dump /dt detailed` writes
them out as base64), and XBF v2 was decoded with a small script: framework types and properties are "stable XBF"
indexes (high bit set), mapped to names through `Windows.UI.Xaml.dll`'s `Parser::c_aStableXbfPropertyToKnownProperty`
/ `c_aStableXbfTypeToKnownType` and `c_aPropertyNames` / `c_aTypeNameInfos` (found with cdb and its symbols). The
WinUI 2 brushes they use come from `SystemApps\Microsoft.UI.Xaml.CBS_8wekyb3d8bbwe\resources.pri`
(`21h1_themeresources.xbf`), the Bluetooth page's rows from `Windows.UI.ShellCommon.pri` (`DevicesFlowUI\*`).

- **Colours** (ControlCenterPage, ControlCenterView): the panel is in-app acrylic, `ShellSurfaceBackgroundBrush` =
  `AcrylicInAppFillColorBaseBrush` (#202020 at tint opacity 0.5 dark, #F3F3F3 at 0 light) or, while "Show accent
  colour on Start and taskbar" is on (its `AccentAcrylic` state), `SystemControlAcrylicAccentElementBrush` =
  `AccentAcrylicInAppFillColorBaseBrush` (the accent's Dark2 shade dark, its Light3 shade light, at 0.8). The
  theme stays Windows' own: with the light theme and the accent on, the panel is a light accent with dark text
  (the taskbar stays dark accent). The tiles and sliders lie on `ControlCenterOverlayBrush` =
  `LayerOnAcrylicFillColorDefaultBrush` (#09FFFFFF dark, #40FFFFFF light); the footer doesn't, so it's darker in
  dark and greyer in light, below a `CardStrokeColorDefaultBrush` rule (#19000000 / #0F000000); the tiles end in a
  `DividerStrokeColorDefaultBrush` rule. Pages are the same: header and content on the overlay, the footer plain.
- NeoShell's popup can't hold an in-app acrylic (WinUI 3 has no host-backdrop brush), so the flyout's
  `DesktopAcrylicController` takes the brush's tint and tint opacity, and a luminosity opacity measured from
  Windows' panel over black, grey and white test windows (how much of the desktop shows through): 0.96 dark, 0.9
  light, 0.8 dark accent, 0.9 light accent (`QuickSettingsDisplay.Backdrop`). Measured over the three backgrounds
  in all four combinations the base colours are within 1-3 levels of Windows'.
- **Footer** (FooterGrid): 48 high, padding 8,0,4,0; All settings is a 40-square button (FooterIconButtonStyle)
  6 in from the right (RightFooterTemplate's margin 2,3,6,3), its gear at 16.
- **Volume** (VolumeSliderTemplate in PaginatedSliderGroupTemplate): 12 from the left, a 40-wide mute button, the
  slider 4 in from either side, a 40-wide Sound output button (U+F4C3 at 16 and the chevron U+E974 at 12), 14 from
  the right; the track ends line up with the slider's (measured to the pixel, the mute glyph 1 lower than the track's
  centre as Windows' animated icon sits).
- **Pager** (PaginatedGridView's PipsPager, `ControlCenterPipsPagerStyle`): 2 from the right, vertically centred in
  the tiles' area; 20-high arrows (U+EDDB/U+EDDC at 8) around 12-high pips, 6 across while chosen and 4 otherwise,
  all `ControlStrongFillColorDefaultBrush`; an arrow with nowhere to go is hidden but keeps its place, so the pips
  don't move. Measured to the pixel against Windows'.

### Tiles

- Two rows of three per page (`QuickSettingsDisplay`, unit tested); more pages are turned with the mouse wheel or the
  arrows beside the page dots, sliding up or down. A tile is a toggle button, accent-filled while its feature is on,
  with its name below: a switch, a page (glyph and chevron), or both split in two halves (left switches, right opens
  the page). Tiles without hardware or support aren't shown, as in Windows. A split tile's two 48-wide halves touch:
  their 1-pixel borders make Windows' 2-pixel divider, and the right half's chevron is 12 pixels (10 inline, 7 from
  the glyph). The three 96-wide tiles sit 23 in from either side of the panel's border, 12 apart.
- Windows' order (its default layout, `HKCU\Control Panel\Quick Actions\Control Center\UserLayoutPaginated`: Wi-Fi,
  Bluetooth, Cellular, Studio effects, Airplane mode, Accessibility, VPN, Rotation lock, Battery/Energy saver, Live
  captions, Night light, Mobile devices, Mobile hotspot, Nearby sharing, Colour profile, Cast, Project; sliders
  Brightness, Volume) with the tiles NeoShell has: Wi-Fi, Bluetooth, Airplane mode, Accessibility, VPN, Rotation lock,
  Energy saver, Live captions, Night light, Mobile hotspot, Nearby sharing, Cast, Project.
- **Wi-Fi** and **Bluetooth** (shown when the PC has the radio): the switch turns the radio on or off
  (`RadioSwitches`, WinRT `Windows.Devices.Radios`; `Radio.RequestAccessAsync` once). The Wi-Fi tile shows the
  connected network's name. Radios are looked for again each time Quick Settings opens (adapters come and go).
- **Airplane mode** (shown when the radio management service answers and Windows' settings environment offers it:
  not without radios, below): every radio off at once through the Radio
  Management API (`AirplaneMode`): `IRadioManager` (CLSID `581333F6-…`, RmSvc), undocumented but unchanged since
  Windows 8 and what airplane-mode tools use; there's no public API. No change notification: it's read again
  whenever a radio changes or Quick Settings opens.
- **Energy saver** (Windows 11 24H2 and later): state from the documented `GUID_ENERGY_SAVER_STATUS` power setting
  notification (`PowerSettingRegisterNotification`; the first call comes straight away). Setting it has no public
  API: Windows' own setting (`SettingsHandlers_OneCore_BatterySaver.dll`) publishes the WNF state
  `0x41C6013DA3BC3075` with 1 (on) or 2 (off), which is what `EnergySaver.Set` does (`RtlPublishWnfStateData`).
- **Live captions**: on while `LiveCaptions.exe` runs; see Accessibility below.
- **Windows' quick actions** (`QuickActions`, Interop `SystemSetting`): night light, nearby sharing, mobile hotspot,
  VPN, rotation lock and brightness (and the Accessibility page's colour filters and mono audio, below) keep their
  state in Windows' private stores (night light in the cloud data store,
  whose old CloudStore registry blob 25H2 no longer reads; nearby sharing in the Connected Devices Platform service).
  Windows' own Quick Settings (ShellHost's `ControlCenter.dll` over `QuickActionsDataModel.dll`) doesn't reach into
  them either: each quick action is a setting of the Settings handlers (`SystemSettings.DataModel`), listed under
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\ActionCenter\Quick Actions\All` (FriendlyName, title in
  QuickActionsDataModel's string table, glyph) and `HKLM\SOFTWARE\Microsoft\SystemSettings\SettingId\<id>`
  (`DllPath`). The handler DLL exports `GetSetting(HSTRING id, ISettingItem**)`; `ISettingItem`
  (`{40C037CC-D8BF-489E-8697-D66BAA3221BF}`, from the DLLs' symbols): Id, Type, IsSetByGroupPolicy, IsEnabled,
  IsApplicable, Description, IsUpdating, GetValue/SetValue(name, IInspectable), GetProperty/SetProperty, Invoke, and a
  `SettingChanged` event (`TypedEventHandler<Object, String>`, the name of what changed). NeoShell opens the same
  settings in the background at start, so they work as the shell too, and shows a tile while its setting is
  applicable, greyed while it isn't enabled (QuickActionsDataModel's `QuickSetting` reads the same two; ControlCenter
  pins inapplicable ones to the end, out of sight). Some settings learn their applicability a moment after they're
  opened (night light reads 0 at first, then reports 1 with SettingChanged), so the state is read again on every
  SettingChanged, on a thread-pool thread, and whenever Quick Settings opens.
  - **Settings' environment** (Interop `SettingsEnvironment`): the handlers count the VPN, the hotspot and airplane mode
    as applicable on any PC, yet Windows' Quick Settings hides them without a VPN set up, without Wi-Fi and without
    radios. Its rule (QuickActionsDataModel's `QuickSetting::get_IsApplicable`): a quick action shows only while its
    setting ID and the Settings page and group it's registered with (`Page`, `Group` under `…\Quick Actions\All\<id>`:
    `SettingsPageNetworkVpnQA`, `SettingsGroupInternetSharing`…) are all applicable in Settings' environment, asked
    through the environment database (`SystemSettings.DataModel.SettingsEnvironmentDatabase`, vtable slot 6
    `IsSettingApplicable`). That class refuses activation outside its allowed callers (E_ACCESSDENIED,
    `AppAccessCheck::IsCallerAllowedToActivate`), so NeoShell asks what it wraps: `SettingsEnvironment.Desktop.dll`'s
    `GetDesktopSettingsEnvironment` (which shell32's `GetSettingsEnvironmentInstance`, ordinal 916, also hands out),
    an `ISettingsEnvironment` (`{0ADB9837-6628-48D2-AD8A-3C138CDC9B62}`, `SettingsEnvironmentImpl`) whose
    `IsApplicable(PCWSTR, bool*)` sits after a virtual destructor. An ID it doesn't know (TYPE_E_ELEMENTNOTFOUND) counts
    as applicable, as in Quick Settings. Some answers are worked out in the background: the VPN's is "no" when first
    asked and "yes" a few seconds later; there's a change handler but NeoShell asks again on each opening instead. On
    this VM (no Wi-Fi, Bluetooth or radios, no battery) Windows' 25H2 Quick Settings shows exactly Accessibility,
    Energy saver, Live captions, Night light, Nearby sharing, Wired display (Cast) and Project, plus VPN after
    Accessibility once a VPN is set up; NeoShell now shows the same tiles in the same order.
  - **Night light**: `SystemSettings_Display_BlueLight_ManualToggleQuickAction` (SettingsHandlers_Display), a bool
    Value; applicable when the night light state in the cloud store says the display supports it (this VM's does).
  - **Nearby sharing**: `SystemSettings_SharedExperiences_NearShareQuickAction` (SettingsHandlers_SharedExperiences_
    Rome), a bool Value: the tile turns it on or off; Settings' three states are `…_NearShareEnabled` (off) and
    `…_NearShareAuthzLevel` (`…_AuthzLevel_MyDevices` / `…_EveryoneNearby`, a string; the tile is on for both and
    keeps the level; checked live in both; the level's handler fails fast when it's read while sharing is off). A
    split tile only while the handler's `QuickActionIsL2TemplateVisible` value is true (a property ControlCenter's
    templates read; its `NearShareQuickActionButtonTemplate` has the page); it's false on this VM, where Windows shows
    a plain switch, and so does NeoShell. Its page has the switch, "Nearby sharing is on/off" with Windows' text about
    Bluetooth and WLAN (ControlCenter's `NearShareL2Page*` strings), and More Nearby sharing settings
    (`ms-settings:crossdevice`); not compared, as Windows never offered it here.
  - **Mobile hotspot**: `SystemSettings_Network_Tethering_QuickAction` (NetworkMobileSettings, a C++/CX class that
    keeps its state in properties): `QuickActionIsActive`, `QuickActionStatus` (the label: "Mobile hotspot", or how
    many devices are connected), `SetProperty("Value", bool)` shares the connection with Settings' saved name and
    password (`TetheringToggleSharingWithCurrentSettings`). Enabled while there's a connection to share and policy
    allows it, and shown only where Settings' environment offers it (hidden here, without Wi-Fi, in Windows' too). A
    switch only: 25H2's Quick Settings has no hotspot page (ControlCenter has pages for Wi-Fi, Bluetooth,
    cellular, VPN, mobile devices, nearby sharing, volume, cast and project, and no hotspot strings).
  - **VPN**: `SystemSettings_Network_VPN_QuickAction`: `QuickActionIsActive`, `QuickActionStatus` (the label while it
    says something: "Can't connect", "Connecting"), `QuickActionIsToggleTemplateVisible` (whether there's a VPN for the
    tile's left half to switch; otherwise it's a page tile); `SetProperty("Value", …)` connects the VPN Windows last
    used through the network UX connection flow, or hangs it up if it's connected (`VPNQuickAction::SetProperty`). The
    right half opens the VPN page ("Manage VPN connections"). Shown only while a VPN is set up (Settings' environment),
    after Accessibility. Checked with a throwaway IKEv2 profile (no server): both tiles appeared split, switching
    showed "Can't connect" and then "VPN" again, and both went away when the profile was removed.
  - **Rotation lock**: `SystemSettings_Display_IsRotationLockedQuickAction` (SettingsHandlers_PCDisplay) switches it
    (SetValue bool, which calls `user32` ordinal 2507, undocumented). Its handler counts every PC as having it; the tile
    shows only where `GetAutoRotationState` reports a sensor (no AR_NOSENSOR / AR_NOT_SUPPORTED), greyed while docked,
    in laptop mode, with several screens or in a remote session, on while locked (`AutoRotation`, read as the handler's
    `GetRotationLockState` reads it; unit tested).
  - **Brightness**: the slider above the volume's (Microsoft.QuickAction.Brightness = `SystemSettings_Display_Brightness`,
    SettingsHandlers_PCDisplay: an Int32 0-100), shown while applicable (`DisplaySettingsManager::IsBrightnessSupported`:
    an internal panel or a monitor Windows controls), with the sun glyph U+E706; the two slider tracks line up. While
    it's dragged only the latest value is sent. Two traps: `SystemSettings_System_Display_Internal_Brightness`
    (SettingsHandlers_Display) fails fast a few seconds after it's opened outside Settings (`CShellHintManager::
    OnSingletonInit`), and the PCDisplay handler still held when a process exits leaves its last thread waiting on an
    LPC reply for good, so `SystemSetting.Dispose` releases each handler's object before NeoShell exits.
  - **Cast** reads `SystemSettings_DeviceDiscovery_Connect_QuickAction` too: Windows labels the tile with its
    `QuickActionStatus` and fills it while `QuickActionIsActive`, so on this VM it's "Wired display", on (a monitor on
    a cable counts); "Cast", off, otherwise. Its glyph is the registry's U+F117 (the screen with the waves at the
    bottom left).
  - Glyphs: VPN U+E705, Rotation lock U+E755, Mobile hotspot U+E88A from the registry. ControlCenter draws every
    tile's icon as an animated icon (ControlCenterResources' `AccessibilityIcon` = `QA_Accessibility`, `CastIcon` =
    `QA_Cast`, `NightlightIcon` = `QA_Nightlight`, … `EnergySaverAcOnlyIcon` = `QS_24_EnergySaver`: Lottie
    AnimatedVisualSources compiled into ControlCenter.dll, no font), whose resting frames differ from the registry's
    glyphs in places: night light is the sun-and-moon U+F08C drawn 14 pixels across and, while on, a filled moon
    (U+F1DB at 10) with faint dots of the sun's rays (seven 2-pixel dots at 40%, placed as measured); nearby sharing is
    the share glyph U+E72D, not U+F3E2; Accessibility, Nearby sharing and Cast are 14 pixels, not 16 (measured
    against U+E776/E72D/F117), Energy saver and Live captions 16. Recorded at 60 fps (T39b): on a toggle Windows'
    icons mostly just change colour; night light's sun turns to the moon two frames after the press and the moon
    stays about 100 ms after it's turned off; nearby sharing's arrow draws back into its box and out again (about
    230 ms) as it turns on — NeoShell swaps glyphs and doesn't animate that arrow.
  - Compared side by side with Windows' own (ShellHost's Control Center, opened with Win+A: on this VM clicking
    Explorer's network button stopped opening it after Explorer was restarted, and restarting Explorer and ShellHost
    didn't help), with NeoShell alongside Explorer, at 100%, in dark and light with "Show accent color on Start and
    taskbar" on and off: zoomed grabs of the tiles, glyphs and pages, and the page transitions at 60 fps.
  - No **Keyboard layout** tile: 25H2's Quick Settings has none (QuickActionsDataModel keeps the string "Keyboard
    layout" from older builds, but no quick action is registered for it and ControlCenter has no template for it); the
    input indicator's switcher is where the input methods are.

### Pages

Each has a header with a back button and, as Windows', its shortcut as key caps (`ShortcutKeys`), and a 48-high
footer with a link to Settings. As Windows' (recorded at 60 fps, VPN and Sound output pages): opening one, the
flyout takes its height, the header shows almost at once (fading in over 80 ms) and the content below it rises 32
pixels into place over 300 ms (cubic-bezier 0,0,0,1) while fading in over 150 ms; going back, the tiles fade in over
100 ms where they are, without sliding. Windows' VPN and Cast pages are 400 high; NeoShell's VPN and Nearby sharing
pages are at least that. Every page's link to Settings is Windows' `L2FooterLinkTemplate`: 12-pixel text in
`LinkForegroundBrush` (SystemBaseMediumColor), on the footer's plain colour below a `CardStrokeColorDefaultBrush` rule.

Between the tiles and a page (T39b, Accessibility page at 60 fps): Windows' window takes the new size in one frame,
for which its old content shows shifted (its frame lags the window), and crossfades old and new over 2-5 frames.
NeoShell's popup did the same shifted frame, then jumped left of the widget sidebar: WinUI places a windowed popup
inside the monitor's work area afresh whenever its size changes (and the sidebar's app bar is out of the work area),
dropping the popup offset that had put it over the sidebar. `PopupWindows.KeepRight` now holds the window's right
edge against WinUI's moves while the flyout is open, and `TaskbarFlyouts` tells WinUI by the popup's offset again
after each size change, so UI Automation and tooltips agree. The page's slow state (assistive apps' processes,
Settings handlers) is read only after its first frame, which had left the flyout empty for 2-3 frames at its new size.
Still differs: NeoShell cuts between old and new content rather than crossfading.

- **Wi-Fi**: a switch for the radio, a refresh button, and the networks in range (`WifiNetworks`, WinRT
  `WiFiAdapter`): one entry per name at its strongest, the connected one first, then by signal (unit tested), each
  with its signal and a lock when secured, and "Connected, secured" / "Secured" / "Open". Choosing one opens it up:
  Connect automatically and Connect, or Disconnect for the connected one. Connecting uses the saved profile; when
  Windows has no key (or a wrong one) a password box and Next appear. Scanned when the page opens and on refresh.
  Windows only gives network names to apps allowed to use the location. More Wi-Fi settings
  (`ms-settings:network-wifi`; shell mode `ncpa.cpl`).
- **Bluetooth**: a switch for the radio and the paired devices, classic and LE (`BluetoothDevices`,
  `DeviceInformation` with `GetDeviceSelectorFromPairingState(true)`), one row per physical device (a dual-mode
  device's classic and LE pairings share a container id; the classic one is kept), connected ones first. Each row: a
  glyph by kind (class of device or LE appearance), the name, and the status in Windows' words (DevicesFlow's strings,
  `Windows.UI.ShellCommon` en-GB): "Paired", "Connected", or for an audio device by its profiles "Connected audio"
  (stereo), "Connected mic" (hands-free) or "Connected mic, audio"; while connected, the battery Windows knows
  (`DEVPKEY_Bluetooth_Battery` on the device's nodes, as the Wireless devices widget reads it) as a battery glyph
  and "80%". More Bluetooth settings (`ms-settings:bluetooth`; shell mode Devices and Printers).
  - **Connecting and disconnecting**, as Windows' own Quick Settings does it. ControlCenter's Bluetooth page
    (`BluetoothListTemplate`) hosts DevicesFlowUI's `ConnectableDevicesControl` over `DeviceFlows.DataModel.dll`,
    whose device collection makes a connectable model (`BluetoothDeviceModel`, source file AudioDevice.cpp) only for
    Bluetooth audio devices (device class 3); every other device is a plain `DeviceBase`, listed with its state and
    battery and no action (read in Ghidra: `DeviceCollectionManager::CreateDeviceBaseModel`). Choosing a paired audio
    device connects it straight away ("Connecting..."); choosing a connected one opens its row with Disconnect
    (`_UpdateDeviceInteractionUnderLock`: a `BluetoothDisconnectModel` while connected). The model gives the device 15
    seconds (`_StartOperationTimerUnderLock`, a 150,000,000 x 100 ns thread-pool timer) and otherwise reports an error
    ("Couldn't connect." / "Couldn't disconnect."; "Disconnecting" while under way).
  - The work is done by DevicesFlowUserSvc (`DevicesFlowBroker.dll`, `BluetoothAudioProvider`, audioconnection.cpp),
    entirely with public pieces, which NeoShell (Interop `BluetoothAudio`) does in its own process: the device's audio
    endpoints are those (render and capture, active or unplugged: a disconnected Bluetooth device keeps its endpoints,
    unplugged) whose `PKEY_Device_ContainerId` is the device's container. Each endpoint is followed through its
    `IDeviceTopology` → connector 0 → `GetConnectedTo` → `IPart::GetTopologyObject` → `GetDeviceId` to the Bluetooth
    audio driver's KS filter, opened with `IMMDeviceEnumerator::GetDevice`, whose `IKsControl::KsProperty` takes
    `KSPROPSETID_BtAudio` (ksmedia.h) `KSPROPERTY_ONESHOT_RECONNECT` (0) or `_DISCONNECT` (1) as a GET with no data.
    Connecting succeeds when any endpoint's profile does. Disconnecting asks every endpoint, then drops the link with
    `BluetoothApis!BluetoothDisconnectDevice(NULL, &address)` (undocumented export: it sends the documented
    `IOCTL_BTH_DISCONNECT_DEVICE` to every radio and returns 0 when one took it, else ERROR_NOT_FOUND); Windows skips
    that for LE Audio-only devices, NeoShell for devices paired only over LE. (Windows can also send the property
    through a "controller interface" path, `CreateFile` + `IOCTL_KS_PROPERTY` on a path read from the filter's
    properties; it's the same request, so NeoShell keeps to `IKsControl`.) Profiles shown: an active endpoint with
    the Headset form factor (`PKEY_AudioEndpoint_FormFactor` 5, what hands-free makes) or any input means "mic", any
    other output "audio". Afterwards the device list is read again every half second until the device follows or 15
    seconds pass. All of it works in both run modes (no Explorer involved).
  - Checked on the VM, which has no Bluetooth adapter (none can be emulated: Windows has no software radio and the VM
    no USB passthrough): the endpoint walk and KS request reach the VM's HD Audio filter, which answers
    ERROR_SET_NOT_FOUND (0x80070492) as a non-Bluetooth filter should; `BluetoothDisconnectDevice` without a radio
    returns ERROR_NOT_FOUND; with fake devices put in the list for the test, rows, battery, the opened row's
    Disconnect, "Couldn't connect." and "Couldn't disconnect." showed in both run modes. Windows' own page can't be
    shown here (no Bluetooth tile without a radio).
  - Its rows as Windows' draws them (T39b, read from DevicesFlowUI's repainted templates in Windows.UI.ShellCommon.pri,
    as the page can't be shown): `ProjectInterfaceDeviceTile` is a grid of a 36-wide glyph column (Segoe Fluent Icons
    16, padding 0,15,12,0), the name (Body, 11 from the top, 20 kept clear at its right) over the status
    (`DeviceStatusTextStyle`: 12, secondary), and the battery at the right, vertically centred: the percentage (12,
    secondary) then the battery glyph (Segoe Fluent Icons 16, secondary); the row is at least 60 high, in a standard
    ListViewItem. The glyph is DevicesFlowUI's `BatteryStatusToGlyphConverter` (read in its code with cdb): U+ECB9 up
    to 4%, U+ECBA to 22, U+ECBB to 40, U+ECBC to 58, U+ECBD to 76, U+ECBE to 94, U+ECBF to 100, none above (unit
    tested). A chosen connected audio device opens below the row with `BluetoothDisconnectDeviceView`: a plain
    (not accent) button, at least 148 wide (`ButtonMinWidth`), right-aligned, 16 above the row's end (the repainted
    `ButtonMargin`). NeoShell's rows follow this; checked with fake devices only.
- **Accessibility**, by need as Windows': Vision (Magnifier, Narrator, Colour filters), Hearing (Live captions, Mono
  audio), Motor and Mobility (Voice access, Sticky keys). Each row: glyph, name, description, its state in words and
  a switch. Magnifier, Narrator, Live captions and Voice access are apps of their own: on while their executable runs
  in this session, started from System32, and closed with their window's `SC_CLOSE` (Magnifier ignores `WM_CLOSE`),
  or ended when they have no window (`AssistiveTools`). Sticky keys is `SPI_SETSTICKYKEYS` (saved to the profile and
  announced). Colour filters and Mono audio are switched as Windows' Accessibility page switches them: it's
  ControlCenter's `AccessibilityPageViewModel.xaml` (Windows.UI.ControlCenter.pri), whose rows are quick actions of
  their own (`Microsoft.QuickAction.ColorFilters`, `.MonoMix`, …) registered under `…\Quick Actions\All` like the
  tiles, so they go through the same Settings handlers (`QuickActions`, see "Windows' quick actions"):
  `SystemSettings_Accessibility_ColorFiltering_IsEnabled` (SettingsHandlers_nt) and
  `SystemSettings_Accessibility_IsAudioMonoMixStateEnabled` (SettingsHandlers_Accessibility), each a bool Value.
  Settings' own switches are other IDs for the same state (`…_ColorFilter_IsEnabled` in SettingsHandlers_Accessibility,
  `SystemSettings_Audio_MonoMixState` in AudioHandlers). The colour filter's handler raises SettingChanged for changes
  made in any process (Settings, Windows' Quick Settings), so the row follows them while the page is open; the mono
  handler reports only its own process's changes, so NeoShell also watches the audio service's
  `HKCU\Software\Microsoft\Multimedia\Audio` (`AccessibilityMonoMixState`, written for either switch). Both open
  and switch fine outside Settings and as the shell, where the filter applies without Explorer (checked: greyscale
  screenshots on, colour again off). Windows' rows show "On"/"Off" beside the switch as NeoShell's do; Windows' page
  closes when another app takes the foreground, so whether it follows outside changes while open couldn't be seen.
  The apps' and sticky keys' states aren't reported, so they're read when the page or the tiles open. More
  Accessibility settings.
- **Cast** (Win+K): without Wi-Fi there's no Miracast, and the page says so as Windows does ("Connect a cable to
  cast"); with Wi-Fi it offers Settings' wireless display search, since connecting to one has no public API. More
  cast settings opens Display settings (shell mode the adapter's classic properties).
- **VPN**: the VPN connections set up in Windows (`VpnConnections`): the `Type=2` entries of the user's and all users'
  remote access phonebooks (`%APPDATA%` and `%ProgramData%\Microsoft\Network\Connections\Pbk\rasphone.pbk`; the
  public `VpnManagementAgent.GetProfilesAsync` lists none of them to a desktop app), "Connected" from
  `RasEnumConnections`. Choosing one opens it up with Connect, or Disconnect for a connected one (`RasHangUp`); as
  Windows', the first one is chosen when the page opens. Rows as Windows' (measured): 350 wide (5 from the flyout's
  sides), the shield at 26 pixels 16 in from the row, the name beside it, the accent Connect button 148 wide, 12 from
  the right and 8 from the bottom of a 105-high chosen row; More VPN settings is 12-pixel secondary text there, as on
  Windows' VPN page (its other pages' links are 14). Connect
  opens Windows' own dial dialog (`rasphone -f <phonebook> -d <name>`), which asks for what isn't saved (Explorer's
  page uses the network UX connection flow and its sign-in prompt, which other processes can't host). "No VPN
  connections" without any. More VPN settings (`ms-settings:network-vpn`; shell mode `ncpa.cpl`).
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
