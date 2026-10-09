# Screenshots

Screenshots and snips (Win+PrtScn, Win+Shift+S, Print Screen). Part of the [NeoShell design](../design.md).

## Screenshots (`Capture/`)

- Pictures are copied from the screen DC (`BitBlt` with `CAPTUREBLT`, opaque), put on the clipboard as a bottom-up
  `CF_DIB` and saved as PNG (`BitmapEncoder`) in Pictures\Screenshots (`FOLDERID_Screenshots`, created if missing)
  as Windows names them: "Screenshot 2026-10-05 183207.png", " (2)" on for more in the same second
  (`Screenshots`, unit tested).
- Win+PrtScn: the whole virtual screen.

### Snips: Win+Shift+S and Print Screen (`ScreenSnip`, `SnipToolbar`, shell mode)

Under Explorer both open Snipping Tool's overlay (`ms-screenclip:`, an ordinary topmost WinUI 3 `XamlWindow` per
monitor, "Snipping Tool Overlay"). Without Explorer Snipping Tool starts but shows nothing (it can't capture a monitor;
see [Hotkeys](hotkeys.md#hotkeys-shell-mode-only), Win+Shift+R), so NeoShell draws the overlay itself, as Snipping Tool's (11.2607 on 25H2, studied with
UIA, 60 fps recordings, its XBF resources and settings hive), and hands the snip to Snipping Tool's editor, which does
work as the shell.

- **Opening.** The whole virtual screen is taken first and shown frozen, one topmost window per monitor, dimmed with
  black at 60 % (measured: every pixel × 0.4). No animation: Snipping Tool's warm overlay appears from one frame to
  the next. The toolbar is on the primary monitor wherever the pointer is.
- **Toolbar** (measured on Snipping Tool's): top centre, its border 12 px from the top; 8 px corners, a 1 px border of
  white at 20 % (black in light mode), `SystemControlAcrylicElementBrush` (in-app acrylic over the frozen picture),
  padding 8 by 4. Snipping Tool's holds the snip/record switch (76×32: the selected half a `ControlFillColorDefault`
  knob with a 16×3 accent pill under the camera), the mode list (a 64×40 ComboBox showing only the mode's glyph, its
  chevron 16 px in from the right), Quick mark-up, a separator, the colour picker and text extractor (pinned "auxiliary
  modes", each with a "new" dot), a separator and Close (32×32, "Close (Esc)"). NeoShell's has the switch (recording
  shown unavailable: recording needs Windows.Graphics.Capture, which fails without Explorer), the mode list, a
  separator (white at 10 %, 24 tall) and Close; Quick mark-up, the colour picker and the text extractor (OCR with
  Snipping Tool's own engine, its own overlay) are Snipping Tool's and left out. Tooltips "Snip", "Snipping area",
  "Close (Esc)", below. The list: Rectangle (F407), Window (F7ED), Full screen (E9A6), Freeform (F408) in Segoe Fluent
  Icons (the same code points as Snipping Tool's SnSkFluent font), items 8 px wider than their names need.
- **Mode.** Snipping Tool's own setting, read and written in its settings hive (`SnippingToolSettings`):
  `LocalState\SnippingMode` (and `ProtocolSnippingMode`), 1 rectangle, 2 window, 4 freeform, written after each snip
  in that mode (picking a mode and closing doesn't keep it; full screen is never kept). The hive is the package's
  `Settings\settings.dat`, loaded with `RegLoadAppKey` (works while Snipping Tool runs); each value's type is 0x5F5E100
  plus the WinRT `PropertyType` and its data the value plus a FILETIME.
- **Rectangle**: crosshair; the area dragged out shows undimmed, outlined just outside by four 1 px lines of white
  dashes (4 on, 3 off, the first 2 px in from each corner: Snipping Tool draws each side separately); letting go snips
  it at once (no animation). A click without a drag, or a right-click, does nothing.
- **Window**: every top-level window on screen is offered — tool windows, title-less pop-ups, no-activate and topmost
  windows too (tested with nine kinds of WinForms windows), but not minimized or cloaked ones, nor the shell's own
  (Explorer's taskbar and desktop; NeoShell's windows). The topmost one under the pointer (its place reaches 1 px
  beyond its edges) shows undimmed at its DWM frame bounds, no outline; the pointer is an arrow over a window and the
  crosshair over the desktop, where a click does nothing. A click snips the frame bounds of the frozen screen
  (opaque, with what's behind the rounded corners). `SnipTargets`, unit tested.
- **Full screen**: picking it snips the whole virtual screen at once (gaps between monitors black), all monitors.
- **Freeform**: arrow pointer; the path draws as a 2 px white line; letting go fills the path with the accent colour
  at 40 %, fading out in 200 ms, then the overlay goes. The snip is the path's bounding box, transparent outside the
  path (even-odd: a path round twice leaves a hole), antialiased; the path is closed by a straight line. Snipping Tool
  draws it as ink, smoothed by curve fitting; NeoShell smooths with quadratic curves through the midpoints
  (`FreeformPath`, unit tested). With pointer input as dense as a real mouse's the masks match (IoU 0.99 against
  Snipping Tool's for the same injected path); with sparse points (every 30 px) ink rounds corners far more (bounding
  box 299×241 against 317×248).
- **Leaving**: Esc (the list closes first when open) or Close cancels, and so does any other window taking the
  foreground (Snipping Tool's goes too; `WindowTracker.ForegroundChanged`). Tab goes round the toolbar. The windows
  are cloaked before closing: a closing WinUI window shows a black frame.
- **The snip** (`Screenshots.KeepSnip`): on the clipboard as a PNG and a `CF_DIB` (Snipping Tool's: PNG, DIB and its
  OLE clipboard-history flags); a freeform snip's PNG is transparent outside the path and its DIB white there. Saved
  as "Screenshot … .png" in Pictures\Screenshots when Snipping Tool's "Automatically save screenshots"
  (`AutoSaveCaptures`, on when unset) is on, otherwise in Snipping Tool's `TempState\Snips` (which it clears). Then
  Snipping Tool's toast, read from the notification database: hero image (the snip, 2:1 cropped to fill, 180 tall),
  "Screenshot copied to clipboard", "Automatically saved to screenshots folder." or "Automatic save is turned off.",
  and a "Mark-up and share" button. Clicking it opens Snipping Tool's editor with
  `ms-screensketch:edit?&filePath=<escaped path>&isTemporary=false&saved=true&source=Toast` (`isTemporary=true&saved=
  false` for unsaved ones, `source=MarkUpButton` for the button), as Snipping Tool's own toast does. NeoShell shows it
  as a banner of its own with Snipping Tool's name and logo (`ToastPopups.ShowAppBanner`), so it isn't kept in the
  notification center, which Snipping Tool's is (another app's notification can't be posted). Compared at 364×337
  on screen.
- **Print Screen** (`PrintScreenKeys`, unit tested): Explorer (twinui.pcshell
  `CScreenClippingExperienceManager::LookUpPrintScreenSetting`, read with cdb) takes PrtScn through the immersive
  shell's hotkeys unless the policy `HKLM\Software\Policies\Microsoft\Windows\Explorer\MakePrintScreenKeyYieldable`
  is 0 or `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` is 0 (on when neither is set). NeoShell's
  keyboard hook takes PrtScn pressed alone while the setting reads on (read at each press), swallowing it; Alt+PrtScn,
  Win+PrtScn and PrtScn with the setting off are left to Windows.
- Not done: the overlay's text extractor, colour picker, quick mark-up and recording; Snipping Tool's "Add border"
  and HDR settings; the snip sound; a custom save folder other than moving the Screenshots known folder.
