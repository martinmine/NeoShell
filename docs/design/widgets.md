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
  open), whose menu lists every kind; Profile, Resource usage, Now playing and
  Weather show once only (disabled in the menu while shown), Pictures and Notes as often as wanted. Widgets are cards
  in a scrolling column.
- Hidden or shown from the taskbar's menu ("Show widgets", `ShowWidgetSidebar`); floating widgets stay.
- Right-clicking the sidebar or a widget in it opens its menu: "Show panel background" (`ShowWidgetPanel`) and
  "Add widget". Without the panel the header goes and the window is cut to its cards (`WindowRegion.SetRoundedRects`,
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
  each floating widget.
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
  (`WidgetView.ContentUnderButtons`: Resource usage's first value, Wireless devices' first level, a note's text), the
  buttons show only while the pointer is near them (16 epx around the plate), so they don't hide the content while
  it's read, expanded or typed into.
- Pressing anywhere the widget's own controls don't take and moving 4 epx drags it. A widget dragged from the sidebar
  is lifted out of the column (invisible and without height, kept in the tree so it keeps the pointer; each card keeps
  its gap below itself rather than the panel's spacing, so a lifted one leaves none). While a widget is over the
  sidebar, the others
  make room for it (moves of a desktop-level window leave its z-order alone, so dragging stays smooth): a
  card-shaped gap opens where it would go (before the first card whose middle is below the
  pointer, measured as if the gap weren't there), and the cards slide (`RepositionThemeTransition`). Off the sidebar
  the widget follows the pointer in its own window, held where it was grabbed, and stays where it's let go. Let go
  over the sidebar it takes the gap's place at once.
- Moving between the sidebar and the desktop moves the widget itself: its view leaves the card (`WidgetFrame.Release`)
  and goes into a frame in the other window, so it shows exactly what it showed, without loading anything again (the
  same picture and slideshow, forecast, track and art, open graphs, devices, and unsaved typing in a note). Dragged
  off the sidebar, the view goes into the window that follows the pointer as soon as the pointer leaves the sidebar
  (the lifted card stays behind, empty, holding the pointer), and back into a new card if it's let go over the
  sidebar after all. A floating widget dropped on the sidebar jumps into the gap and its view goes into a new card
  there; the window stays over the gap with a picture of the widget in its place (`RenderTargetBitmap`, taken as it's
  dropped) until the card has been drawn, then is hidden and closed. A window doesn't show or go in step with what
  WinUI draws (hiding one holds WinUI's next frame up by about 100 ms), so going at once it left the gap empty for a
  frame or more, and staying empty it covered the card.
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
- **Notes**: plain text, straight on the card (no box or underline, focused or not), saved half a second after typing stops, to `notes\<id>.txt` next to the settings; text size
  in its settings. Closing a note keeps its file.
