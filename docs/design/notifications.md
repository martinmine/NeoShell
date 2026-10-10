# Notifications and calendar

The notification center and calendar, Do not disturb, focus sessions and toasts. Part of the [NeoShell
design](../design.md).

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
  notifications"), but its `NotificationChanged` event needs package identity, so they're read whenever the platform republishes `WNF_SHEL_NOTIFICATIONS` (on every notification that comes or goes, seen or not) or the quiet hours profile (`NotificationChanges`), or once a second if those can't be followed; an
  unchanged set of IDs changes nothing. The notification platform keeps notifications whether or not a shell runs.
  The listener gives each one's app (AppUserModelID and name), time and texts, not the toast's XML (its sound,
  arguments, images, buttons): a click is carried out by the notification platform itself (see Toast activation),
  and the XML (sound, images, buttons, inputs) comes from the app's toast history (see Toast sound and Toast
  content). App icons as the taskbar's (packaged logo, else the `shell:AppsFolder` item's icon).
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
  minutes at a time to 30, then 15, between 5 and 240; back to 30 each time the flyout opens, as Explorer's) and
  Focus.
- **Focus** (`FocusSession`, Interop `FocusSessions`; Windows 11 25H2, studied live with Explorer's calendar, Settings
  and the Clock app, ffmpeg at 60 fps, a WASAPI loopback recording and Process Monitor, and read with cdb):
  - **Windows' sessions.** The public `Windows.UI.Shell.FocusSessionManager` (Windows.UI.Accessibility.dll) reads
    freely (`IsFocusActive`, `IsFocusActiveChanged`) but starts and ends sessions only for apps that unlock the
    limited access feature `com.microsoft.windows.focussessionmanager.1` (Microsoft's: ShellExperienceHost's
    calendar, Settings, the Clock app). It hands everything to the undocumented `Windows.Internal.Shell.
    FocusSessionThemeManager` (same DLL, in-process, base trust; `IFocusSessionThemeManager` `ae042191-…`: current,
    off and default theme IDs, `InitializeTimer`, `AddSession(themeId, end)`, `RemoveSession`, `RemoveAllSessions`)
    of Explorer's immersive shell: twinui.pcshell's `FocusSessionComponent` holds the one that runs and calls
    `InitializeTimer`; other processes reach it as a service of `CLSID_ImmersiveShell` (SID `7cefd1e5-…`,
    `IFocusSessionComponent` `b4c18645-…`, `get_ThemeManager`), with registered proxies. NeoShell does the same
    alongside Explorer: Focus is `AddSession(DefaultThemeId, now + length)` (what `TryStartFocusSession` does), End
    session `RemoveAllSessions` (`DeactivateFocus`), a session runs while the current theme isn't the off theme
    (`IsFocusActive`). Sessions are a list (ID, theme, end as a FILETIME) in the cloud store
    (`…\CloudStore\Store\DefaultAccount\Current\default$windows.data.shell.focusactivesessions\…`, Bond), so
    NeoShell follows that key: a session started in Settings or the Clock app shows in NeoShell's calendar, and
    NeoShell's shows in Explorer's, Settings and the Clock app. The manager keeps cloud store objects of the
    apartment it was made in and uses them from its thread-pool timer: made on an STA it fails fast
    (`RPC_E_WRONG_THREAD` in Windows.CloudStore), so NeoShell makes and calls it on the thread pool.
  - **What a session does** is the default theme (`Windows.Data.Shell.FocusSessionActiveTheme`, read through
    `Windows.Internal.Shell.FocusSessionActiveTheme`), Settings → System → Focus, all on by default: "Show the timer
    in the Clock app", "Hide badges on taskbar apps", "Hide flashing on taskbar apps", "Turn on do not disturb". The
    manager applies it (`ApplyActiveTheme`): `TaskbarBadges` and `TaskbarFlashing` (Explorer\Advanced) = 0, which
    Explorer's taskbar (and NeoShell's) simply follows; the notification platform's focus quiet moment
    (`QuietHoursSettingsInterop.FocusQuietMomentApplicable`, which leaves the user's Do not disturb choice alone);
    `ms-clock://createfocustimer?…&endTime=…` with `ShellExecuteEx`, which opens the Clock app's always-on-top focus
    timer. At the end (`ApplyOffTheme`) it writes back whether badges and flashing were shown before (1 or 0, kept
    in the off theme), ends the quiet moment and stops the Clock app's timer. Explorer forgets flashing buttons when
    flashing is hidden: they don't come back afterwards (recorded: the next flash shows, nothing before).
  - **During a session** Explorer's calendar footer shows "Focusing" and ■ End session (no countdown; the flyout
    closes when Focus is clicked, not on End session; the length goes back to 30 minutes whenever it opens); the
    bell shows Do not disturb, as the active quiet hours profile (`WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED`, 0 off,
    1 priority only) is no longer 0, and toasts are held back. The clock itself shows nothing more. NeoShell's
    `DoNotDisturb.Read` counts that WNF state too. Footer measured against Explorer's at the same place: "Focusing"
    17 px in; the button 72 px wide at least, padding 8, 4 px between the glyph (U+F5B0 / U+EE95 at 12) and its text.
  - **The end chime** is the Clock app's: when its timer runs out it posts a toast ("Great job!" — the text comes as
    `ms-resource:` strings, which NeoShell now looks up in the app's package) with
    `ms-winsoundevent:Notification.Looping.Alarm4` (Media\Alarm04.wav, 2.1 s), played by the toast host. With "Show
    the timer in the Clock app" off, or without the Clock app, a session ends silently; ending one early is silent
    too. Explorer itself plays nothing.
  - **As the shell** the manager can't be used: its `ShellExecuteEx` of `ms-clock:` fails without Explorer
    (0x80040900) and shows an error box, and the Clock app (a UWP app) can't run anyway (T36). So NeoShell runs its
    own session there: it reads the focus settings, writes `TaskbarBadges` / `TaskbarFlashing` as the manager does
    (`FocusChanges`, unit tested), turns on Do not disturb if it was off, and puts everything back when its timer
    runs out, on End session or when NeoShell exits. No Clock timer, so no chime; Settings and the Clock app can't
    start sessions without Explorer either.
- **Toasts** (`ToastPopups`, shell mode only): without Explorer no toasts show at all — they belong to the
  ShellExperienceHost that Explorer runs — though the notifications are still stored. Each notification that
  arrives while NeoShell runs (not those already there at start, not while Do not disturb is on or the flyout is
  open) shows as Explorer's: its own acrylic window, 364 epx wide, 16 epx from the screen's right edge and 12 above
  the taskbar, with the app's icon and name, "…" and close, the title and up to three lines of body. It slides in
  from the edge; the newest is lowest and older ones move up, three at most. It leaves after the system's "Dismiss
  notifications after" time (`SPI_GETMESSAGEDURATION`, 5 s by default), later while the pointer is on it. Closing
  it only puts it away (it stays in the notification center, as in Explorer); clicking it activates the app (see
  Toast activation), and it plays the toast's sound (see Toast sound). Alongside Explorer, Explorer shows toasts.
  Tray icons' balloons show the same way (see [System tray](tray.md#system-tray-tray)), with a picture beside the text, and are never stored.
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
  alongside Explorer that the same calls behave as its clicks. Buttons, menu items and replies go the same way (see
  Toast content).
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
  told from the listener or the history, so it pops up with its sound.
- **Toast content** (T43; Interop `ToastContent`, `ToastLayout`, `ToastContentView`, `NotificationCard`). Studied on
  25H2 with a test app (unpackaged, `AppUserModelId\<AUMID>` with `CustomActivator`, a COM activator logging what
  `INotificationActivationCallback::Activate` gets), UI Automation bounds of Explorer's toast views
  (`NormalToastView`, `PriorityToastView`), screenshots, and cdb on WpnUserService breaking in
  `MainControllerImpl::ActivateNotification`.
  - **The XML** is the history entry matched by its texts (the attribution, which the listener leaves out, is left
    out of the match too), with the entry's `NotificationData` filling `{bindings}` of a progress bar. Images as the
    platform finds them: `ms-appx:///` (the largest `.scale-*` variant when the plain file isn't there) and
    `ms-appdata:///local/` in the package, `file:///` and full paths; web images only for a packaged app with the
    `internetClient(Server)` capability. An app without a Start entry gets its logo from
    `AppUserModelId\<AUMID>\IconUri`, as Explorer's header shows it.
  - **Layout** (epx; toast 364 wide, content 332 at 16 in): title 50 from the top (the old 54 was 4 low), 21 under the
    last text; `appLogoOverride` 48 square or 60 cropped to a circle, 16 from the texts, its top 4 above the title's,
    the toast ending 16 under it; hero 364x180 across the top, the header under it as on a toast without one;
    attribution (caption, secondary) 3 under the body; `header` title (caption) over the texts, the title 38 under it;
    inline pictures 332 wide by their shape (at most 204 high, centred), a circle-cropped one a 96 circle (an incoming
    call's picture); then the progress bar (title, 8, a 4 epx accent bar on a dim track, 8, status with the value or
    percentage at the right, 21 to the bottom), text box (32 high, the send button 49 wide 8 beside it, showing its
    icon), selection boxes (32, full width) and buttons, 16 apart and 16 from the bottom. Buttons share a row in equal
    widths 8 apart (five fit); with an icon and no `useButtonStyle` they are 61 high with the 16 epx icon over 12 pt
    text; `useButtonStyle="true"` puts the icon beside the text and colours `Success` / `Critical` buttons
    (`SystemFillColorSuccess` #6CCB5F, `SystemFillColorCritical` #FF99A4 dark). An incoming call without
    `useButtonStyle` has its last button alone in a full-width accent row under the others. `urgent` puts a red "!"
    before the logo (the logo 11 to the right). System `snooze` / `dismiss` without content read "Snooze" / "Dismiss".
    Context menu items come first in the "…" menu and the right-click menu, above a separator.
  - **Timing**: a reminder, alarm or incoming call with a button stays until acted on (Explorer's "priority"
    toasts), `duration="long"` stays 25 s, others the system time; any toast stays while its text box has the
    keyboard (clicking it lets the no-activate toast take the foreground) or its menu is open.
  - **Notification center**: a card shows logo, header and attribution; Explorer's chevron by the time expands it to
    the pictures (the hero under the texts, edge to edge), progress, inputs and buttons. A card with a hero starts
    expanded (Explorer's `IsHeroImageAutoExpanded`).
  - **Activation**: `ActivateNotification(AUMID, ID, data)` with a `NOC_ITEM_ACTIVATION_DATA` (0x28 bytes: invoke ID
    `LPWSTR`, `NOTIFICATION_USER_INPUT_DATA*` pairs of key/value `LPWSTR`s and their count, system hints and count,
    caller window ID; from ShellExperienceHost's `NotificationItemActivationData` setters and the controller's
    `FindUserInputValueByKey`). The invoke ID isn't the button's arguments (those fail with E_INVALIDARG): it is
    "<" and the action's index among the toast's actions, context menu items and system buttons counted, the button
    beside a text box left out (its own ID is that text box's ID). Every input goes with it (an empty text box too,
    a selection box's chosen ID); a click on the body sends no invoke ID but the inputs. The controller does the rest:
    the app's activator gets the button's arguments and inputs, `snooze` reschedules it for the selected minutes,
    `dismiss` removes it. Checked with the test app: Explorer's and NeoShell's clicks deliver the same arguments and
    inputs (buttons, a reply, a menu item, a background button, a body click with typed text), and a 1-minute snooze
    came back after a minute.
  - **Not matched**: Explorer sometimes draws a square logo 60 (a reply toast with a selection box) where NeoShell
    draws 48; WinUI opens the toast's menus left of the toast instead of ending at the pointer; Explorer's
    notification center shows a `header` as a subgroup row, NeoShell over the card's texts; a progress toast's later
    data updates aren't read; Windows' "Important Notification Request" prompt for an urgent toast isn't shown by
    NeoShell. Not run live: packaged apps' package images and web images, `protocol` buttons as the shell (the
    activation is the controller's either way).
