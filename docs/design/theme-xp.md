# Theme: Windows XP

Windows XP's default blue Luna taskbar and Start menu as a NeoShell theme ("Windows XP" in Settings > Taskbar >
Theme): measured in a real XP and drawn in code from those numbers. Part of the [NeoShell design](../design.md); how
themes work is in [Themes](themes.md).

## How it was measured

- XP SP2 ran in the owner's v86 emulator page in Edge (1280x720, 32-bit colour, 96 DPI, "Windows XP style", "Default
  (blue)", font smoothing "Standard"; the same VM as the [Luna frames](window-frames-overlay.md#luna-layout)). The page
  shows XP 1:1, so screen grabs of its canvas are XP's own pixels. XP's pointer was found in each grab and moved in
  small relative steps; keys went in as US scan codes (the host's layout is Norwegian).
- Captured: the taskbar idle, each task button state (normal, hot, pressed, the active window's, active and hot), one to
  twelve Notepads (button widths, the ellipsis, grouping), a group button and its menu (normal, hovered item), its
  right-click menu, a button's system menu, the taskbar's menu and its Toolbars submenu, the Start button (normal, hot,
  pressed), Quick Launch turned on (icons, hot, pressed, its » chevron and menu), the notification area expanded and
  collapsed (the chevron, hot), tooltips ("Click here to begin", tray icons, the clock's date), Start (clean, each
  column's items hovered, All Programs and a cascade, a hovered cascade item, Log Off and Turn Off Computer with their
  infotips), the Log Off and Turn off computer dialogs, and 60 fps recordings of Start and All Programs opening.
- Scripts fitted the art (row colours, end patches, a mesh for the Start button, alpha for the flag) and the fonts
  (GDI text rendered on Windows 11 compared with XP's pixels), and wrote `XpArt.Taskbar.cs` and `XpArt.StartMenu.cs`.
  They and the captures stayed outside the repo (`E:\NeoShellWork\xptheme` on the VM, removed when done).
- Not caught: a flashing task button (no app in the XP image flashes on demand; the theme uses the hot button), and
  XP at other DPIs.

## Taskbar

Pixels at 96 DPI from the taskbar's top left (it's 30 high, `TaskbarDock` with height 30).

| Part | XP |
|---|---|
| Background | One colour per row, `3168D5 3888E9 4993E6 307BE5 … 1E50C4 1941A5` (the top three rows are the highlight) |
| Start button | x 0-99 over the whole height, its own art: a rounded green pill, `start` in Franklin Gothic Medium italic 19 px at (35, 3), white over a soft shadow (the text's shape blurred 1-2-3-2-1 each way, 1 px down and right, `304010` at 0.9), the Windows flag (21x22 at (10, 4)). Hot is lighter, pressed (and while Start is open) darker; the flag and text don't move |
| Quick Launch | Off by default in XP; turned on and locked, three 23-px buttons from x 106, 16-px icons 3 in and 5 down, then a » chevron slot of 23 (its frame 17 wide at 4 in, the arrows `7BB6E8` 8x5 at (8, 4)); the rest in the chevron's menu. Hot and pressed are rounded frames (`ToolbarHot`, `ToolbarPressed`) |
| Task buttons | From x 106 (after Quick Launch's width), 25 high at y 3, 160 wide (SM_CXMINIMIZED); when they don't fit, the band shared out: 7 in 979 px were one 139 and six 140, 8 were five 122 and three 123 (the wider last). Icon 16x16 at (10, 5), label Tahoma 8 pt white at (30, 5), cut off with "…" 13 px before the right end. Pressed (the active window's, or held) moves icon and label one pixel down and right |
| Button states | Normal, hot, pressed (= the active window's), pressed and hot: each a band of 25 rows, 25 measured columns at the left (a highlight that fades out) and 5 at the right |
| Grouping | Each window its own button, beside its program's other windows (a new Notepad's button goes after the first Notepad's, not at the end). When the taskbar fills, the program with the most windows becomes one button, then the next: cmd and 8 Notepads (108 px each) grouped, cmd and 7 (122 each) didn't, so the threshold is in between (120 used). A group shows its count in bold `FFCC6D` at (29, 5), the program's name 4 px after it, and a 5-3-1 white arrow 9 px from the right |
| Group menu | Above the button, at its left: the taskbar blue `2157D5` with two orange rows on top (`E68B2C`, `FFC73C`), edges `1E4FC3 3A97F3 2560DE` left and `2053CA 1D4AB5` right/bottom; items 22 high, 26 apart, icon at (11, 4), title at (35, 4) without " - Notepad"; hovered `4F96FF` from x 3 to the right edge. 169 wide for a 160 button |
| Group's right-click | Cascade, Tile Horizontally, Tile Vertically, Minimize Group, a line, Close Group |
| Notification area | A lighter panel from its left edge: 11 px to the first icon, 16-px icons 18 apart from y 7, 10 to the clock's text, the text (Tahoma 8 pt white, time without seconds) at y 8 and 16 from the right. Four icons and "7:29 AM" made it 147 wide. Left edge `0A2E52` then two light columns; the panel shades darker towards the screen's right edge |
| Chevron | A blue ball 19 px across centred on the panel's left edge, `<` while icons are hidden, `>` while shown; hot is lighter. A click shows the hidden icons in place, to the left of the others; they fold back a few seconds after the pointer leaves |
| Tooltips | Black 1-px border, `FFFFE1`, Tahoma black at (3, 2) inside, lines 13 apart; a soft shadow right and below. Over the taskbar their bottom is at its top, at the pointer's x. "Click here to begin" on Start, the window's title on a cut-off button, the long date on the clock, the app's tip on a tray icon |

## Menus

| | Classic (taskbar menu, system menu, group's commands) | All Programs (`Programs`) |
|---|---|---|
| Frame | 1-px `ACA899` border on white, 2 px above and below the items, a 5-px soft shadow | White, `55A1FF` top and bottom, `347EE2` right, each column starting with a 6-px blue band `3E87E4 84B2ED 2C7AE0 2D7CE2 307FE5 3382E8`, 5-px shadow |
| Items | 17 high (21 with a glyph or icon), text Tahoma at x 20 (25 with glyphs) 1 down (3 in a 21-px item); bold for the default | 23 high, 16-px icon at (12, 4), text at (36, 5), the highlight from the band to the column's right edge, a row higher than the item |
| Hover | `316AC5` from 3 in to 6 before the border, white text | `1665CB`, white text |
| Separator | 9 high, `ACA899` line 3 down from x 4 to 5 before the right | 8 high, a line fading from white to `D3D3C8` over 70 px and back, 3 down, with a white line under it |
| Width | Text + 44 (with a shortcut column: text + 20 + shortcut + 21, text at 25 + 1) | Columns as wide as the widest item (36 + text + 22), as tall as the screen; more go in another column |
| Glyphs | Marlett 13 px 4 in, 4 down: Restore `2`, Minimize `0`, Maximize `1`, Close `r`; check `a`; disabled text and glyphs `ACA899`. Submenu arrow 4x7, 12 from the right | Folder arrow 4x7 at 9 from the right |
| Submenus | Open after 400 ms of hover (XP's `MenuShowDelay`; All Programs opened 500 ms after its highlight in the recording) or at once on a click; a classic one overlaps its menu by 5 and goes 3 above the item, Start's open beside the column level with the item |

The system menu is the window's own (`GetSystemMenu`), read item by item and shown in XP's menu; Restore, Move, Size,
Minimize and Maximize are enabled as XP's taskbar set them (Restore when minimized or maximized, Move unless either,
Size for a sizable window that's neither, Minimize/Maximize when the window has the box and isn't so), the default is
Maximize, or Restore when minimized or maximized. The chosen command goes to the window as `WM_SYSCOMMAND`, after
bringing it forward (except for Minimize and Close).

## Start menu

380 wide, from the left end of the taskbar up; 478 high with two pinned and six most used programs. It appears at
once (no slide or fade); its shadow appears three frames later.

| Part | XP |
|---|---|
| Header | 65 rows: a blue band (`XpArt.StartHeader`) with top corners cut round (5, 3, 2, 1, 1 pixels each side); the user's picture 48x48 at (9, 9) in a light rounded frame with a shadow; the name in Franklin Gothic Medium 19 px white at (67, 20) over a soft black shadow at 0.5 |
| Orange line | 2 rows, fading in from white at the left and out to grey-blue at the right (`XpArt.StartOrangeLine`) |
| Left column | White from x 2 (border `1854C2`, `91A4DA`). Items 36 high, 40 apart from y 73: icon 32 at (9, 2), name Tahoma `373738` at x 46, pinned ones bold with a grey `808080` second line ("Internet Explorer"), one-line names 11 down, two-line names 5 down. A fading separator (x 20-178) after the pinned, another above All Programs. Hover `316AC5` from x 8, 176 wide |
| All Programs | Bold at (46, top + 5), its green arrow (15x22) at x 129; the hover 137 wide; 7 below the line, 5 above the bottom bar |
| Right column | `D3E5FA` from x 191 (a `95BDEE` line between), right edge `D2E4FA CFE2F8 C5DAF4 B8D0EE A6C2E6 2B6DD1 1854C2`. Items 26 high, 30 apart from y 73: icon 24 at (198, 1), text `0A246A` at (226, 6), the first group bold; groups parted by a blue fading line (`81B6FF`). Hover `316AC5` from x 196, 176 wide. Infotips below the pointer |
| Bottom bar | 41 rows (`XpArt.StartBottom`): Log Off's key and Turn Off Computer's power button (24x24 at x 176 and 251, 8 down), their names in white at x 204 and 279, 13 down |

## Log Off and Turn off computer

314x200 in the middle of the screen: `003399` with the title (Trebuchet-like, white) and the flag at the top right, a
middle band lightest in the middle (`96B5F5` to `5A7EDE`), the choices as 33-px rounded buttons with bold white names
under them (Switch User and Log Off at x 84 and 199; Stand By, Turn Off and Restart at 56, 141 and 226, at y 81), and
Cancel at (244, 169). XP greyed the screen behind it, fading over a few seconds.

## As built (`Themes/WindowsXp/`)

- **Drawing.** Each surface is one picture drawn in code into an `XpCanvas` (premultiplied BGRA for a
  `WriteableBitmap`) from `XpArt`'s measured numbers: rows, `XpBand`s (left columns, middle colour per row, right
  columns), the Start button's mesh, patches for small pieces (flag, chevrons, All Programs' arrow, Log Off and Turn
  Off's icons, the picture frame). Text is GDI's (`GdiText`, grayscale antialiasing as XP's "Standard" smoothing);
  shadows under text are the text's coverage blurred. Icons are `Image`s over the picture; each clickable thing has an
  invisible `Button` over it (`XpHit`) for the pointer, the keyboard and UI Automation.
- **Taskbar** (`XpTaskbarWindow`, `XpTaskbarLayout`, `XpTaskbarPainter`): `TaskbarDock` (height 30), `TaskButtonList`
  for the buttons and their clicks, `XpTaskbarLayout.Group` for XP's grouping (Settings' "Combine taskbar buttons":
  Never turns grouping off, otherwise XP's when-full), Quick Launch from `PinnedTaskbarApps`, the tray from
  `NotificationArea` with `TrayIconInput`, and Windows' volume and network state as two tray icons with XP's
  pictures, still in Windows (`SndVolSSO.dll` and `netshell.dll`): the speaker opens SndVol's small slider, a
  double-click the mixer; the network icon opens Network Connections. The clock's double-click opens Date and Time.
  Task buttons show the window's small icon. Start opens on the Start button's click (XP's opened on the press), the
  Windows key and Ctrl+Esc; a press elsewhere on the taskbar closes it.
- **Menus and tooltips** (`XpMenu`, `XpToolTip`, `XpPopup`): no-activate topmost popups, see-through for the shadows;
  a `ClickCatcher` below a menu closes it on a click elsewhere; arrows, Enter and Esc reach the menu from the window
  with the keyboard. The taskbar's menu has XP's items but not Toolbars (Quick Launch is the pinned apps); Properties
  and Customize Notifications open NeoShell's taskbar settings; alongside Explorer it ends with Exit NeoShell.
- **Start** (`XpStartMenuWindow`, `XpStartMenuPainter`): the left column is Start's pinned apps (top-level app pins,
  up to four, one line each) and the six most used other apps by Windows' launch history (`UserAssist`, as Windows
  11's "Most used"); All Programs is the Start Menu folders (`StartMenu/StartMenuFolders`: the user's and all users'
  merged, folders first, by name), cascading. The right column opens the real places: My Documents, My Pictures, My
  Music, My Computer, Control Panel, Printers and Faxes (shell folders), Help and Support (`ms-contact-support:`),
  Search (`search-ms:`), Run (Windows' Run dialog); My Recent Documents and Set Program Access and Defaults are left
  out. Log Off and Turn Off Computer open `XpPowerDialog`, whose choices run Windows 11's (`PowerItems`).

## Compared with XP

Rendered by the same code as the live views (the live taskbar's Start button is pixel-identical to the rendering)
and compared with XP's captures pixel by pixel, icons left out:

| Case | Pixels | Identical | Within 8 | Mean |
|---|---|---|---|---|
| Taskbar with one button, normal / pressed (1280x30) | 37120 | 92.1% / 91.7% | 99.19% | 0.31 / 0.30 |
| Group button "10 Notepad" and a cmd button | 9388 | 86.6% | 99.84% | 0.56 |
| Eight narrow buttons with ellipses | 27388 | 88.8% | 100.00% | 0.35 |
| Tray collapsed / expanded with the chevron | 3344 / 4224 | 73.9% / 83.3% | 99.04% / 99.62% | 0.76 / 0.36 |
| Start menu (right column below Control Panel left out: a two-line item not modelled) | 119472 | 97.3% | 99.05% | 0.28 |

What differs:

- The Start button's `start`: within 8 levels on 76% of its box (mean 5): XP's Franklin Gothic Medium hints a little
  differently, and the soft shadow is a fit. Its background is a mesh, 1-2 levels off in the middle.
- Buttons' middles: XP stretched a small image, leaving ±1-4 levels of banding that the one-colour middle leaves out.
- A flashing button looks hot (XP's flashing wasn't caught). XP's dialogs are approximated (glyphs from Segoe Fluent
  Icons on drawn buttons; no greyed screen).
- Text and glyph shapes come from Windows 11's copies of Tahoma, Franklin Gothic and Marlett with GDI's grayscale
  antialiasing; those measured matched XP's to the pixel (labels, clock, menus, tooltips, Start's items).
- Art is 96 DPI; at other scales it's stretched.

Live, alongside Explorer and as the shell: the taskbar, Quick Launch, task buttons (hot, active, grouped with their
menu), a test window's system menu, the taskbar's menu, the tray's chevron expanding the hidden icons, tooltips, Start
opening from the button (pointer and UI Automation), the Windows key and Ctrl+Esc, items hovered, All Programs
cascading, Run, the Turn off computer dialog (cancelled), and switching themes live in Settings both ways. Not run
live: Log Off and the power choices themselves, a second monitor, DPI other than 96, auto-hide with the XP taskbar.
