# Theme: 2088 (concept)

A concept rather than a measured Windows: how the desktop might look and work in the year 2088, built as a NeoShell
theme ("2088 (concept)" in Settings > Taskbar > Theme) so it runs on today's Windows with today's windows, tray and
apps. Cyberpunk in look: near-black surfaces, white text, one signal red, Japanese next to the English, and edge
markers so every panel reads as a physical part. Part of the [NeoShell design](../design.md); how themes plug in is
in [Themes](themes.md).

## The premise

By 2088 the desktop is an instrument rather than a filing cabinet. Four ideas shape every surface:

1. **Ask, don't navigate.** You say or type what you want and the system resolves it, an app or an action. Start is
   an ask line first, with the apps as the things it can resolve to ("lock", "reboot", "settings" are as much
   answers as "Calculator").
2. **Apps are modules, windows are threads.** Pinned apps are numbered modules that slot into a grid; each running
   window is a numbered chip on the bar, the number being the key that brings it up (Win+1…9).
3. **The machine reports its state.** Readouts instead of icons: the network, volume and power are words and
   numbers (`ETH·OK`, `75%`, `NO LINK` in red), processor and memory are segmented meters, uptime ticks. The
   desktop is a HUD with the time and the machine's vitals on it.
4. **Hardware you can feel.** Panels have cut corners, L brackets on the others, a ruler's ticks along their edges, a
   serial plate and barcode, scan lines over the screen. They look machined, not drawn.

Japan is where the story's machine stands (新東京 第七区, New Tokyo, District 7): every heading carries its Japanese,
and the date is printed with the weekday's kanji.

## The visual language

| Role | Value |
|---|---|
| Void (the bar, the deepest backgrounds) | `07070A` |
| Panel (Start, chips, sensors) | `0C0C11` |
| Raised (the active chip, tiles, the ask field) | `16161D` |
| Lines: normal / bright | `2A2A33` / `4A4A56` |
| Text: normal / dim / faint | `F2F2F4` / `8E8E9A` / `55555F` |
| Signal red / hot (hover) / deep (attention, hovered tile) | `FF2A3D` / `FF5A69` / `3A0A12` |

- **Type**: Bahnschrift SemiCondensed (Windows' own DIN-like face) for labels, in capitals and spaced out;
  Cascadia Mono (or Consolas) for readouts that change; Yu Gothic UI for the Japanese.
- **The frame** (`HudFrame`): a plate with its top-left and bottom-right corners cut at 45°, a 1-px outline, and red
  markers: L brackets (2 px thick) on the two square corners and a square tick in each cut. Active or hovered parts
  show the markers; idle ones don't.
- **Rulers**: a tick every 8 px along a top edge, a long one every 64 px (the bar's top, Start's divider).
- **Meters** (`SegmentBar`): separate cells lit from the left in red; the last 15% light white, a warning in a red
  interface.
- **Bilingual headings**: `MODULES // モジュール`, the English bold and spaced, the Japanese in red.

| Japanese | Reading | Where |
|---|---|---|
| 起動 | kidō, start up | The Nexus block, Start's title (起動ノード, launch node) |
| 意図 | ito, intent | The ask field |
| 網 / 音 / 電力 | net / sound / power | The sensor readouts |
| 遠隔測定 | telemetry | Start's vitals |
| 最近 | recent | Start's most used apps |
| 解決 | resolved | Start's results |
| 命令 | command | A result that is an action |
| 施錠 / 退出 / 休止 / 再起動 / 停止 | lock / sign out / sleep / restart / shut down | The power buttons |
| 操作者 / 端末 / 稼働 | operator / terminal / running | The user, the PC's name, the uptime |
| 正常稼働 | running normally | The desktop's status line |
| 新東京 第七区 | New Tokyo, District 7 | The desktop, the serial plate |

## Desktop: the Field

`Themes/Concept2088/FieldDesktop`, laid over the wallpaper and under the desktop icons on every monitor
(`WallpaperWindow.ShowField`, so shell mode only: alongside Explorer the desktop is Explorer's). From the bottom up:

1. The user's wallpaper, kept, under an 84% shade of Void: it shows through faintly (a red wallpaper glows).
2. A red glow rising from the bottom left.
3. The floor: a perspective grid in red, 29 lines running to a vanishing point at the middle of a horizon at 64% of the
   height above the bar, crossed by 12 lines that close up towards the horizon.
4. "2088" in 420-px type at 3.5% opacity, standing on the horizon.
5. Scan lines (a dark line every 3 px) and the sweep: a red band with a bright lower edge passing down the screen
   every 9 seconds (a composition animation, no work per frame).
6. The sky's crosses on a 120-px grid, registration marks (34-px L brackets) in the four corners above the bar, and
   corner captions (`FIELD 1764×988`, `REC ● 新東京`, `NS-2088 // ΔK7`).
7. At the top middle (the icons keep to the left, the widget sidebar to the right): `ネオシェル NEOSHELL OS · FIELD`,
   the time in 128-px light type with the seconds in red, the date (`2088.10.13 · 火曜日`), the place, and a table of
   the operator, the PC's name, the uptime and processor and memory meters. Other monitors show their number and
   size only.
8. `● SYSTEM NOMINAL 正常稼働` above the bar.

Nothing in the Field takes the pointer: clicks, drags and right-clicks reach the desktop icons and the desktop as
before. Switching the theme lays the Field on or takes it away live (`Wallpaper` follows the settings).

## Taskbar: the Spine

`Themes/Concept2088/SpineTaskbarWindow`, 48 px high, Void, with a hairline, the ruler and a red mark over the Nexus
block along its top. Left to right:

| Part | What it is and does |
|---|---|
| Nexus block (`StartButton`) | Red, cut corners, 起 large and `NEXUS / 起動 · START`. Opens Start; while it's open the block turns to a red outline (Start has taken its colour) |
| Ask field (`SearchBox`) | `› ASK NEXUS` with a blinking red caret and `意図 WIN+S`. Opens Start with its ask line focused |
| Chips (`TaskButton`) | One per window (never combined: a window is a thread) after the pinned apps without windows (icon only, 44 wide). A window's chip: its Win+N number, icon and title in capitals, 188 wide, narrowing together down to the number and icon when the row is full. In front: raised, red outline, red markers, a red bar along the bottom. Running: grey bar. Wanting attention: deep red plate. Progress: a white bar growing along the bottom (red on error, grey paused). Right-click: launch another, pin or unpin, close |
| Sensors | The tray icons (the hidden ones folded into a red `+N` that unfolds them in place), then readouts with their captions: `NET 網` (`ETH·OK`, Wi-Fi bars `▮▮▮▯▯`, `LOCAL` or `NO LINK` in red; opens the network settings), `VOL 音` (the volume in %, `MUTE` in red; a click mutes, the wheel sets it 2% a notch), `PWR 電力` (with a battery: the charge, `+` charging, red at 15% and below) |
| Clock (`ClockButton`) | `20:22` with the seconds in red, `2088.10.13 火` under it; the real long date in its tooltip. Opens the notification center and calendar (Windows 11's), a red line under it while they're open |
| Show desktop | A 12-px strip with a mark that turns red on hover |

What it shares with every theme: placement, auto-hide and full screen (`TaskbarDock`), the buttons kept in step and
Win+1…9 (`TaskButtonList`), the tray's input (`TrayIconInput`), the indicators. It has no thumbnails, jump lists,
Quick Settings or input switcher: those calls do nothing.

## Start: Nexus

`Themes/Concept2088/NexusStartMenu`, an 880×640 panel 8 px above the Spine at its left, rising 14 px into place as it
fades in (180 ms). Opaque: a see-through panel let the windows behind show through its text. From the top:

- **Header**: `NEXUS 起動ノード`, `LAUNCH NODE · WHAT YOU NEED, NOT WHERE IT IS`; at the right the operator's name and
  picture in a red-cut frame (opens the account settings).
- **Divider**: the ruler, `SYS://NEOSHELL/NEXUS BUILD 88.10 KERNEL NOMINAL`, `接続済 LINKED ●`.
- **The ask line** (`StartSearchBox`): `› WHAT DO YOU NEED? 何が必要ですか`, focused on opening. Typing replaces the
  modules with what it resolves to, numbered: the apps by Start's own ranking (`AppSearch`), and commands
  (`NexusIntents`). Up and Down choose, Enter runs, Esc closes.
- **Modules** (`PinnedApp`): Start's pinned apps, up to 15 in a 5×3 grid of cut tiles with their number, icon and name;
  hovered, a tile goes deep red with markers.
- **All modules** (`AllAppsButton`): every app in the catalog, A to Z, in the same numbered rows.
- **Telemetry**: CPU and memory meters (`Hud.Resources`, sampled only while Nexus or the Field is showing), uptime,
  the link. **Recent**: the five most used apps by Windows' launch history (`UserAssist`, as XP's Start). A barcode
  and the serial `NS-2088-ΔK7 · 新東京 第七区 製`.
- **Footer**: `CONFIG 設定` (NeoShell's settings, `NeoShellSettingsButton`), and the power row: `LOCK 施錠`,
  `SIGN OUT 退出`, `SLEEP 休止`, `REBOOT 再起動` and `SHUT DOWN 停止` in solid red.

### The ask line's commands

| Words (English, Japanese) | Does |
|---|---|
| lock, 施錠 | Locks the PC |
| sleep, suspend, 休止 | Sleeps |
| restart, reboot, 再起動 | Restarts |
| shut down, shutdown, power off, 停止 | Shuts down |
| sign out, log off, logout, 退出 | Signs out |
| settings, config, 設定 | NeoShell's settings |
| run, execute, 実行 | The Run dialog |
| desktop, field | Shows the desktop |

A command is offered from two letters of one of its words. It comes **before** the apps only when a word is typed
whole; otherwise after them. Found live: with commands always first, "re" put REBOOT SYSTEM at the top, and Enter on a
half-typed "Registry Editor" would have restarted the PC.

## Widgets: the Side rail

The widget sidebar and floating widgets in the concept (`Themes/Concept2088/HudWidgets`, applied by `Sidebar` live
when the theme changes: `SidebarWindow.SetConcept`, `WidgetFrame.SetConcept`, `WidgetView.SetConcept`):

- **Windows**: dark and see-through (the backdrop is Transparent and the theme dark whatever the taskbar's backdrop,
  Windows' mode and accent colour are), so the Field shows through.
- **The strip**: a Void plate at 72%, a red rule down its left edge with a ruler's ticks (every 8 px, long and red
  every 64), `SIDE RAIL 側面` at the top beside the add button.
- **Cards**: each a cut-corner `HudFrame` (14-px cuts, red markers) with a heading: a red tick, the widget's name in
  capitals and its Japanese (`PROFILE // 操作者`, `RESOURCE USAGE // 資源監視`, `PICTURES // 映像`, `NOW PLAYING // 再生`,
  `WEATHER // 気象`, `NOTES // 記録`, `WIRELESS DEVICES // 無線機器`, `ABOUT WINDOWS // 系統情報`). The outline turns red
  while the pointer is anywhere over the card. A floating widget's window is its card, cut corners and all.
- **Inside**: the widgets keep their layout; their ThemeResources resolve to the concept's palette over the card
  (`HudWidgets.Palette`: text, fills, the accent as signal red, Bahnschrift), and the resource graphs' default colours
  become red, hot red, white and grey (colours chosen in the widget's settings stay).

Text set in a fixed text style (captions, the profile's date) keeps Segoe UI: those styles are resolved when the
widget is made. Checked live as the shell: the sidebar over the Field, hover, a floating Notes widget, and switching
to Windows 11 and back with the widgets open. Not run live: dragging a widget between the sidebar and the desktop in
the concept, the sidebar's panel hidden (only the cards).

## What stays Windows 11's

The notification center and calendar, toasts, Quick Settings, menus' look (they follow the dark theme), Alt+Tab,
Snap, the widgets' settings flyouts, desktop icons' selection colours and NeoShell's Settings window. Other apps' window frames
are styled separately ([Window frames](window-frames.md)); a 2088 frame style would be the next step there.

## Beyond the prototype

What the 2088 premise asks for that this build only hints at, in the order it would be worth building:

- **Voice and the ask line as one**: hold the Copilot key and speak; the words land in the ask line as you say them.
- **Intents that take arguments**: "open the report from yesterday", "mute for an hour", "call Ana": the ask line
  resolving to documents (the index, `IndexSearch`, is already there), timers and contacts, each result saying which
  module would handle it.
- **Threads, not windows**: chips grouped by task rather than app (a browser tab, a document and a chat as one
  thread), restored together.
- **The Field as a live surface**: the desktop's grid showing the windows' places as a map, notifications arriving
  as lines on the horizon, the sweep slowing when the machine is busy.
- **2088 window frames** in the frames program: cut caption corners, the title in capitals, red markers on the active
  window.
- **A 2088 Quick Settings and notification center**, to finish the set.

## Checked live

On the VM at 96 DPI, 1764×988, with the owner's red wallpaper: alongside Explorer (the Spine above Explorer's taskbar:
Start, the ask line, a search for "re" and for "lock", All modules) and as the shell (the Field with icons and the
widget sidebar, the Spine with the tray, Nexus). Not run live: a second monitor, other DPIs, auto-hide, a battery,
progress and attention on a chip, the power buttons (they would end the session).
