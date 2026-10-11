# Window frames: XP's Luna through overlay windows

Feasibility spec for drawing XP's blue Luna frame over other apps' windows with overlay windows of a separate program:
option C of [Window frames](window-frames.md#options), worked out and tested. Part of the
[NeoShell design](../design.md).

Status: **feasibility spike only** (2026-10-11, title-bar-prototype branch). Nothing is built in the repo. The
findings come from a throwaway WinForms spike (overlay engine, Luna-like drawing, test windows) run on this VM
(Windows 11 25H2, build 26200, one 1764x988 monitor at 100%), measured with logs, `CopyFromScreen`, `PrintWindow`,
UIA-free `mouse_event` input and 60 fps `ffmpeg -f gdigrab` recordings stepped frame by frame.

The owner's constraints: a **standalone program**, not NeoShell itself (NeoShell's Settings configures it); it runs
**elevated** so it can style elevated apps too; **public Windows APIs only** plus its own drawing; it **never loads
code into other processes or into dwm.exe**; the artwork is our own, drawn to match Luna (no Microsoft theme files or
images in the repo).

## Verdict

Feasible, with known seams. Classic frames (Win32, WinForms, WPF, dialogs, mmc, regedit, Control Panel applets,
installers) get a convincing Luna caption, borders and buttons; the app keeps working
normally underneath. What can't be hidden:

- **Moves can lag one composition frame.** The overlay follows an out-of-context location event, so on some frames
  of a drag the app has moved and the overlay hasn't (measured: 1-3 of 20-25 position updates). With the app's own
  caption coloured Luna blue underneath through `DWMWA_CAPTION_COLOR` the lag shows blue on blue and is hard to see.
- **Thumbnails, Alt+Tab, Task View, minimize/restore/maximize animations and the app's own screenshots show the
  app's real frame** (DWM never sees our windows as part of it): Windows 11's frame in Luna blue with Windows 11
  caption buttons.
- **Apps that draw their own title bar** (Chromium, Electron, Edge, Office, Visual Studio, Terminal, WinUI/UWP,
  Task Manager, Paint, Notepad) are left alone: there is no standard caption to cover, only their client area.
- **The Snap layouts flyout** of the maximize button is lost (our buttons sit over Windows' own).
- A hung app's ghost window, a flashing caption and a few other states show Windows' look (below).

## Experiment setup

- Targets: WinForms windows in medium-IL processes (started through the scheduled-task helper), one elevated, one
  with a custom title bar (client area extended over the caption in `WM_NCCALCSIZE`, as Chromium), one dialog-style
  (close button only), one with an owned modal dialog over its caption; plus the elevated Registry Editor
  (`regedit -m`). Each could be hung for 8 s, made topmost or retitled on request.
- Overlay engine: an elevated process (high IL), one tracker per target window, drawing with GDI+ into per-pixel-alpha
  layered windows.

## 1. The overlay windows

### Kind

Per styled window, five windows of ours:

| Window | Covers | Styles |
|---|---|---|
| Caption | the title bar band, from the visible frame's top to the title bar's bottom, 4 px wider than the frame on each side | `WS_POPUP`, `WS_EX_LAYERED \| WS_EX_TRANSPARENT \| WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW` |
| Left, right, bottom | XP's 4 px frame, drawn **outside** the app's visible bounds, inside Windows 11's invisible 7 px resize border | as the caption |
| Buttons | the caption's right end: our three buttons and Windows' own caption buttons under them (`DWMWA_CAPTION_BUTTON_BOUNDS`) | as the caption **without** `WS_EX_TRANSPARENT`, class style `CS_DBLCLKS`; answers `WM_MOUSEACTIVATE` with `MA_NOACTIVATE` |

- Pixels come from `UpdateLayeredWindow` (`ULW_ALPHA`, premultiplied 32 bpp DIB); it sets position, size and content
  in one call. Moves without a size change are `SetWindowPos(SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE)` only, no
  redraw. Strips keep memory small (a 634x31 caption is 78 KB) where one window over the whole frame would need a
  full-size bitmap per window.
- `WS_EX_TRANSPARENT` on a layered window makes it invisible to hit testing, so the caption's drag, double-click,
  right-click (window menu) and the resize borders stay the app's own (so should Snap by dragging and Aero Shake; not
  tried). Measured: `WindowFromPoint` over the caption and borders returns the app's window.
- `WS_EX_TOOLWINDOW` keeps ours out of Alt+Tab and the taskbar; `WS_EX_NOACTIVATE` keeps clicks on our buttons from
  taking the foreground. **Not topmost.**

### Z-order: owned by the app's window

The overlays are created with the app's window as **owner** (`hWndParent` of a `WS_POPUP`; accepted across processes
and integrity levels, `GW_OWNER` reads back the app's window). Then:

- A new owned window starts at the **top of the z-order**, above unrelated windows (seen: our caption over an
  Explorer window that covered the app). Once, after creating them, restack them just above the app:
  `SetWindowPos(ours, <window above the app, skipping ours>, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)`.
- From then on the system moves owned windows with their owner: activating the app brings ours along, activating
  another window covers both (z-order lists checked after each click).
- Minimizing the owner hides ours (`WM_SHOWWINDOW` with `SW_PARENTCLOSING`); restoring shows them again
  (`SW_PARENTOPENING`).
- An app's own owned windows (a modal dialog placed over its caption) stay above our overlays of it, and the dialog
  gets overlays of its own above it. Measured, and the screenshot looks right.
- Making the owner **topmost** made all its owned windows topmost, but the buttons window ended up *below* the app
  (the others above). The tracker must check the order (`GetWindow(app, GW_HWNDPREV)` should be one of ours) on
  `EVENT_OBJECT_REORDER` and on foreground changes, and restack.

**Cross-thread ownership attaches input queues** (as Raymond Chen describes: an owner/owned pair on different threads
shares one input queue, and this can't be undone). Measured: on our thread `GetActiveWindow()` returns the app's
window while it's active; `AttachThreadInput(ours, app's, FALSE)` returns TRUE but changes nothing. Consequences:

- With **one overlay thread for all apps**, every styled app's queue is joined through ours: while app A hung (8 s
  `Sleep` on its UI thread), a click on app B activated B only **after 8058 ms**. With **one overlay thread per app
  UI thread**, B activated in 1044 ms (the test's own click delay) during A's hang. So: one thread per target UI
  thread (windows of the same app thread can share it), each with its own message loop, hooks and windows.
- Our thread must never block: anything slow (icon fetch with `SendMessageTimeout`, file IO) goes elsewhere.
- Our own windows kept getting mouse input during the app's hang (hover states changed).
- The high-IL helper's queue joined to a medium-IL app's: UIPI still blocks the app's messages to our windows, and
  our windows take only mouse input and post only `WM_SYSCOMMAND` / `WM_NCLBUTTONDOWN` to the app. Worth a security
  review before shipping, but nothing found that a medium app could use.

The alternative, unowned overlays restacked after the app on every z-order event, avoids the joined queues but
trails every activation by an event round trip and needs `EVENT_OBJECT_REORDER` handling for every window; not
tried, since ownership worked.

### Following the window

Out-of-context WinEvent hooks (`WINEVENT_OUTOFCONTEXT`: nothing is loaded into the app), per tracker thread, filtered
to the app's process id (so the pointer's location events elsewhere cost nothing: 0 ms CPU over 100 pointer moves):

| Event | Handling |
|---|---|
| `EVENT_OBJECT_LOCATIONCHANGE` (`OBJID_WINDOW`) | re-measure; move, or redraw if the size or maximized state changed |
| `EVENT_SYSTEM_MOVESIZESTART` / `END` | optional hide during the move; full redraw at the end |
| `EVENT_SYSTEM_MINIMIZESTART` / `END` | see restore below |
| `EVENT_OBJECT_SHOW` / `HIDE` / `DESTROY` | show / hide / destroy ours |
| `EVENT_OBJECT_CLOAKED` / `UNCLOAKED` | hide / show (virtual desktops, cloaked UWP frames; not run live) |
| `EVENT_OBJECT_NAMECHANGE` | new title (and icon) |
| `EVENT_SYSTEM_FOREGROUND` (all processes) | active or inactive look |
| `EVENT_OBJECT_REORDER` | check our order (topmost case above) |

Measured while dragging a window with the mouse (41 steps over 1 s): events arrive 0-16 ms after they are raised
(the timer's resolution), moving five windows takes no measurable time, the tracker used 31 ms of CPU for the whole
drag. In the 60 fps recordings (this VM composes about every third captured frame) the overlay was one update behind
in 3 of about 25 position changes (offset 7 px for one composed frame) in the first run and 1 of 20 in the second;
otherwise aligned to the pixel. Behind a lagging overlay the app's own caption shows: white by default, Luna blue with
`DWMWA_CAPTION_COLOR`. A "hide while moving" variant shows the app's own blue DWM caption (Segoe UI title, Windows 11
buttons) for the whole drag; following looks better.

Resizing: every location event redraws (GDI+ 3-5 ms per caption at 634 px; first draw 25-75 ms while GDI+ and the
JIT warm up). The recorded resize stayed aligned.

### Minimize, restore, maximize

- Minimizing: the system hides ours with the owner.
- Restoring: the system shows ours **at once**, before DWM's restore animation has finished, so a Luna frame stands
  empty at the final place for about 150 ms while the window grows into it (recorded). Swallowing `WM_SHOWWINDOW`
  `SW_PARENTOPENING` doesn't stop it. What works: on `SW_PARENTCLOSING` move ours off screen (-32000), and repaint them
  in place 300 ms after `EVENT_SYSTEM_MINIMIZEEND`. Recorded: the window restores with its own caption, Luna appears
  about 100 ms after the animation ends.
- Maximized: the caption covers Windows' 23 px maximized title bar at the monitor's top (borders off screen, no
  rounded corners; our 21 px buttons fit with 1 px to spare). The maximize/restore animations show the app's own
  frame, as above.

### Active and inactive

Luna's caption is blue when active and pale (#7A96DF family) when inactive. Findings:

- `GetWindowInfo().dwWindowStatus & WS_ACTIVECAPTION` is set by the app's `WM_NCACTIVATE` and can lag the
  foreground event (read on the event, it said inactive for a window that had just been restored and activated):
  read again 250 ms later, as the prototype's `WindowFrames` does for dark mode.
- It can also be **stale**: a window that was its thread's active window when another process took the foreground
  kept `WS_ACTIVECAPTION` while DWM drew it inactive (`PrintWindow` showed the grey title). Use: active when
  `WS_ACTIVECAPTION` is set **and** the app's thread is the foreground window's thread.
- An app recovering from a hang repaints its caption active without a foreground event; and a flashing caption
  (a click on a modal dialog's owner) presumably toggles `WM_NCACTIVATE` with no event either (not checked). A cheap
  poll of the foreground window's tracker (every 250-500 ms) covers the first; flashing would need a faster poll and is
  left out.

### Hangs, ghosts, destruction

- A hung app with input waiting gets a **ghost window** in its place (Windows' "(Not Responding)" copy, class
  `Ghost`) and the app's window is hidden: ours hide with it (`EVENT_OBJECT_HIDE`). The spike's discovery then found a
  new window with the app's title and creating windows owned by it failed with access denied even elevated (the spike
  crashed on it; presumably the ghost, which the system owns), so ghosts are skipped by class and show Windows' frame.
  Later hangs didn't ghost again in time to look closer. When the app recovers its window is shown again, and
  `EVENT_OBJECT_SHOW` brings ours back (not seen live: the spike had crashed by then).
- When the app's window is destroyed the system **hides** our cross-thread owned windows but doesn't destroy them:
  the tracker destroys them on `EVENT_OBJECT_DESTROY` (measured: all five still windows at that point).
- If the helper exits or crashes, its windows go with it and the apps show their own frames again; DWM attributes it
  set (caption colour, corners) stay until reset or the window closes, which is harmless.

## 2. Geometry

All rectangles in physical pixels (the helper is per-monitor DPI aware v2):

- Visible frame `V` = `DWMWA_EXTENDED_FRAME_BOUNDS`; client `C` = `GetClientRect` + `ClientToScreen`.
- Title bar bottom = `GetTitleBarInfo().rcTitleBar.bottom + 1`. **Not** the client top: a menu bar lies between them
  (Registry Editor: title bar to y 181, client from 201; the first version covered the menu).
- Caption = (`V.left - 4`, `V.top`) to (`V.right + 4`, title bar bottom); left/right strips from the title bar bottom to
  `C.bottom`, `V.left - 4` to `C.left` (and mirrored); bottom strip `C.bottom` to `V.bottom + 4`. Measured on a
  standard window at 100%: `V` is 7 px inside the window rect, the caption band 31 px (XP's Luna: 25 px caption plus
  frame, about 30 px to the client: a close fit), client inset 1 px from `V`.
- No caption to cover when `C.top - V.top` is under 10 px or `rgstate[0]` of `TITLEBARINFO` says invisible (custom
  title bars, full-screen windows): leave the window alone (measured on the custom-title-bar test window).
- Buttons window = from Windows' own caption buttons' left edge (`DWMWA_CAPTION_BUTTON_BOUNDS`, window-relative) to the
  caption's right end. XP's buttons 21x21 px, 2 px gaps, vertically centred +1, right edge 2 px inside the frame.
  Which buttons: `WS_MINIMIZEBOX`/`WS_MAXIMIZEBOX` style bits, `WS_EX_CONTEXTHELP` (?), and `TITLEBARINFO.rgstate` for
  disabled ones (greyed close of `CS_NOCLOSE` windows).
- DPI: scale our metrics by the **monitor's** DPI, not `GetDpiForWindow(app)` (a DPI-unaware app reports 96 but DWM
  stretches its frame); redraw on `WM_DPICHANGED`-equivalent (a location event with a new monitor). Not run live (one
  monitor at 100%).
- XP drew the window larger than Windows 11 does (4 px frame outside): our strips fall in Windows 11's invisible resize
  border, so they never cover content and resizing still works through them. Windows 11's rounded corners and its 1 px
  border are set square and blue with `DWMWA_WINDOW_CORNER_PREFERENCE` / `DWMWA_BORDER_COLOR` (the prototype's
  `WindowFrame` already does both); the drop shadow stays.

## 3. Input

- Caption, icon, borders: the app's own (transparent overlay). Measured: drag, double-click maximize/restore,
  right-click window menu, resize from the corner.
- Our buttons: hover from `WM_MOUSEMOVE` + `TrackMouseEvent(TME_LEAVE)`, pressed with capture, and on release a
  `PostMessage(app, WM_SYSCOMMAND, SC_MINIMIZE | SC_MAXIMIZE | SC_RESTORE | SC_CLOSE)`. Measured: all four work on
  medium and elevated apps from the high-IL helper (UIPI allows posting downwards).
- The gap in the buttons window left of our buttons (over Windows' own minimize/maximize) must still behave as
  caption: on press, `SetForegroundWindow(app)` (allowed: we received the last input), `ReleaseCapture()` and
  `PostMessage(app, WM_NCLBUTTONDOWN, HTCAPTION, <screen point>)` starts the app's own move loop (measured: the window
  moved exactly with the pointer); double-click posts maximize/restore; right-click posts `SC_KEYMENU` with `' '`,
  which opens the window menu at the window's corner (Alt+Space), not at the pointer: the only public way.
- Lost: Windows 11's **Snap layouts flyout** on hovering maximize (no public API opens it); Win+Z still works. The
  keyboard is never involved.
- Accessibility: screen readers keep reading and invoking the app's own title bar buttons through UIA, which still
  work. Our windows should expose nothing (or mirror the app's buttons); not examined further.

## 4. Which windows

The prototype's `FrameRules.IsEligible` (visible, top-level, has a caption, not tool/no-activate/transparent, not
cloaked, not NeoShell's or the helper's own) plus: a standard caption to cover (section 2), not class `Ghost`, not
full-screen. Rules per process (and class) as today, with "Luna (overlay)" as a style. Dialogs, message boxes and
property sheets get the close-only caption. Owned tool windows with a small caption (`WS_EX_TOOLWINDOW`) are left out
at first (XP drew them with a 15 px caption).

## 5. Process model and elevation

- **A separate executable**, e.g. `NeoShellFrames.exe` (a third project, plain Win32 message loops, no WinUI: each
  tracker thread needs its own message loop and WinUI wants one window thread per window anyway), using
  `NeoShell.Interop` for all calls. It never loads anything into other processes: windows, WinEvent hooks
  (out-of-context), `DwmSetWindowAttribute`, `PostMessage`.
- **Elevated** (high IL): measured that a medium-IL helper **can't** create windows owned by an elevated app's window
  (`CreateWindowEx` fails with access denied), so it styled every medium window and skipped the elevated Registry
  Editor; the elevated helper styled both, and DWM accepts its attributes on elevated windows (the medium prototype's
  are refused, see [Window frames](window-frames.md)).
- **Starting it without a UAC prompt each time:** NeoShell registers a scheduled task once (one UAC prompt when the
  user turns the feature on), "run with highest privileges", at logon and on demand; NeoShell starts it with
  `IRegisteredTask::Run` and stops it with a named event (as the spike did) or `/exit`. Turning the feature off
  removes the task (again one prompt). This is what DWMBlurGlass does, without its injection.
- **Settings:** NeoShell's Settings page writes `settings.json` as now (`WindowFrameStyles`, `WindowFrameRules`; Luna
  becomes a style with `Overlay = "Luna"`); the helper reads the same file (same user profile when elevated) and
  watches it (`FileSystemWatcher`). Nothing is sent from the medium NeoShell to the high helper's windows (UIPI would
  drop it without `ChangeWindowMessageFilterEx`).
- One owner of DWM attributes: while the helper runs it applies the whole style (attributes and overlays) and
  NeoShell's `WindowFrames` stands aside, so elevated windows get the attributes too.
- Recovery: a crash of the helper only removes overlays; NeoShell can restart the task. It must never take NeoShell or
  the shell down with it (separate process).

## 6. Drawing

- Our own artwork: a set of small images drawn for this project to look like Luna (caption left/middle/right for
  active and inactive, the button faces in normal/hover/pressed/disabled for minimize, maximize, restore, close and
  help, and the frame edges), stretched and tiled as XP's msstyles did; or the same drawn in code as the spike did
  (gradients, rounded top corners of 8 px, buttons with a white rim and glyphs). The spike's colours were eyeballed
  from memory of XP's caption (#0058E6 middle, a #3D95FF highlight near the top, darker #0043CB bottom; inactive
  #7A96DF; red close #E5785A to #C5371A): to be matched against a reference screenshot, not Microsoft's files.
- Title: Trebuchet MS Bold 13 px (`trebucbd.ttf` ships with Windows 11), white with a 1 px dark-blue shadow when
  active, #D8E4F8 when inactive; icon from `WM_GETICON` (`ICON_SMALL2`, `SendMessageTimeout` with
  `SMTO_ABORTIFHUNG`) or `GCLP_HICONSM`.
- The spike used GDI+ (System.Drawing through WinForms). For the real helper, without new packages: compose the
  parts into a premultiplied DIB in C# (spans), decode the artwork with WIC (`NeoShell.Interop/Imaging` already
  decodes pictures), and draw the title with GDI `DrawText` onto the opaque caption (the caption is opaque except its
  two rounded corners, so ClearType works; then set alpha 255 there). GDI+ on an alpha bitmap allows only grayscale
  antialiasing.

## 7. Cost (measured, two styled windows)

- CPU: 0 when nothing moves; 31 ms over a one-second drag; a redraw 3-5 ms.
- Memory: 45 MB working set, 11 MB private for the WinForms spike (the WinForms runtime dominates); the bitmaps are
  tens of KB per window. One thread per styled app UI thread (about 1 MB reserved stack each).

## Open questions for the owner

1. Is the result worth the seams (one-frame lag, the app's own frame in thumbnails and animations, no Snap layouts
   flyout, custom-title-bar apps untouched)? If yes, build it as a third project with the process model above.
2. The helper is a new executable that installs an elevated scheduled task: OK?
3. Artwork: images drawn by us (more faithful) or drawn in code (no assets)? Either way a reference screenshot of XP
   is needed to match the colours and sizes.
4. Same mechanism for other looks later (Windows 7 Basic/Aero buttons, classic): the overlays don't care what they
   draw, but glass would need `DesktopAcrylic`-like blur behind our windows, which a layered window can't have.
