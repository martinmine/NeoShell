# Window frames: XP's Luna through overlay windows

Feasibility spec for drawing XP's blue Luna frame over other apps' windows with overlay windows of a separate program:
option C of [Window frames](window-frames.md#options), worked out and tested. Part of the
[NeoShell design](../design.md).

Status: **feasibility spike**, and XP's Luna measured in an XP VM with a renderer drawing it from the numbers (see
[Luna layout](#luna-layout); 2026-10-11, title-bar-prototype branch). Nothing is built in the repo. The
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
  standard window at 100%: `V` is 7 px inside the window rect, the caption band 31 px (XP's Luna: 30 px from the
  window's top to the client, 29 for dialog frames, measured: one row short; the caption's flat middle rows can take
  the extra row), client inset 1 px from `V`.
- No caption to cover when `C.top - V.top` is under 10 px or `rgstate[0]` of `TITLEBARINFO` says invisible (custom
  title bars, full-screen windows): leave the window alone (measured on the custom-title-bar test window).
- Buttons window = from Windows' own caption buttons' left edge (`DWMWA_CAPTION_BUTTON_BOUNDS`, window-relative) to the
  caption's right end. XP's buttons: 21x21 px, 23 px apart, at y 6 (sizable) or 5 (dialog frame), the rightmost
  ending 6 px (sizable) or 5 px (dialog) inside the window's right edge (measured, [Luna layout](#luna-layout)).
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
at first (XP drew them with a 21 px caption band and a 13 px close button, measured).

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

- Drawn in code (the owner's choice), from numbers measured in XP: see [Luna layout](#luna-layout) and its renderer,
  which matches XP's pixels to within 8 levels on 99.65% of the frame. (The first spike's eyeballed gradients and
  8 px corners are replaced.)
- Title: Trebuchet MS Bold 13 px with GDI grayscale antialiasing, white over a `0A1883` shadow when active, `D8E4F8`
  when inactive (details under Luna layout); icon from `WM_GETICON` (`ICON_SMALL2`, `SendMessageTimeout` with
  `SMTO_ABORTIFHUNG`) or `GCLP_HICONSM`.
- The first spike used GDI+ (System.Drawing through WinForms). The renderer composes the frame into a premultiplied
  buffer in C# and draws the title with GDI `DrawText` onto the opaque caption, as the helper should: no packages,
  nothing to decode. XP used grayscale antialiasing, not ClearType, and so does the renderer.

## 7. Cost (measured, two styled windows)

- CPU: 0 when nothing moves; 31 ms over a one-second drag; a redraw 3-5 ms.
- Memory: 45 MB working set, 11 MB private for the WinForms spike (the WinForms runtime dominates); the bitmaps are
  tens of KB per window. One thread per styled app UI thread (about 1 MB reserved stack each).

## Luna layout

XP's blue Luna frame measured in a real Windows XP (2026-10-11) so it can be drawn in code, and a renderer that draws
it from these numbers, compared with XP pixel by pixel.

### How it was measured

- XP ran in the owner's v86 emulator page in Edge: 1280x720, 32-bit colour, 96 DPI ("Normal size"), "Windows XP
  style", colour scheme "Default (blue)", font size "Normal", font smoothing "Standard" (all read in Display
  Properties). The page shows XP's screen 1:1 (a 1280x720 canvas on 1280x720 screen pixels; one-pixel lines are
  crisp), so `CopyFromScreen` over the canvas gives XP's own pixels. Input: real mouse moves (the page locks the
  pointer, so the XP pointer was found in each screenshot and moved in small relative steps) and keys with scan codes.
- Windows: Notepad (sizable frame), Explorer (My Documents), Calculator (fixed dialog frame, minimize, disabled
  maximize), Run (dialog frame, help and close), Display Properties and its Advanced dialog (long title), a message
  box (close only), Paint's floating Fonts toolbar (tool window). States: active, inactive, each button hovered and
  pressed (pressed, dragged off, released outside), hovered on an inactive window, maximized with the restore button.
- Not seen: the page rounds its canvas's corners, so the outer 11x11 px of a maximized caption's two top corners are
  unknown (the renderer repeats the nearest measured rows there); a pressed button on an inactive window (the press
  activates it); a disabled close button; DPI other than 96.

### Geometry

Pixels, relative to the window rectangle (XP's window rectangle is the visible frame: no invisible borders).

| | Sizable (Notepad, Explorer) | Dialog frame (Run, Calculator, message box) | Tool window (small caption) | Maximized |
|---|---|---|---|---|
| Caption band, window top to client top | 30 | 29: the sizable rows without row 11 | 21 | Window at -4,-4 (frame off screen); 26 caption rows on screen, a gradient of their own |
| Side and bottom borders | 4 | 3: the sizable border's inner three (left drops `0019CF`, right drops `0048F1`, bottom drops `003DDC`) | 3, as dialogs | Off screen |
| Top corners | Rounded: rows 0-4 leave 5, 3, 2, 1, 1 pixels transparent at each end (a window region, no partial alpha) | As sizable | Square | Square |
| Bottom corners | Square | Square | Square | — |
| Icon, 16x16 | (6, 8) | (7, 7) | None | (2, 4) on screen |
| Title x (GDI origin) | 26 with an icon, 7 without | 25 with an icon, 7 without | 3 | 22 with an icon |
| Title top (`DrawText` rectangle top) | 8 | 7 | 5 | 4 |
| Buttons | 21x21 at y 6; the rightmost ends 6 px inside the right edge (x = width - 27); 23 px apart (2 px gaps) | 21x21 at y 5; rightmost at x = width - 26 | Close only, 13x13 at y 5, x = width - 18 | 21x21 at y 2; rightmost ends 2 px from the screen's right edge |
| Title's right limit | Ends before the leftmost button's x - 3, with an end ellipsis (`DT_END_ELLIPSIS`; "...Standard PCI Gra..." matched to the pixel) | As sizable | As sizable | As sizable |

Buttons from the right: close; then maximize (or restore) and minimize; or help instead of them (`WS_EX_CONTEXTHELP`
without minimize/maximize boxes). A dialog with only a close button has just that one.

### Caption gradient, every row

The middle of the caption is one colour per row (the columns vary by ±1-3 in three bands where XP stretched a
three-pixel-wide image; the renderer uses the median).

| Row | Sizable active | Sizable inactive | Active left edge, columns 0-3 | Active right edge, columns w-5..w-1 |
|---|---|---|---|---|
| 0 | `0058EB` | `688DE0` | (corner) | (corner) |
| 1 | `3D95FF` | `98B2E8` | (corner) | (corner) |
| 2 | `2B90FF` | `9DB9EB` | (corner) | (corner) |
| 3 | `0372FF` | `8AACE7` | (corner) | (corner) |
| 4 | `0365F1` | `7EA2E4` | (corner) | (corner) |
| 5 | `005CE9` | `7B9DE1` | 0032C1 004ADC 004BCD 0053DA | 003DCC 0037CB 0032C1 0032C1 0015A0 |
| 6 | `0058E6` | `7A99E0` | 0032C1 0032C1 0041CA 014CD0 | 0031C6 0032C1 002AB7 0026AC 0015A0 |
| 7 | `0054E3` | `7996DE` | 0024A6 0032C1 0041CA 014CD0 | 0031C6 0032C1 0022B9 0024A6 000E7D |
| 8 | `0054E3` | `7996DE` | 0018AB 0032C1 003DCC 0245D0 | 0031C6 002CC8 0022B9 0024A6 000E7D |
| 9 | `0054E3` | `7996DE` | 0018AB 0032C1 003DCC 0047D7 | 0037CB 002CC8 0020C8 0024A6 000E7D |
| 10 | `0055E5` | `7A96DF` | 0018AB 0031C6 003FD2 004AD8 | 0036D2 002CC8 0020C8 0026AC 000E7D |
| 11 | `0055E5` | `7A96DF` | 0018AB 0031C6 003FD2 004AD8 | 0036D2 002CC8 0020C8 0026AC 000E7D |
| 12 | `0055E5` | `7A96DF` | 001ABC 0037CB 003BD2 004ADC | 0036D2 002CC8 0020C8 0024A6 000E7D |
| 13 | `0055E5` | `7A96E0` | 001ABC 0036D2 0040DC 004ADC | 0036D2 0029D0 0020C8 0026AC 000E7D |
| 14 | `0055E5` | `7B97E0` | 001ABC 0036D2 0040DC 004ADC | 0035D8 0029D0 0020C8 0026AC 000E7D |
| 15 | `0055EA` | `7B99E1` | 001ABC 0036D2 0040DC 0050E2 | 0035D8 0036D2 0029D0 002AB7 000E7D |
| 16 | `0055EA` | `7B99E1` | 0020C8 0038D8 0046E4 0051E5 | 003AD9 0035D8 0029D0 0026AC 00138B |
| 17 | `0058EE` | `7D9BE3` | 0020C8 0038D8 0046E4 0051E5 | 003BE0 0035D8 0023D6 002AB7 00138B |
| 18 | `005BF2` | `7D9BE3` | 001FD2 003DDC 0046E4 0055E5 | 0040E1 003BE0 002EDC 002AB7 001592 |
| 19 | `005AF5` | `7F9FE6` | 001FD2 003BE0 0046E4 0055EA | 0041E8 003BE2 002EDC 002AB7 001592 |
| 20 | `0060F9` | `7FA2E6` | 0023D6 003BE0 0046EA 0051EB | 0046EA 0041E8 003BE2 0032C1 001592 |
| 21 | `0164F9` | `80A5E7` | 0023D6 003BE0 004BE7 0058EB | 0047EF 0041E8 0038EA 002CC8 001592 |
| 22 | `026AFE` | `81A7E8` | 001FD2 003BE2 004BEB 0058EE | 0047EF 0046EA 0038EA 0031C6 001592 |
| 23 | `026AFE` | `82A9E9` | 001ACF 003BE2 004BEB 0058EE | 004CF3 0047EF 0038EA 0032C1 001592 |
| 24 | `026AFE` | `82A9E9` | 001ACF 003BE2 004BEB 0058EE | 004CF3 0047EF 0038EA 0032C1 00138B |
| 25 | `026AFE` | `81A7E8` | 001ACF 003BE2 004BEB 0058EE | 004CF3 0047EF 0038EA 0032C1 00138B |
| 26 | `0065FD` | `80A5E7` | 001ACF 003BE0 004BEB 0058EE | 0047EF 0046EA 0038EA 0032C1 00138B |
| 27 | `0060F9` | `7E9DE5` | 001ACF 003BE0 004BEB 004DE3 | 0047EF 0047EF 0038EA 0032C1 00138B |
| 28 | `004DE3` | `7A93DF` | 001ACF 003BE0 003BE0 004BE0 | 0035D8 003BE0 0038EA 0032C1 00138B |
| 29 | `0143CF` | `7A93DF` | 001ACF 003BE0 003BE0 004BE0 | 0043CF 003BE0 002EDC 0032C1 001592 |

| Row | Tool active | Tool inactive | Maximized active | Maximized inactive |
|---|---|---|---|---|
| 0 | `3485EB` | `789AE2` | `3E96FF` | `99B3E9` |
| 1 | `5DB3FF` | `9FBAEB` | `2C8AFF` | `9DB9EB` |
| 2 | `1983FE` | `89ADE8` | `0370FF` | `88AEE9` |
| 3 | `036CF5` | `7C9FE2` | `0465F1` | `7EA1E5` |
| 4 | `026AEF` | `7C9CE2` | `015CE9` | `7B9CE1` |
| 5 | `0265EC` | `7A99E0` | `005AE7` | `7998DF` |
| 6 | `0064EB` | `7A99E0` | `0055E3` | `7998DF` |
| 7 | `0064EB` | `7A99E0` | `0055E3` | `7A97DF` |
| 8 | `0064EE` | `7A99E0` | `0055E3` | `7A97DF` |
| 9 | `0067F1` | `7A99E0` | `0055E6` | `7A97E0` |
| 10 | `0067F1` | `7C9CE2` | `0055E6` | `7A97E0` |
| 11 | `0067F1` | `7A99E0` | `0056E8` | `7B98E1` |
| 12 | `0067F1` | `7C9CE2` | `0054EC` | `7C99E2` |
| 13 | `0067F4` | `7E9DE5` | `0055F1` | `7C99E2` |
| 14 | `006BF8` | `7FA0E6` | `005AF2` | `7E9BE5` |
| 15 | `0270FD` | `80A5E7` | `015DF4` | `7E9EE5` |
| 16 | `0673FE` | `81A7E8` | `005DF9` | `7FA1E6` |
| 17 | `0776FF` | `81A7E8` | `0162FA` | `80A4E7` |
| 18 | `0A78FF` | `82A9E9` | `0269FE` | `81A8E9` |
| 19 | `006BFD` | `7E9CE5` | `036CFE` | `81A9E8` |
| 20 | `014ADF` | `7993DE` | `036CFE` | `82ABE9` |
| 21 | | | `0269FE` | `81A9E8` |
| 22 | | | `0266FB` | `80A6E8` |
| 23 | | | `0162FA` | `7E9EE5` |
| 24 | | | `014DE1` | `7A95DF` |
| 25 | | | `0043CF` | `7A95DF` |

The caption's ends are shaded: past the edge columns, each column adds an offset to its row's colour (left end: 26
columns, up to +8 green and -15 blue near the edge; right end: 43 columns, green -4 to -12), and the first 5 and
last 4 rows of the ends (the rounded corner's highlight, the bottom's shading; 16 columns on the left, 25 on the right)
are measured pixels because they don't separate into row and column. Every number, inactive, tool and maximized
included, is in the renderer's `LunaArt.cs` (below).

### Borders below the caption

| | Left, outside in | Right, inside out | Bottom, top down |
|---|---|---|---|
| Sizable active | `0019CF 0831D9 166AEE 0855DD` | `0048F1 003DDC 001EA0 00138C` | `0048F1 003DDC 001EA0 00138C` |
| Sizable inactive | `5B68CD 7480DC 758CDD 758CDC` | `7586DC 7587DD 6D74CD 4F53BC` | `7586DC 7587DD 6D74CD 4F53BC` |
| Dialog and tool active | `0831D9 166AEE 0855DD` | `003DDC 001EA0 00138C` | `0048F1 001EA0 00138C` |
| Dialog and tool inactive | `7480DC 758CDD 758CDC` | `7587DD 6D74CD 4F53BC` | `7586DC 6D74CD 4F53BC` |

The bottom corners are mitred (a 4x5 or 3x4 patch of their own each, in `LunaArt.cs`); the borders vary by ±1 along
their length (stretched art), which the renderer ignores.

### Title

- Trebuchet MS Bold, 13 px (10 pt at 96 DPI, weight 700); tool windows Tahoma Bold, 11 px (8 pt). Both ship with
  Windows 11 (`trebucbd.ttf`, `tahomabd.ttf`).
- XP's "Standard" smoothing is GDI's grayscale antialiasing: drawn on Windows 11 with `ANTIALIASED_QUALITY` the glyphs
  match XP's to the pixel, edge levels included.
- Active: white `FFFFFF` over a `0A1883` shadow offset (1, 1). Inactive: `D8E4F8`, no shadow. Tool windows: white
  or `D8E4F8`, no shadow.
- The shadow must be drawn by GDI straight onto the caption, then the white text over it: XP's shadow edge pixels are
  much lighter than the text's (GDI blends a dark colour's coverage differently; an edge pixel with 50% white coverage
  has 9% shadow). Compositing the shadow from a white-on-black mask left about 90 pixels of Notepad's title off by
  more than 24 levels; drawing it with GDI leaves 1-3.

### Caption buttons

- **Rim:** white, 1 px, with rounded corners that fade over three pixels: alpha in sixteenths 0, 6, 12 from each
  corner along both sides, and 11-12 on the diagonal pixel inside (the 13 px tool button: 4 at the corner pixel, 5 on
  the diagonal). The rim lies over the caption behind the button, not over the face: an inactive close button's rim
  is bluish (`BCC7ED`), not pink.
- **Face:** a gradient, light at the top left and dark at the bottom right, with sharp light and dark lines just
  inside the rim. Drawn as a bilinear mesh of 9x9 colours (control points at 1, 2, 3, 6, 10, 14, 17, 18, 19 px; 7x7
  at 1, 2, 4, 6, 8, 10, 11 on the 13 px tool button), fitted to XP's buttons: mean error 0.6-1.8 levels, at most
  10.
- **Glyphs** (white, alpha maps in `LunaArt.cs`): minimize a 7x3 bar at (5, 13); maximize an 11x11 window at (5, 5),
  3 px top and 1 px sides and bottom; restore two 7x7 windows, back at (8, 5) and front at (5, 8), 2 px tops; close
  an X from (5, 5) to (15, 15), 3 px strokes with lighter end pixels; help a `?` 9 px wide from (6, 4), antialiased,
  with its dot at (9-10, 15-16); the tool window's close a 7x7 X at (3, 3).

| Face | Top left | Centre | Bottom right | Rim opacity | Glyph opacity |
|---|---|---|---|---|---|
| Blue normal (minimize, maximize, restore, help) | `90AFF9` | `3A76F5` | `0045D3` | 1 | 1 |
| Blue hot | `97B9FF` | `4A94FF` | `0A5DDD` | 1 | 1 |
| Blue pressed | `002A62` | `0050B5` | `0054B9` | 1 | 0.47 |
| Blue, inactive window | `849FEA` | `597FE9` | `3F6DDC` | 0.48 | 1 |
| Blue disabled (Calculator's maximize) | `4C80EA` | `1F5DEA` | `0049E3` | 0.5 | 0.42 |
| Blue disabled, inactive window | `7D97E2` | `6984E2` | `6083E1` | 0.2 | 0.23 |
| Red normal (close) | `EFA390` | `E5603A` | `C22A00` | 1 | 1 |
| Red hot | `FFAEA5` | `FF836E` | `E54930` | 1 | 1 |
| Red pressed | `69220F` | `C2401D` | `C5431F` | 1 | 0.47 |
| Red, inactive window | `B399B5` | `AE748A` | `A06072` | 0.49 | 1 |
| Tool close normal / hot / pressed / inactive | `F0A794` / `FFB4AB` / `912D14` / `B396B6` | | `E23A03` / `FD724D` / `CC4921` / `B06573` | 1 / 1 / 1 / 0.49 | 1 / 1 / 0.49 / 1 |

Hovering a button of an inactive window shows the same hot button as on an active one; the rest of the caption stays
inactive. A pressed button released outside it does nothing and returns to normal.

### The renderer

`LunaFrame.Render(LunaWindow)` draws a window's frame into a premultiplied `0xAARRGGBB` buffer (client area left
transparent): kind (sizable, dialog, tool), active, maximized, title, an optional 16x16 icon, and the buttons with
their states. `LunaArt` holds the numbers above as C# string tables, generated from the measurements; the title goes
through GDI (`CreateFontW`, `DrawTextW` on a DIB section). No image files, no packages; user32 and gdi32 only. It
lives with the spike for now (`E:\NeoShellWork\luna\src\LunaFrame.cs`, `LunaArt.cs`; the measuring and comparing
scripts in `E:\NeoShellWork\luna\ana`), to move into the helper project when that's built.

Compared with XP pixel by pixel (same window size, title and state; XP's icon and pointer left out; every frame pixel
we draw counted), 29 cases, 707,000 pixels: 75.9% identical, 89.2% within 2 levels, 99.65% within 8, 0.08% (583
pixels) off by more than 24.

| Case | Pixels | Identical % | Within 8 % | Off > 24 | Mean |
|---|---|---|---|---|---|
| Notepad active / inactive | 36040 | 74.0 / 83.4 | 99.86 / 99.93 | 3 / 3 | 0.70 / 0.29 |
| Notepad, each button hot / pressed (active) | 35980 | 73.8-74.1 | 99.62-99.84 | 11-87 | 0.71-1.18 |
| Notepad inactive, minimize / close hot | 35980 | 83.5 / 83.2 | 99.94 / 99.71 | 3 / 60 | 0.29 / 0.57 |
| Maximized active / inactive | 32845 | 78.1 / 71.2 | 99.91 / 99.77 | 0 / 2 | 0.71 / 0.59 |
| Maximized, restore hot / pressed | 32785 | 78.0 / 78.1 | 99.86 / 99.86 | 24 / 24 | 0.76 / 0.77 |
| Run active / inactive / help hot / help pressed | 12004 | 70.2 / 77.3 / 69.8 / 70.3 | 99.56 / 99.90 / 99.35 / 99.23 | 10 / 3 / 34 / 26 | 0.90 / 0.43 / 1.25 / 0.99 |
| Calculator active / inactive (disabled maximize) | 9408 | 67.7 / 76.0 | 98.75 / 99.34 | 6 / 3 | 0.91 / 0.51 |
| Explorer, My Documents (not used for fitting) | 31448 | 73.8 | 99.83 | 3 | 0.69 |
| Message box (not used for fitting) | 25468 | 73.0 | 99.85 | 1 | 0.82 |
| Display Properties active / inactive (not used for fitting) | 15442 | 71.6 / 80.8 | 99.64 / 99.92 | 2 / 3 | 0.75 / 0.36 |
| Advanced dialog, title cut with an ellipsis (not used for fitting) | 15442 | 72.5 | 99.64 | 2 | 0.71 |
| Tool window active / inactive / close hot / close pressed | 11538 | 81.1 / 65.2 / 81.1 / 81.3 | 97.63 / 99.77 / 97.29 / 97.63 | 15 / 0 / 58 / 18 | 0.83 / 0.73 / 1.69 / 0.84 |

What still differs, and why:

- **Button faces**, ±1-5 levels (at most 10): the 9x9 gradient mesh smooths XP's bitmap. A denser mesh (11x11) gets
  under 1 level mean; a coarser one (7x7) about 2.5 with ±15 at the edges.
- **Button rim corners**, 10-20 levels on a few pixels per button: the partial pixels' alpha is one map for every
  state.
- **The caption's middle**, ±1-3 in two vertical bands per row (XP stretched a three-pixel image; we draw one colour).
- **The ends' interior rows**, mostly ±1-6 (the shading is modelled as row colour plus a column offset); the tool
  window's caption fits worst (97.6% within 8).
- **Text**: identical glyphs and edge levels; a few edge pixels per title differ by up to 10-20 where the shadow and
  the white overlap.
- **Hot and pressed shots** count some pixels of XP's pointer shadow as differences (the larger "off > 24" counts).
- **Maximized top corners** (11x11 px each) are not compared: the emulator page hides them.

## Open questions for the owner

Answered (2026-10-11): the spike is a good starting point; a separate program with an elevated logon task is fine;
the artwork is drawn in code, no image files (now measured in an XP VM and rendered, see [Luna layout](#luna-layout)).

1. Other looks later (Windows 7 Basic/Aero buttons, classic): the overlays don't care what they draw, but glass would
   need `DesktopAcrylic`-like blur behind our windows, which a layered window can't have.
2. The button faces are 9x9 gradient meshes and parts of the caption's ends measured rows, all fitted to XP's
   bitmaps: numbers, not image files, but close copies of Microsoft's art at this size. If that's too close, a
   simpler drawing (one gradient per row and per column, no mesh) costs about 5-8 levels on the faces.
3. Other DPI scales: XP at 120 DPI had its own, larger Luna art (not measured: the VM's XP runs at 96 DPI). Scaling
   the 96 DPI numbers is the fallback; a second measuring session at 120 DPI in the VM would settle it.
4. Where the renderer lives once the helper project exists (`LunaFrame.cs` and `LunaArt.cs` are in the spike folder
   today, outside the repo).
