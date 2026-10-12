# Theme: Windows 98

Windows 98's taskbar and Start menu as a NeoShell theme ("Windows 98" in Settings > Taskbar > Theme), measured in a
real 98 and drawn in code from those numbers, as the [XP theme](theme-xp.md) is. Part of the
[NeoShell design](../design.md); how themes work is in [Themes](themes.md). This file is written so the rest can be
built without the VM: every number below was read off 98's own pixels.

## How it was measured

- Windows 98 (second edition, Internet Explorer 5 desktop) ran in the owner's v86 emulator page in Edge
  (`oses.ioblako.com`, "Windows 98", Run Preloaded) at 1024x768, True Color (32 bit), 96 DPI, the default scheme
  "Windows Standard" with the teal desktop. The page shows the guest 1:1 (its canvas has rounded corners that hide a
  few pixels at the screen's bottom left). The XP session the owner had open stayed untouched in its own tab; 98 ran
  in a separate Edge window. The launcher offers Windows 1.01 to XP, 2000 and 2003, NT 4 and CE, but no Vista or 7.
- 98's pointer was found in each grab (a template for the arrow's tip) and moved with small relative `mouse_event`s:
  moves of up to 2 counts every 130 ms arrive 1:1, larger or faster ones are accelerated about 2x by the guest.
- Captured: the idle taskbar; the Start button normal, pressed (held and while Start is open) and its tooltip; Quick
  Launch normal and hot with its tooltip; task buttons normal and the active window's (My Computer and Notepad);
  the notification area and the clock's tooltip; Start, an item selected, Programs and Accessories cascading, an item
  selected in a submenu; Start's slide (screen grabs every ~20 ms); the desktop's menu; window frames active and
  inactive (sizable My Computer and Notepad, the Display Properties dialog with its tabs, group boxes and buttons); the
  Shut Down Windows dialog over the dimmed screen.
- Fonts: everything is MS Sans Serif 8 pt (the bitmap font `sserife.fon`, 11 px), bold for "Start", the active task
  button, captions. Windows 11 still has the same font: GDI's rendering of it on Windows 11 matched 98's glyphs pixel
  for pixel ("Start", "My Computer", the clock, Start's items).
- Not caught: a flashing task button, task buttons when the taskbar is full (98 shrinks them, then scrolls them with
  arrows), the taskbar's and a task button's right-click menus, auto-hide, other DPIs or schemes.

## Colours (Windows Standard)

| Role | Colour |
|---|---|
| Face (3D objects, menus, taskbar) | `C0C0C0` |
| 3D light / highlight / shadow / dark shadow | `DFDFDF` / `FFFFFF` / `808080` / `000000` |
| Selected menu item / its text | `000080` / `FFFFFF` |
| Tooltip | `FFFFE1`, black text and 1-px black border |
| Active caption | gradient `000080` → `1084D0`, white bold text |
| Inactive caption | gradient `808080` → `B5B5B5` (measured; the scheme is usually quoted as ending at `C0C0C0`), `C0C0C0` text |
| Desktop | `008080` |

### Edges

Drawn outside in; the bottom and right lines are drawn last and take the corners (the top right and bottom left
pixels belong to them).

| Edge | Outer top/left | Inner top/left | Inner bottom/right | Outer bottom/right |
|---|---|---|---|---|
| Raised button (Start, a task button, push buttons) | `FFFFFF` | `DFDFDF` | `808080` | `000000` |
| Pushed button | `000000` | `808080` | `DFDFDF` | `FFFFFF` |
| Menu, Start, window frame | `DFDFDF` | `FFFFFF` | `808080` | `000000` |
| Thin raised (flat toolbar button hot, gripper) | `FFFFFF` | | | `808080` |
| Thin sunken (toolbar button pressed, notification area) | `808080` | | | `FFFFFF` |
| Etched line (between bands) | a column `808080`, then a column `FFFFFF` | | | |

A default push button (OK) has a 1-px black frame around the raised edge. A dotted focus rectangle is black on every
other pixel (where x + y from its corner is odd).

## Taskbar

28 high at the bottom of the screen; pixels at 96 DPI from its top left (on the 1024x768 screen its top is y 740).

| Part | 98 |
|---|---|
| Background | Row 0 `DFDFDF`, row 1 `FFFFFF`, the rest `C0C0C0` (its bottom edge is off the screen) |
| Start button | x 2, y 4, 54x22, raised. The Windows flag (16x16, `Win98Art.Flag`) at (4, 3) in the button, "Start" bold black, its text box at (22, 4). Pressed (held, and while Start is open): pushed edge, a dotted focus rectangle 3 in (48x16), flag and text one pixel down and right. No hot look. Its tooltip is "Click here to begin" |
| Bands | Each toolbar band starts with an etched line (2 wide, rows 4-25), a gripper 4 further (3x18 thin raised bar, rows 6-23), its contents 11 after the line. Quick Launch's band at x 58 (two after the Start button), the task buttons' band 4 after Quick Launch's last button |
| Quick Launch | 23x22 flat buttons from x 69 (four as installed: Launch Internet Explorer Browser, Launch Outlook Express, Show Desktop, View Channels), 16-px icons at (3, 3). Hot: thin raised edge; pressed: thin sunken, icon one pixel down and right. Tooltips name them |
| Task buttons | From x 176 (with four Quick Launch buttons), y 4, 22 high, at most 160 wide, 3 apart, in the order the windows appeared (98 doesn't group). Raised; the active window's pushed: a white row under the dark edge, then the face checkered white/`C0C0C0` (white where x + y is odd, from the taskbar's origin). Icon (the window's small icon) at (4, 3), (5, 4) pushed; label black at (22, 4), bold for the active window, (22, 5) pushed (only down: the bold text isn't moved right), cut off with "..." |
| Notification area | A thin sunken panel, y 4, 22 high, ending 2 px from the screen's right edge; an etched line 4 to its left. 16-px icons from 3 in, side by side (inferred from the speaker's leftmost pixel; check with a third icon), at row 3 of the panel. The clock (`h:mm AM`, no seconds) black, its text box 8 from the panel's right end, at row 5 of the panel; with two icons and "10:49 AM" (46 px wide in GDI) the panel was 98 wide (3 + 32 + 9 + 46 + 8). The clock's tooltip is the long date ("Monday, October 12, 2026") |
| Tooltips | 1-px black border, `FFFFE1`, the text 4 in and 2 down, 17 high for one line, no shadow. Over the taskbar its bottom is a pixel above the pointer, its left at the pointer's x, pushed in at the screen's right edge |

## Start menu

179x342 with 98's items for user "IOblako", its left at x 2 of the screen and its bottom the row above the Start
button (it covers the taskbar's two top rows). Opens on the button's **press**, sliding up: the top of the menu
appears first from behind the taskbar and the menu rises until whole (about 250 ms in the emulator, fast at the end:
55 rows after 117 ms, 212 after 205 ms, all 342 after 253 ms; 98's real timing may differ, the emulator is slow).

| Part | 98 |
|---|---|
| Frame | Menu edge (2 px), then a 1-px `C0C0C0` gap |
| Banner | x 3, y 3, 21 wide, down to 3 from the bottom: navy `000080`; its bottom 105 rows are 98's bitmap (`Win98Art.BannerArt`): "Windows" bold and "98" light, white reading upwards, over navy dithered more and more with blue `0000FF` towards the bottom, grey antialiasing |
| Items | 32 high from y 3, from x 24 to 7 short of the right edge. 32-px icon at (26, top); text at x 67, vertically centred (its 13-row box 10 down), black, its mnemonic underlined (always shown, as 98 did): Windows Update, *P*rograms, F*a*vorites, *D*ocuments, *S*ettings, *F*ind, *H*elp, *R*un..., *L*og Off *name*..., Sh*u*t Down... |
| Separators | 8 high: a `808080` line 3 down and a white line under it, from x 26 to 6 before the right edge. After Windows Update and after Run... |
| Selected | `000080` over the item's width, white text; the icon unchanged (its transparent parts show the navy) |
| Submenu arrow | A black triangle 4x7 (white when selected), 12 in from the right edge, 12 down in a 32-px item |
| Width | 67 + the widest text + 28 (the text measured without its ampersand) |
| Submenus (Programs, Favorites, Documents, Settings, Find) | Open beside the item: the submenu's left edge 5 px inside the parent's (overlapping its frame and gap), its top 3 above the item. Same frame, items 20 high from (3, 3): 16-px icon at (5, top + 2) of the menu, text at x 30, vertically centred; selected navy from x 3 to 6 before the right edge; arrow 11 in from the right edge, 6 down; folders first (Accessories, Online Services, StartUp), then shortcuts, by name. Programs was 139 wide. A submenu opens after a short hover, or at once on a click |

The desktop's menu (and other classic menus) use the same frame with 17-px items and 9-px separators, text 24 in.

## Window frame (for the frames program)

| Part | 98 |
|---|---|
| Borders | Sizable: 4 px, the menu edge (`DFDFDF`, `FFFFFF` / `808080`, `000000`) and 2 px of `C0C0C0`; dialogs: 3 px (the edge and 1 px of face) |
| Caption | 18 high, inside the borders; a horizontal gradient from the caption's left (after the icon's 19 px, which take the first colour) to the first caption button, the last colour behind the buttons |
| Icon and title | The small icon at (2, 1) in the caption; the title bold, its text box 21 in with an icon, 2 without, 2 down |
| Caption buttons | 16x14 raised buttons 2 down from the caption's top; minimize and maximize side by side, 2 px, close, then 2 px to the caption's right end; a dialog has help and close 2 apart. Pressed: pushed edge, glyph a pixel down and right |

The glyphs (black on the face, `#`), in the 16x14 button with its edge:

```
minimize          maximize          close             help
ooooooooooooooo#  ooooooooooooooo#  ooooooooooooooo#  ooooooooooooooo#
o-------------+#  o-------------+#  o-------------+#  o-------------+#
o-............+#  o-.#########..+#  o-............+#  o-....####....+#
o-............+#  o-.#########..+#  o-..##....##..+#  o-...##..##...+#
o-............+#  o-.#.......#..+#  o-...##..##...+#  o-...##..##...+#
o-............+#  o-.#.......#..+#  o-....####....+#  o-......##....+#
o-............+#  o-.#.......#..+#  o-.....##.....+#  o-.....##.....+#
o-............+#  o-.#.......#..+#  o-....####....+#  o-.....##.....+#
o-............+#  o-.#.......#..+#  o-...##..##...+#  o-............+#
o-..######....+#  o-.#.......#..+#  o-..##....##..+#  o-.....##.....+#
o-..######....+#  o-.#########..+#  o-............+#  o-.....##.....+#
o-............+#  o-............+#  o-............+#  o-............+#
o++++++++++++++#  o++++++++++++++#  o++++++++++++++#  o++++++++++++++#
################  ################  ################  ################
```

(`o` white, `-` `DFDFDF`, `.` face, `+` `808080`, `#` black.) Restore (not captured) is Marlett's `2` in the same
place; disabled glyphs are `808080` with a white copy one pixel down and right.

**Not built yet.** The frames program (`src/NeoShell.Frames`) draws XP's Luna through overlay windows whose geometry
(`LunaLayout`), tracking (`LunaTracker`) and settings are Luna's; a 98 frame needs those made per style (98's caption
is 18 rows, not 30, the buttons are 16x14 and the borders square), which is more than a small step. With the numbers
above it can be drawn entirely in code: edges, the gradient, the title in bold MS Sans Serif, the glyphs.

## Dialogs

The Display Properties dialog (403x448): dialog frame and an active caption with help and close; push buttons 75x23
(OK the default, with its black frame), 7 apart, 8 from the bottom; tabs and group boxes in etched lines.

Shut Down Windows (323x173, centred): the computer icon, "What do you want the computer to do?", radio buttons Shut
down, Restart, Restart in MS-DOS mode, buttons OK, Cancel, Help. The screen behind is dimmed by drawing black over
every other pixel (a 50% checkerboard) until the dialog closes.

## As built (`Themes/Windows98/`), the groundwork

Drawn as the XP theme draws: one picture per surface into a `ThemeCanvas`, icons as `Image`s over it, an invisible
`Button` (`ThemeHit`) over each clickable thing.

- `Win98Art`: the scheme's colours, MS Sans Serif (regular and bold, 11 px), the flag and the banner's art as
  character maps. `Win98Painting`: the edges, etched lines, grippers, the checkered face and the focus rectangle.
- **Taskbar** (`Win98TaskbarWindow`, `Win98TaskbarLayout`, `Win98TaskbarPainter`): `TaskbarDock` at 28 rows, the
  Start button (normal, pushed while held and while Start is open; Start opens on the press as 98's did, after the
  press is through so the button's focus doesn't take the keyboard back from Start), Quick Launch from the taskbar's
  pinned apps (up to four, hot and pressed), one task button per window in the order they appeared (160 wide, then
  sharing the band), the active one pushed, checkered and bold, and on the primary monitor the notification area with
  every tray icon (`TrayIconInput`) and the clock (double-click: Date and Time).
- **Start** (`Win98StartMenuWindow`, `Win98StartMenuPainter`): the frame, banner, the ten items with shell32's icons
  and their underlined mnemonics, separators, selection under the pointer, arrows. Windows Update opens Windows
  Update's settings, Help opens Get Help, Run opens Windows' Run dialog, Log Off and Shut Down open NeoShell's shut
  down dialog (Windows 11's); Escape closes it.
- Shared with XP (moved out of `WindowsXp/` for it): `ThemeCanvas` (was `XpCanvas`; drawing a stretching band moved
  to `XpBand.Draw`), `ThemeHit`, `ThemeIcons`, `ThemePopup`, and the submenu arrow (`ThemeCanvas.SubmenuArrow`).
  `GdiText` and `ThemeCanvas.Text` can now underline mnemonics (`prefix`).

Compared with 98 by rendering the same scenes and diffing with the captures (icons left out): the taskbar with two
task buttons, four Quick Launch buttons and the clock is 99.98% identical (26484 pixels; the rest are icon edges);
the pushed Start button identical; Start (179x342) 99.79% identical, the rest where 98's pointer was. Live alongside
Explorer: the taskbar, the Start button (pressed, held, toggling Start by press), Start opening from the button and
UI Automation with an item selected under the pointer; the live Start button and banner matched 98's pixels.

### What remains

- Start: the submenus (Programs from the Start Menu folders as XP's All Programs does, Favorites, Documents, Settings
  with Control Panel, Printers, Taskbar & Start Menu, Find with Files or Folders and the Internet), the slide up (and
  the submenus' slide right), keyboard (arrows, mnemonics), 98's Log Off and Shut Down Windows dialogs (with the dimmed
  screen).
- Taskbar: tooltips (98's: no shadow, MS Sans Serif), the taskbar's menu and a task button's system menu in 98's grey
  menus (with Exit NeoShell alongside Explorer: until then use `/exit` or Settings), task buttons shrinking further
  and scrolling when full, a flashing button, Quick Launch beyond four, the volume indicator as 98's speaker, the
  window's small icon on task buttons (the tracker's large icon is shown scaled), the tray's tooltips.
- The 98 window frame in the frames program (above).
- Other DPIs (the art is 96 DPI, scaled), a second monitor, shell mode and auto-hide: not run live yet.
