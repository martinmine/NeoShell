# Widgets

The widget sidebar, floating widgets and each widget. Part of the [NeoShell design](../design.md).

## Widgets (`Widgets/`)

Small widgets about the computer and its user, as Windows Vista's sidebar gadgets, with the taskbar's look.

### Sidebar and floating widgets

- `SidebarWindow`: a strip along the right of the primary monitor, from the top to the taskbar (the work area's
  height), 320 epx wide by default (`WidgetSidebarWidth`, 240 to 560); dragging its left edge (`EdgeGrip`) resizes
  it, the edge keeping where it was grabbed. The grip is 6 epx plus a maximized window's invisible resize border wide
  (`SidebarLayout.GripWidth`, 14 epx at 100%, tested): a maximized window's bounds reach that border (8 px at 96 DPI)
  past the work area, over the sidebar's edge, and it takes the pointer there, so a 6 epx grip couldn't be reached
  beside one (T39d; Explorer has no sidebar). It reserves its space so maximized windows stop at its edge: alongside Explorer as an app bar on the right
  (`AppBar.DockRight`, left of other app bars there); as the shell through `ShellWorkArea` (see the taskbar's
  [Window](taskbar.md#window)), which keeps the taskbar's bottom and the sidebar's right reservation per monitor so neither undoes the
  other's; the sidebar's height comes from what's reserved there, not from Windows' work area, which follows a moment
  later. While the edge is
  dragged only the window moves; the space is reserved again when it's let go.
- No header text: only an add button at the top right, invisible until the pointer is over it (or its menu is
  open), whose menu lists every kind; Profile, Resource usage, Now playing and
  Weather show once only (disabled in the menu while shown), Pictures and Notes as often as wanted. Widgets are cards
  in a scrolling column.
- Hidden or shown from the taskbar's menu ("Show widgets", `ShowWidgetSidebar`); floating widgets stay.
- Right-clicking the sidebar or a widget in it opens its menu: "Show panel background" (`ShowWidgetPanel`) and
  "Add widget". Without the panel the header goes and the window is cut to its cards (`WindowRegion.SetRoundedRects`,
  kept up with layout and scrolling): each widget keeps the backdrop behind it, as floating ones do, and the rest of
  the strip shows the desktop and lets clicks through. The space stays reserved.
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
  settings button (a flyout with the widget's own settings) and a close button, on a solid plate at the top right.
- Pressing anywhere the widget's own controls don't take and moving 4 epx drags it. A widget dragged from the sidebar
  is lifted out of the column (invisible and kept in the tree at its size, so it keeps the pointer, with a negative
  bottom margin so it takes no room; each card keeps its gap below itself rather than the panel's spacing, so a lifted
  one leaves none). While a widget is over the sidebar, the others
  make room for it (moves of a desktop-level window leave its z-order alone, so dragging stays smooth): a
  card-shaped gap opens where it would go (before the first card whose middle is below the
  pointer, measured as if the gap weren't there), and the cards slide (`RepositionThemeTransition`). Off the sidebar
  the widget follows the pointer in its own window, held where it was grabbed, and stays where it's let go. Let go
  over the sidebar it takes the gap's place at once.
- Moving between the sidebar and the desktop makes a new view in the other window (WinUI can't move an element between
  windows), so the move hands over without showing it empty and filling in: a picture of the widget is taken as it's
  pressed (`RenderTargetBitmap`, its pixels copied into a `WriteableBitmap`, as a render target shows only in its own
  window; on the press as it takes 60-200 ms on the VM), and the new view is hidden under it, at its height, until the
  view is ready (`WidgetView.Ready`: once laid out, or for the profile picture, weather, pictures and now playing once
  they show their content) and two more frames are drawn, or 2 s at most. A floating widget dropped on the sidebar
  jumps into the gap, and its window closes once the new card is drawn. The pictures widget goes on with the picture
  it showed.
- A floating note can be resized by its bottom-right corner (`WidgetView.CanResize`): its width (200 to 640 epx) and
  the height of its text (60 to 900 epx), saved as `FloatingWidth` and `ContentHeight`; the text keeps its height when
  the note is docked.
- `ShellSettings.Widgets` keeps every widget, docked and floating; the docked ones in the sidebar's order. Each has an
  id, its kind, a position while floating and its kind's options (`WidgetSettings`; unset options take defaults and
  aren't written). A widget's view is made anew when it moves between the sidebar and the desktop, so what it keeps
  lives in its settings, a file or a shared service. The default widgets have fixed ids.

### The widgets

- **Profile**: the account picture (`AccountPicture\Users\<SID>`, as Start), the user's display name, the time
  (optionally with seconds) and the date.
- **Resource usage**: CPU (Processor Utility, as Task Manager), GPU (the busiest engine, summed over processes, as
  Task Manager; left out without GPU counters), memory used of total, disk activity (100 - idle time) with the free
  space of the fixed drives, network down and up (bits a second) over the adapters that are up and carry IP (each
  adapter's filter drivers are listed as adapters too, with the same counts). Its settings show each drive and each
  adapter as a row of its own. Sampled once a second by `ResourceMonitor` (PDH through `SystemUsage`, wildcard
  counters for GPU engines and logical disks, `GlobalMemoryStatusEx`, `DriveInfo`, `NetworkInterface` statistics;
  read on the thread pool, about 10 ms a time) only while the widget is shown; it keeps the last minute of each, so moving the widget keeps its graphs. Each row
  expands to its graph (networks scaled to the minute's peak). A bar shows use and goes while the graph is open; a
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
