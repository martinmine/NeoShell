# Widgets

The widget sidebar, floating widgets and each widget. Part of the [NeoShell design](../design.md).

## Widgets (`Widgets/`)

Small widgets about the computer and its user, as Windows Vista's sidebar gadgets, with the taskbar's look.

### Sidebar and floating widgets

- `SidebarWindow`: a strip along the right of the primary monitor, from the top to the taskbar (the work area's
  height), 320 epx wide by default (`WidgetSidebarWidth`, 240 to 560); dragging its left edge (`EdgeGrip`) resizes
  it, the edge keeping where it was grabbed (a 6 epx grip). The sidebar is part of the desktop: it reserves no space,
  so maximized windows cover it as they cover the desktop, as Vista's sidebar did when not kept on top. It sits at the
  right of the work area (`SidebarLayout.Bounds`), so it ends above the taskbar and keeps left of other apps' app bars;
  as the shell that work area is what `ShellWorkArea` reserved (see the taskbar's [Window](taskbar.md#window)), not
  Windows' own, which follows a moment later. While the edge is dragged only the window moves; the width is saved when
  it's let go.
- No header text: only an add button at the top right, invisible until the pointer is over it (or its menu is
  open), whose menu lists every kind; Pictures, Notes, Clock and Currency show as often as wanted (several clocks for
  several time zones), the others once only (disabled in the menu while shown). Widgets are cards
  in a scrolling column.
- Hidden or shown from the Settings window (Taskbar > Widgets, or the Widgets page; `ShowWidgetSidebar`); floating
  widgets stay.
- Right-clicking the sidebar or a widget in it opens its menu: "Add widget" and "Widget settings" (the Settings
  window's Widgets page, which has the panel background, `ShowWidgetPanel`, and the width). Without the panel the header goes and the window is cut to its cards (`WindowRegion.SetRoundedRects`,
  kept up with layout and scrolling): each widget keeps the backdrop behind it, as floating ones do, and the rest of
  the strip shows the desktop and lets clicks through.
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
  each floating widget; in the 2088 concept they are its own, and so are the cards (see
  [Theme: 2088](theme-2088.md#widgets-the-side-rail)).
- Closing a widget window (`Shut`): its subclasses are removed and moves and resizes no longer reach WinUI
  (`WindowClosing.IgnoreMoves`). While closing, WinUI destroys the window's content and then re-applies the window's
  styles; when that moves the client area, WinUI's move handler repositions the destroyed content (an access violation
  in `CWindowChrome::UpdateBridgeWindowSizePosition`, found from a crash dump: NeoShell crashed on exit now and then,
  as the shell, after a widget had been added).

### Each widget (`WidgetFrame`, `WidgetView`)

- `WidgetFrame` draws the card (none when floating: the window is the card) and, while the pointer is over it, a
  settings button (a flyout with the widget's own settings, headed by the widget's name) and a close button, on a
  solid plate at the top right.
- No widget shows a heading: each says what it is by itself. Its name (`WidgetView.Title`) is in the add menu, its
  settings flyout and its UI Automation name. Where the widget's own content reaches the top right
  (`WidgetView.ContentUnderButtons`: Resource usage's first value, Wireless devices' first level, a note's text, the
  calendar's arrows, the converter's first currency, the first stock's price), the
  buttons show only while the pointer is near them (16 epx around the plate), so they don't hide the content while
  it's read, expanded or typed into.
- Pressing anywhere the widget's own controls don't take and moving 4 epx drags it. While a widget is over the
  sidebar it shows itself where it would go if let go (before the first card whose middle is below the pointer,
  measured as if it weren't there), and the other cards slide to make room (`RepositionThemeTransition`); it moves
  from place to place as the pointer does, sliding too. Off the sidebar it follows the pointer in its own window, held
  where it was grabbed, and stays where it's let go. Let go over the sidebar it simply stays where it shows: nothing
  moves on release. (Until 2026-10 the sidebar opened an empty card-shaped gap, a `Border` with the subtle fill and
  card stroke, while the dragged widget stayed in its window or, dragged within the sidebar, was invisible; the
  owner saw that as an empty container drawn before letting go.)
- How (`SidebarWindow.ShowDropSlot`, the drop slot): a docked widget's own card is moved among the others
  (`UIElementCollection.Move`): an element moved stays in the tree, so it keeps the pointer it's being dragged with and
  its pictures; taken out and put back, its pictures were drawn anew and missing for about 100 ms (6 frames at 60 fps).
  A floating widget's view moves into a new card there as the pointer enters the sidebar, and back as it leaves; its
  window stays meanwhile, cut to nothing (`FloatingWidgetWindow.Conceal`, an empty window region) rather than hidden,
  so it keeps the pointer and needs no new frame from WinUI (hiding a window holds WinUI's next frame up by about 100
  ms), and keeps its size. Measured (ffmpeg, 60 fps): the widget is missing for one frame as it enters, the frame
  between the region taking effect and the sidebar drawing the card; the window is closed once let go.
- Moving between the sidebar and the desktop moves the widget itself: its view leaves the card (`WidgetFrame.Release`)
  and goes into a frame in the other window, so it shows exactly what it showed, without loading anything again (the
  same picture and slideshow, forecast, track and art, open graphs, devices, and unsaved typing in a note). Dragged
  off the sidebar, the view goes into a window that follows the pointer as soon as the pointer leaves the sidebar
  (its card stays behind, lifted: empty, invisible and without height, holding the pointer; each card keeps its gap
  below itself rather than the panel's spacing, so a lifted one leaves none), and back into its card if the pointer
  comes back. With the panel background off the sidebar's window region follows the cards, the one being dragged in.
- A widget's exception doesn't end NeoShell (`WidgetFaults.Thrower`): one on the UI thread whose stack goes through
  a `WidgetView` subclass is logged ("Exception in the MediaWidget, contained") and ends only what the widget was
  doing; the widget stays and its next refresh or click runs as usual. Found from a crash dump (2026-10-17): Now
  playing's `GetPlaybackInfo()` returned null for the current session during a burst of changes, and the
  NullReferenceException, rethrown from an async void method into a `DispatcherQueue` callback, fail-fasted the
  shell (`0xc000027b`). Code a widget runs on the thread pool is outside this, unless the widget awaits it.
- A floating note can be resized by its bottom-right corner (`WidgetView.CanResize`): its width (200 to 640 epx) and
  the height of its text (60 to 900 epx), saved as `FloatingWidth` and `ContentHeight`; the text keeps its height when
  the note is docked.
- `ShellSettings.Widgets` keeps every widget, docked and floating; the docked ones in the sidebar's order. Each has an
  id, its kind, a position while floating and its kind's options (`WidgetSettings`; unset options take defaults and
  aren't written). A widget's view is made once and kept while it's shown, also when the sidebar's window is made
  anew for another monitor; what must outlast a restart lives in its settings or a file. The default widgets have
  fixed ids.

### The widgets

- **Profile**: the account picture (`AccountPicture\Users\<SID>`, as Start), the user's display name, the time
  (optionally with seconds) and the date.
- **Resource usage**: CPU (Processor Utility, as Task Manager), GPU (the busiest engine, summed over processes, as
  Task Manager; left out without GPU counters), memory used of total, disk activity (100 - idle time) with the free
  space of the fixed drives, network down and up (bits a second) over the adapters that are up and carry IP (each
  adapter's filter drivers are listed as adapters too, with the same counts). Its settings show each drive and each
  adapter as a row of its own. Sampled once a second by `ResourceMonitor` (PDH through `SystemUsage`, wildcard
  counters for GPU engines and logical disks, `GlobalMemoryStatusEx`, `DriveInfo`, `NetworkInterface` statistics;
  read on the thread pool; the adapters are listed again only when addresses change) only while the widget is shown and the display is on; it keeps the last minute of each, so moving the widget keeps its graphs. Each row
  expands to its graph (networks scaled to the minute's peak). A bar (drawn as a ProgressBar looks: a ProgressBar animates each change of value, which kept the compositor busy) shows use and goes while the graph is open; a
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
- **Clock**: an analogue clock, as Vista's gadget: a 150 epx face with hour and minute marks, hour and minute hands
  in the text colour and a second hand in the accent colour (on by default, as Vista's; off in its settings, and the
  clock then ticks once a minute). Its settings give it a name, shown under the face, and a time zone (Windows' own
  list; the computer's by default); a clock in another zone says how it differs from here under the face ("Tomorrow,
  9 hours ahead", as Windows' Clock app). Vista's eight faces aren't brought over: the face is drawn in the theme's
  colours instead. As many as wanted.
- **Calendar**: the month, as Vista's gadget, in six weeks so its height doesn't change: the weekdays' names in two
  letters from Windows' first day of the week, the days around the month dimmed, today on an accent circle (as
  Windows' calendar flyout). Clicking a day shows it as a page of a desk calendar (the day's number large, its
  weekday, "Today" for today), and clicking the page goes back to its month; the arrows go a month, or on the page a
  day, back or on, and the title (month and year) back to today's month. Just after midnight a calendar left on
  today's month or day follows to the new day. One at a time.
- **Picture puzzle**: Vista's sliding puzzle: a picture cut into fifteen tiles in four rows of four with a gap,
  square and as wide as the card; clicking a tile beside the gap slides it in (animated,
  `RepositionThemeTransition`). The board is mixed by 200 random slides, so it can always be solved, and again with
  the shuffle button. The time runs from the first slide until it's solved ("Solved in 1:23"), when the picture shows
  whole; holding the peek button shows it meanwhile. The picture is Windows' wallpaper
  (`Web\Wallpaper\Windows\img0.jpg`) or one chosen in its settings, cut to a square from its middle and decoded at
  about the size shown; without one the tiles are numbered. One at a time.
- **Currency**: Vista's converter: two amounts and their currencies, either amount typed into converts into the other,
  and a swap button; below them the rate ("1 USD = 9.4410 NOK") and its date. The European Central Bank's reference
  rates (about 30 currencies, set once a working day) through Frankfurter (`api.frankfurter.dev`, free and without a
  key; MSN's service, which Vista's used, is gone), fetched once an hour at most for every converter and checked every
  six hours; a failed fetch is tried again five minutes later, keeping rates already shown. A new one converts US
  dollars into the computer's own currency (euros where that is the dollar or one the bank doesn't set); the two
  currencies and the first amount are kept in its settings. The currencies' names (Windows' English names) show as
  tooltips. As many as wanted.
- **Stocks**: Vista's stocks gadget: a row per symbol (its symbol and name, the price and the change since the last
  close, up in Windows' green and down in its red), each expanding to the day so far against the last close (a
  dashed line), as Resource usage's rows expand. Quotes from Yahoo Finance's chart service
  (`query1.finance.yahoo.com/v8/finance/chart`, a request per symbol, without a key, delayed as exchanges require;
  unofficial, so it may change), refreshed every five minutes while shown; a symbol Yahoo doesn't know shows "Not
  found", and one that fails keeps what it showed. The symbols are typed into its settings, separated by commas (up
  to 20; the Dow Jones and NASDAQ indexes and Microsoft by default). One at a time.
- **Notes**: plain text, straight on the card (no box or underline, focused or not), saved half a second after typing stops, to `notes\<id>.txt` next to the settings; text size
  in its settings. Closing a note keeps its file.
