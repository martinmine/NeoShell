# Window switcher and Snap

Alt+Tab and Snap: layouts, window snapping, Snap Assist and snap groups. Part of the [NeoShell design](../design.md).

## Window switcher (`Switcher/`)

- As the shell only: alongside, Explorer's Alt+Tab runs; without Explorer Windows would cycle windows with no UI.
- Keys from the keyboard hook (`AltTabKeys`, unit tested): Alt+Tab opens it, Tab/Shift+Tab and the arrow keys move,
  letting go of Alt or Enter switches, Esc cancels, Delete closes the chosen window. Its keys are swallowed (down,
  repeats and up); Alt always passes, masked with vkE8 so the app in front doesn't open its menu bar.
- `WindowSwitcher`: the taskbar's windows of other processes, most recently used first (`AltTabLayout.Order`), the
  previous one chosen (Shift: the last). Shown after 100 ms, so a quick Alt+Tab switches without the panel flashing
  up; moving on shows it at once.
- Most recently used is when each window was last in front, as Explorer keeps it (twinui.pcshell's
  `CWin32ApplicationView::SetLastActivationTimestamp`, sorted by `SwitchItem::GetLastActivationTimestamp`), not the
  z-order: a window minimized from the front stays second, though the stack has it last. `WindowTracker.RecentlyActive`
  moves a window (a dialog's root owner) first on each foreground change; windows that were there before NeoShell
  start in their z-order, and ones never in front go after the rest in z-order. Snap Assist's suggestions and the
  taskbar previews' snap groups use the same order.
- Ctrl+Alt+Tab (Shift: backwards, from the last) opens it to stay, as Explorer's (`CAltTabViewHost::IsSticky`),
  measured on Explorer: shown at once (no animation, opening or closing), with the same first choice; it takes the
  foreground; letting go of the keys switches nothing; Tab and Shift+Tab move without Alt, as do the arrows,
  Ctrl+Alt+Tab and Alt+Tab; Enter or Space switch, as does a click on a card; Delete closes the chosen window and
  the panel stays, re-laid; the mouse wheel does nothing; hovering shows a card's close button but doesn't choose it;
  Esc, or a click outside, closes it. Explorer's window covers the work area, so a click outside only closes it:
  NeoShell puts an all but transparent `ClickCatcher` over the work area under the panel; a click on the taskbar (or
  the widget sidebar, outside the work area) closes it by taking the foreground. After Esc or a click outside,
  Explorer leaves the foreground on its hidden switcher window, so keys go nowhere; NeoShell gives it back to the
  window that was in front.
- Explorer's "Show tabs from apps when snapping or pressing Alt+Tab" (`MultiTaskingAltTabFilter`: 0 the 20 most
  recent tabs, 1 five, 2 three, the default when absent, 3 none) adds Edge's tabs, which Edge hands the shell through
  `Windows.UI.Shell.WindowTabManager`, served by twinui.pcshell's private `WindowTabHost`: no public way to read them,
  so NeoShell shows windows only (as with 3).
- Explorer's look: an acrylic panel centred on the monitor of the window in front; cards in centred rows
  (`AltTabLayout.Arrange`), each the window's colours with icon and title over a live DWM thumbnail 132 epx high
  (a minimized window shows its icon), the chosen one ringed in the accent colour 3 epx off the card; the close button
  shows over a card. More windows than fit make the previews smaller (Explorer's panel scrolls instead).
- Snap groups show as items of their own, just before their first window (see Snap groups).
- Switching: `SetForegroundWindow` after a key of NeoShell's own (vkE8), which lifts the foreground lock (the keys
  went to the app in front). `SwitchToThisWindow` sends the window left behind to the bottom of the stack, which
  breaks the most recently used order.

## Snap layouts (`Snap/`)

Windows leaves all of Snap to Explorer (twinui.pcshell's `SnapComponent`, `SnapAssistController`, `TaskGroups`,
fed by win32k through the private `NtUserRegisterWindowArrangementCallout`): as the shell NeoShell does it itself.

### Settings (`SnapSettings`, unit tested)

Settings → System → Multitasking → Snap windows, found by toggling each checkbox and diffing the registry; read each
time they're needed, so changes count at once. The checkboxes are on unless their value is 0; with "Snap windows" off
none of them count, and Win+Left/Right do nothing (Up still maximizes, Down restores or minimizes).

| Setting | Value |
|---|---|
| Snap windows | `HKCU\Control Panel\Desktop\WindowArrangementActive` (string, `SPI_SETWINARRANGING`) |
| When I snap a window, suggest what I can snap next to it | `Explorer\Advanced\SnapAssist` |
| Show snap layouts when I hover over a window's maximize button | `Explorer\Advanced\EnableSnapAssistFlyout` |
| Show snap layouts when I drag a window to the top of my screen | `Explorer\Advanced\EnableSnapBar` |
| Show my snapped windows when I hover over taskbar apps, in Task View, and when I press Alt+Tab | `Explorer\Advanced\EnableTaskGroups` |
| When I drag a window, let me snap it without dragging all the way to the screen edge | `Explorer\Advanced\DITest` |

(`JointResize`, `SnapFill` and `SnapTabs` are older values the 25H2 page no longer shows.)

### The layouts (`SnapLayouts`, `SnapLayoutPicker`, unit tested)

- Windows 11's: halves, an uneven pair, a half and two quarters, four quarters; with at least 1920 effective pixels
  of work area also thirds and a wide middle; stacked rows on a portrait screen. Below 1920 the uneven pair is 60/40,
  not two thirds (Explorer's zone was 1058 of 1764 pixels); above, two thirds (not checked: no such screen here).
- Before them, 24H2's suggestions: the halves with another app's window beside this one, and a half beside two
  stacked, each zone of a suggested window showing its icon instead of the fill. The windows suggested are the most
  recently used of other apps, one each (the window's own app counts when it has other windows). Picking any zone of
  a suggestion puts the window in its own zone and the suggested windows in theirs, and makes them a group.
- Measured at 100% (Explorer's dark theme): previews 98x64, 12 apart; zones 4 apart with only the layout's outer
  corners rounded (4); fill white at 16%, outline white at 42%; the zone under the pointer fills with the accent
  (`SystemAccentColorLight2`), a suggestion layout lights its own zone and outlines the suggested ones. The flyout's
  acrylic is darker than the system default (measured #152E20 over a #2E8B57 window; NeoShell's: tint opacity 0,
  luminosity 0.66, giving #102F1E). The light theme's colours are the dark ones in black, not measured.

### The flyout: hover and Win+Z (`SnapLayoutsWindow`, `MaximizeButtonHover`, `MaximizeButton`)

- Explorer shows one flyout for both: centred under the window's maximize button, its top a pixel below the button;
  233x244 at 100% (13 from the edge to the layouts, 14 at the top), two columns. Win+Z adds each layout's number in
  its middle (an opaque #767676 block 13x24, white digit) and takes the keyboard: nothing is chosen until an arrow
  key, Enter picks, a number picks a layout and then a zone (`SnapLayoutsWindow`), Esc or a click elsewhere closes it.
- Hover: the pointer resting on the button opens the flyout about 630 ms later (Explorer's: 636-640 ms to its window,
  then a 100 ms fade); leaving both the button and the flyout closes it 230 ms later (Explorer's: 228 ms); a click
  anywhere else closes it at once. It doesn't take the focus. It shows for maximized windows (on the restore button)
  too. Windows hides the button's own "Maximize" tooltip under Explorer's; NeoShell hides it
  (`TopLevelWindows.HideCaptionTooltip`).
- Finding the button: the pointer is followed on the thread pool every 50 ms (`MaximizeButtonHover`), since asking a
  window waits for its thread. Windows' own caption buttons are DWM's: `DWMWA_CAPTION_BUTTON_BOUNDS`, the middle of the
  three 46 effective pixel buttons from the right; `WM_NCHITTEST` there answers with the caption's old metrics, about
  20 pixels to the right of what's drawn. Windows with their own title bars (WinUI, Chromium, Electron, the test
  app's) answer `WM_NCHITTEST` with `HTMAXBUTTON` for theirs, as Windows asks them to; the button's extent is found
  by asking point by point (`MaximizeButton`). Explorer gets it from win32k instead. Apps running as administrator
  answer nothing to a medium process (UIPI), so they get no hover flyout from NeoShell.
- New WinUI windows are black until their first frames are drawn: the flyout opens off the screen and moves into
  place once drawn (`FirstFrame`), then fades in.

### Window snapping (`WindowSnapping`, shell mode)

Windows leaves snapping to Explorer: without it, a window dragged against an edge just moves, and Win+arrows do
nothing (they stay registered as hotkeys, so the keyboard hook takes them: `SnapKeys`, tested).

- **Dragging.** `EVENT_SYSTEM_MOVESIZESTART`/`END` (WinEvents) bracket the app's own move loop; in between the pointer
  is polled every 30 ms. A window whose size changes is being resized, not moved, and doesn't snap. The zone under
  the pointer (`WindowSnap.AtPointer`, tested): against the left or right edge a half, within an eighth of the work
  area's shorter side from a corner a quarter, against the top edge maximized (a quarter near its corners). With
  "snap without dragging all the way to the screen edge" (`DITest`, on by default) the sides count from 63 effective
  pixels away and the top from 7, as measured on Explorer; off, only the edge. (With it off, Windows' own top-edge
  maximize still answers within 6 pixels of the top without Explorer; Explorer suppresses it.) During a move Windows
  keeps the pointer inside the work area (`ClipCursor`), which left windows unable to go over the widget sidebar; as a
  move starts NeoShell widens the clip over the sidebar's strip (`ShellWorkArea.DragArea`: the work area without the
  sidebar's share), and Windows frees it when the move ends. The edges are that area's: the taskbar's edge at the
  bottom, the screen's beside the sidebar. While the pointer is in a zone, `SnapPreview` shows it: an acrylic,
  rounded outline 8 epx inside the zone, just behind the dragged window (`PinnedLayer.Normal` below it). Let go, the
  window fills the zone (`TopLevelWindows.Place`, as Win+Z) or is maximized.
- **Work areas are NeoShell's own** (`ShellWorkArea.Monitors`): zones, layouts, the Snap bar, Snap Assist and the
  groups' previews go by what NeoShell reserves on each monitor, not by Windows' work area, which is the whole
  monitor for a while after the displays change (as right after signing in) until NeoShell hears of it and the
  reservations go out again (see the [taskbar's](taskbar.md#window) "Display changes"); T27 once saw windows snapped under the taskbar
  then. Tested in T40 by setting Windows' work area to the whole monitor: Win+Left still snapped to 722x940.
- **Elevated windows aren't snapped**: NeoShell can't move, size or maximize them (see the [taskbar's](taskbar.md#task-buttons) "What gets
  through to an elevated window").
- **The Snap bar** (`SnapBar`, with "Show snap layouts when I drag a window to the top"): once the window moves, a bar
  of the layouts in a row (one suggestion and the four or six layouts; 562x88 at 100% with 12 around the layouts)
  slides in to peek 10 epx down from the top centre of the monitor (70 ms; Explorer's starts about 100 ms after the
  move and takes about 67 ms). The pointer within 12 epx of the top over the bar brings it down to 25 from the top
  (180 ms, decelerating; Explorer's measured 180 ms); leaving it sideways or below sends it back (200 ms). The zone
  under the pointer lights up and the screen shows its preview; let go there, the window goes into it (a
  suggestion's windows too). The app's move loop has the mouse, so the bar follows the polled pointer. Not done:
  Explorer shrinks the dragged window while it's over the bar (a DWM effect on another process's window).
- **Its own size back.** The bounds a window had before it was snapped are kept; dragged out of its zone (not just
  clicked on its title bar), it gets that size back under the pointer, at the same share of its width as where it was
  grabbed (`WindowSnap.Unsnapped`, tested). A maximized window dragged is restored by Windows itself first. A snapped
  window stays snapped underneath when maximized, as in Windows; sized by hand, it isn't snapped any more.
- **Win+arrows** (`WindowSnap.AfterKey`, tested), Windows 11's moves, as measured on Explorer with one and two
  monitors: Left/Right snap to that half, cross over to the other quarter, give the window its own bounds back from
  the other half or from maximized, and from the far half or quarter go on to the next monitor's near one (Win+Right
  on the right half: the next monitor's left half; the top right quarter: its top left one), round from the last
  monitor to the first; with one monitor round the same one (right half, Win+Right: left half). Up goes from a half
  to its top quarter, from a bottom quarter to the half, and maximizes the rest (a top quarter too); Down undoes
  those, restores a maximized or stretched window and minimizes what's left. Once per press. Win+Left/Right go
  through the monitors left to right (by their left edges); the window's own bounds go along to the other monitor as
  Windows moves them (below), and Snap Assist comes up there as after any snap.
- **Win+Shift+arrows.** Windows itself (win32k's `xxxArrangeWindow`, its hotkey table read with cdb) still does two
  of them without Explorer: Win+Shift+Left/Right move a window to the previous/next monitor in its list of them
  (`EnumDisplayMonitors`' order), round from the last to the first, with more than one monitor; Win+Shift+Down
  restores a maximized window. A maximized window stays maximized; restored after the move it goes back to where it
  was on the first monitor (Windows' own, with or without Explorer). Win+Shift+Up (stretch) and Win+Up/Down/Left/Right
  are left to the shell and do nothing without it. So `SnapKeys` lets Win+Shift+Left/Right/Down pass to Windows unless
  `WindowSnapping.Takes` says the window in front is one NeoShell snapped or stretched (Windows would move or leave
  it as an ordinary window); Win+Shift+Up it always takes. What each does, as Explorer's:
  - Up stretches the window to the work area's height at its own place and width (`SnapPosition.Tall`; Explorer's
    vertical maximize); a quarter becomes its half; halves, maximized and stretched windows stay. No Snap Assist.
  - Down gives a snapped, stretched or maximized window its own bounds back; a window that's neither stays.
  - Left/Right take a snapped window to the same zone of the other monitor, its own bounds going along; a stretched
    one is maximized there (Explorer's, oddly: restored, it's back stretched on the first monitor). With one monitor
    nothing happens. No Snap Assist.
- **Its own bounds on another monitor** (`WindowSnap.OnMonitor`, tested against Explorer's results; win32k's
  `AdvancedWindowPos::xxxTransformRectToMonitor`, decompiled with Ghidra): the size is scaled by the monitors' DPIs
  (`MulDiv`, for a per-monitor aware window); what's drawn of the window (the bounds less the invisible borders at
  the new DPI, `WindowMargins::ReduceRect`) keeps its share of the way across and down the monitor, not the work
  area: `x + (from.Width / 2 + x * (to.Width - from.Width)) / from.Width` from the monitor's corner, in C's integer
  division; then `FitRectToWorkArea`: pushed back in from the right and the bottom, then the left and the top, and cut
  to the work area where a resizable window is still too big. Measured, 1764x988 to 1280x800 on its right: a window at
  (300, 200) comes out at (1981, 163); at 125% it grows to 875x625 at (1980, 123), its 8 pixel borders there instead of
  7 (`TopLevelWindows.ResizeBorder`: `SM_CXSIZEFRAME` + `SM_CXPADDEDBORDER` less one). A window snapped by dragging
  from another monitor keeps its own bounds there; they're moved from the monitor they're on.
- **Placing windows that aren't per-monitor DPI aware.** `TopLevelWindows.Place` moves a DPI-unaware or system-aware
  window in its own coordinates (the thread switched to the window's DPI awareness, the rectangle scaled to the
  monitor as that window sees it): placed in screen pixels, Windows scaled such a window again on the monitor at 125%
  (a 740 high zone came out 927 high), and on the 100% monitor beside it a right half came out a pixel narrow where
  its invisible border reached the 125% monitor. A window moved to a monitor at another DPI is placed twice: its app
  may size it for the new DPI as it arrives, and its invisible borders change.
- Compared side by side with Explorer (two monitors, the second at 100% and at 125%, and one monitor): the same
  13 key sequences on a fresh per-monitor aware window each, every state's extended frame bounds, the same to the
  pixel, Snap Assist coming up after the same keys. Differs: a DPI-unaware window's own bounds can land a pixel off
  on a monitor at another scale (Windows works them out in the window's scaled coordinates); Win+Left/Right go round
  monitors left to right, which may not be Explorer's order with three or more (only two could be tried).
- **Window motion: Windows' arranged state** (T39d). Explorer's snaps animate (about 250 ms, recorded at 60 fps: the
  window's old picture and its new content crossfading while both slide and scale from the old place to the new one,
  decelerating) because of *how* the window is moved, not by anything Explorer draws: twinui.pcshell's
  `CShellSnapComponent::SnapWindow` calls user32's `ShellSetWindowPos(hwnd, 0, &frameRect, 3, 0x10, 1)` (traced with
  cdb on Win+Right: the rectangle is what's drawn of the window), win32k applies it as a window action that puts the
  window in Windows' *arranged* state (`AdvancedWindowPos::xxxUpdatePosAndStateForAction` sets the window's
  move-reason bit for state 3, `DwmNotifyMoveReason` tells DWM), and DWM runs its own arrangement transition
  (udwm's `CWindowArrangementTransition`). None of DWM's transition APIs (`DwmpTransitionWindowWithRects`, types
  0-63, as another process) nor a plain `SetWindowPos` does it. The documented `ApplyWindowAction` (WinUser.h, 26100
  SDK) with `WAK_PLACEMENT_STATE` and `WPS_ARRANGED` does, but only for the caller's own windows (access denied
  otherwise) — unless the thread has the immersive window manager's access: `NtUserApplyWindowAction` then skips
  both the integrity check and the same-thread check. That access comes from user32's unnamed `AcquireIAMKey`
  (ordinal 2509) and `EnableIAMAccess` (2510), which win32k grants only to the process that registered the shell
  window (`SetShellWindow`) and only while no other thread holds it (read in `NtUserAcquireIAMKey`; a live or still
  exiting Explorer does). So as the shell NeoShell takes it on its UI thread at start
  (`ShellRegistration.TakeWindowManagerAccess`, logged), and Snap places windows with `TopLevelWindows.Arrange`
  (`ApplyWindowAction`: position, size and `WPS_ARRANGED`, `WAM_FRAME_BOUNDS`, `WAM_DPI` with the monitor's DPI) and
  gives them their own bounds back with `Unarrange` (`WPS_NORMAL`): Win+arrows, a drag let go at an edge, the bar,
  the layouts flyout and Snap Assist's picks animate as Explorer's, recorded side by side at 60 fps (normal to a half,
  a half to a quarter, a half back to its own bounds, a pick from Snap Assist, a drop at the edge; Win+Up/Down's
  maximize and restore are `ShowWindow`'s, which Windows animates for both). The process ending gives the access up
  (Explorer took it again each time after NeoShell's `/exit`). Without it (log: "refused") windows are placed as
  before and jump. Windows applies the action on the window's own thread: `Arrange` waits for the window to move (at
  most 100 ms, for a hung app) so Snap Assist's empty zones see it in its zone. An arranged window dragged gets its own
  size back from Windows itself as the drag starts (as Explorer's snapped windows do), so `WindowSnapping` takes that
  first change of size as the unsnap, not as a resize by hand. Not compared: a move between zones of the same size
  (a quarter across to the other), which DWM doesn't animate (ApplyWindowAction tried directly); Explorer's layouts
  always made the sizes differ when tried. Not run live: Win+Shift+arrows across monitors and DPI-unaware windows on a
  monitor at another scale with `ApplyWindowAction` (one monitor at 100% this time).

### Snap Assist (`SnapAssist`, `SnapAssistPlan`, unit tested)

- After a snap into a zone (dragging, the bar, the flyout, Win+arrows), the layout it starts or completes is worked
  out as Explorer does (`SnapAssistPlan.EmptyZones`): of the layouts with that zone, the one the other snapped windows
  on the monitor (each exactly in its zone, not minimized) fill most of; then one of zones the zone's size; then the
  fewest zones. So a half alone leaves the other half, a quarter alone the other three quarters (in reading order),
  a quarter beside a window in the other half the last quarter; a half beside one in the other half completes the
  layout (no Assist). A layout picked in the flyout or bar is its own. The windows in a layout's zones form a group.
- The empty zones show as panels 12 epx inside them, rounded 8, of blurred wallpaper (only the wallpaper: windows
  behind don't show, as in Explorer); around them the wallpaper sharp. The first zone has the windows as cards: the
  other windows of the taskbar that can be resized, most recently used first (as Alt+Tab orders them), minimized ones
  too, showing the picture DWM keeps of them. A card is a 40 epx title (icon, title, a close button over the card)
  above the live preview, the card as wide as the preview's shape (what's drawn of the window, without its invisible
  borders). Rows as Alt+Tab's, as many as the square root of the number of windows (rounded), the previews as high as
  fits across and down, rows centred (`SnapAssistPlan.Arrange`; Explorer's measured: right half with four windows
  260 high, a quarter with five 178, cards 24 apart, 18 in from the panel's sides). Explorer's scrolls when they don't
  fit; NeoShell's shrink.
- Picking a card (a click, or arrows and Enter) puts its window in the zone without activating it and moves on to the
  next zone; after the last the window picked comes to the front. Arrows go round the cards and wrap (Up from the top
  row goes to the bottom one), Home and End jump, the chosen card is ringed in the accent once the keyboard is used,
  Esc ends it and gives the focus back to the snapped window, as does a click elsewhere (deactivation, or another
  window coming to the front).
- **Showing** (T39d, recorded at 60 fps side by side): Explorer's appears whole about 435 ms after the snapped window
  starts moving (its own animation is over by about 250 ms): the panels, cards and titles at once, no fade, the live
  previews flying in from where their windows are to their cards, already about 90 % of the way in the first frame
  and settling over about 170 ms more. Fitted to the recorded frames (`SnapAssistPlan.FlyIn`, tested): cubic-bezier
  (0.1, 0.9, 0.2, 1) over 260 ms, the window showing 84 ms in. NeoShell's asks to show 350 ms after the snap, counted in
  frames (its window takes about 80 ms more to show: the foreground and the region), and moves each DWM preview from
  its window's place (what's drawn of it) to its card frame by frame; a minimized window's preview is in place from the
  start. Measured: it shows 415-450 ms after the window starts moving, first frames at the same share of the way.
  Picking a card then moves on without the fly-in (not compared).
- A WinUI window that's new, shown again or resized is black for about 100-150 ms, and WinUI doesn't draw a window
  that's hidden, off the screen or cloaked: Snap Assist is one window over the monitor's work area, kept shown and cut
  to the zones by its region (nothing between showings), which draws the cards before its region shows them.

### Snap groups (`SnapGroups`, unit tested)

- Windows snapped together into a layout (by Assist, a suggestion, or snapping into the space the others leave) are a
  group; a window leaves when it's dragged out of its zone, resized by hand, snapped elsewhere or closed, and a group
  of one is no group. Minimizing keeps it.
- With "Show my snapped windows ..." on, the taskbar's previews of an app with a grouped window start with the group:
  its windows' icons and "Group | <most recent window> and 1 other window", over the work area in small (the wallpaper
  window's DWM preview, cut to the work area, with each window's preview where it is, inset 2% across and 9% down so
  the wallpaper shows round them, as Explorer's). Alt+Tab shows the group just before the first of its windows (no
  icon), and starts on the window after the one in front, as Explorer's (`AltTabLayout.WithGroups`, tested). Clicking
  either restores the windows and brings them to the front, the most recently used last.
