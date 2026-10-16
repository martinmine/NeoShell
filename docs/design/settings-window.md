# Settings window

NeoShell's own settings window, the button in Start that opens it, and the menus whose settings moved into it. Part
of the [NeoShell design](../design.md). The settings record and its file are in [Settings](settings.md).

## What the user can change, and whose setting it is

Before this window NeoShell's settings were spread over menus. Each is now on a page; the menus keep only what the
Windows 11 surface they copy has.

| Setting | Was | Now | Whose |
|---|---|---|---|
| Taskbar alignment (`TaskbarAlignment`) | taskbar menu | Taskbar > Taskbar behaviors | NeoShell's (Explorer's is `TaskbarAl`; not shared) |
| Automatically hide (`AutoHide`) | taskbar menu | Taskbar > Taskbar behaviors | NeoShell's |
| Show on all displays (`ShowOnAllDisplays`) | taskbar menu | Taskbar > Taskbar behaviors | NeoShell's |
| Combine buttons (`CombineButtons`) | taskbar menu | Taskbar > Taskbar behaviors | NeoShell's |
| Theme (`Theme`) | — (new, [Themes](themes.md)) | Taskbar > NeoShell | NeoShell's own |
| Taskbar backdrop (`TaskbarBackdrop`) | taskbar menu | Taskbar > NeoShell | NeoShell's own (no Explorer counterpart) |
| Search (`SearchboxTaskbarMode`) | taskbar menu | Taskbar > Taskbar items | Windows' (Explorer follows the same value) |
| Widgets (`ShowWidgetSidebar`) | taskbar menu | Taskbar > Taskbar items, and Widgets | NeoShell's |
| Hidden icon menu (`NotifyIconSettings` chevron) | taskbar menu, as the shell | Taskbar > Other system tray icons, as the shell | Windows' (as Explorer keeps it) |
| Panel background (`ShowWidgetPanel`) | sidebar menu | Widgets | NeoShell's |
| Sidebar width (`WidgetSidebarWidth`) | dragging the sidebar's edge | Widgets (slider), and the edge | NeoShell's |
| Start's size (`StartMenuWidth/Height`) | dragging Start's corner | Start (Reset), and the corner | NeoShell's |
| Window frames (`WindowFrames*`) | "Window frames..." window from the taskbar menu | Window frames | NeoShell's |
| Exit NeoShell | taskbar menu, alongside Explorer | taskbar menu (alongside Explorer) and About | — |
| Switch to Explorer | Start, as the shell | Start and About, as the shell | — |

Left where they were, because the Windows surface NeoShell copies has them there: the desktop's View and Sort by
(`DesktopSortOrder` and Explorer's view settings, on the desktop menu as in Explorer), each widget's options (its
card's gear), adding widgets (the sidebar's + and menu). Windows' settings NeoShell follows but doesn't set — the
clock's seconds, badges, flashing, the far corner, Start's layout and folders, the wallpaper — are linked from the
pages (Related settings) rather than copied.

## The window (`Settings/SettingsWindow`)

Laid out as Windows 11's Settings app: the content drawn into a tall title bar (glyph E9E9 in the accent's text colour
and "NeoShell settings"), a `NavigationView` on the left (300 epx open, pane toggle only when the window is too narrow
for it to stay open; About in the footer as Settings' Windows Update), the page's title (`TitleTextBlockStyle`) and a
scrolling page at most 1000 epx wide, 40 epx in. Everything sits on the window's Mica: the navigation's pane and
content plates are made transparent. Light and dark follow Windows' app mode live (WinUI does the content; the caption
buttons are coloured on `ActualThemeChanged`, as WinUI colours them only for the theme the window opened in); the
accent comes from the theme resources. 1100 × 820 epx, centred on the primary monitor's work area.

- One at a time (`SettingsWindow.Open`): opening it again brings it to the front (`TopLevelWindows.Activate`), on the
  page asked for if any. Closed on `/exit`.
- Pages (`SettingsPage` enum, titles and glyphs in `SettingsPages`): Taskbar, Start, Desktop and wallpaper, Widgets,
  Window frames, Agents, About. A page is made each time it's shown (`SettingsPageView` subclasses).
- `SettingsPageView` fills a page's controls from the settings when it's loaded and again when something else changes
  them (the sidebar's edge dragged, Start resized); its own changes are saved at once through `SettingsStore.Update`,
  without reloading itself. `IsFilling` keeps the controls' change events while filling from saving.
- `SettingsCard` is Settings' card: 68 epx high, a 20 px glyph, header (14) and description (12, secondary), the
  control on the right; `SettingsStyles.xaml` has it, the section heading, expanders (Settings' "Taskbar behaviors"
  style: rows with dividers, checkboxes and combo boxes) and link cards with the "open" glyph E8A7.
- Pages:
  - **Taskbar**, in Settings' Personalization > Taskbar order: Taskbar items (Search, Widgets), Other system tray
    icons (Hidden icon menu; only while NeoShell keeps the tray, as the shell), Taskbar behaviors (alignment,
    auto-hide, all displays, combine), NeoShell (taskbar backdrop), Related settings (Windows' taskbar settings).
  - **Start**: the size and Reset, and links to Windows' Start settings and Folders.
  - **Desktop and wallpaper**: links to Background (alongside Explorer only: Settings can't open as the shell, and
    Control Panel's page sends there) and the classic Desktop icon settings (`desk.cpl,,0`, which works as the shell).
  - **Widgets**: the sidebar on/off, its panel background, its width (slider 240–560 epx in steps of 8; the sidebar
    follows each step, as it follows its edge).
  - **Window frames**: the prototype's window as a page (`Frames/WindowFramesPage`); its preview window closes when
    the page is left. Since 2026-10-12 it also starts, stops, installs and removes NeoShell.Frames.exe, which draws the
    XP Luna style ([Luna through overlay windows](window-frames-overlay.md#as-built)).
  - **Agents** (glyph E99A): agent access on/off, what agents may do (four switches, greyed while access is off), the
    connected agents with Disconnect (filled again as agents come and go), and the `claude mcp add` command for this
    NeoShell.exe in a read-only box ([Agent access](agents.md#permissions-and-safety)).
  - **About**: version (`1.0.0 (commit)` from the informational version), run mode, Switch to Explorer (as the shell,
    asked first as in Start) or Exit NeoShell (alongside), a link to docs/design.md on GitHub.
- Settings opened from NeoShell as the shell use `Launcher.OpenSettings`, which falls back to Control Panel (see
  [UWP apps as the shell](lifecycle.md#uwp-corewindow-apps-as-the-shell-not-possible-t36)).

## Opening it

- **Start: a NeoShell settings button** (`NeoShellSettingsButton`, glyph E9E9 "sliders", tooltip "NeoShell settings")
  in the bottom row, right after Switch to Explorer and before the folders. Why there: Explorer's Start has no place
  for another app's settings, and the bottom row is where Windows 11 puts its own shortcuts (the folders, including
  Windows' Settings gear, then Power). Putting NeoShell's button left of that group, next to NeoShell's other own
  button, leaves Explorer's folders and Power exactly as Explorer lays them out (still touching, 40 apart, in their
  fixed order); a pinned tile or an All entry would mix NeoShell into the user's own apps, and the account menu is
  Microsoft's card. The glyph is not the gear (E713), which stays Windows' Settings folder button. Shown in both run
  modes; a click closes Start and opens the window on Taskbar, or brings it to the front on the page it shows.
- **The taskbar's menu: "Taskbar settings"**, as Explorer's opens Windows' Settings on its Taskbar page.
- **The widget sidebar's menu: "Widget settings"** (Widgets page).

## Menus that became Explorer's

- **Taskbar context menu**, measured on Explorer 25H2 alongside NeoShell (right-click on the empty taskbar,
  UIA and a 4× screenshot): a `MenuFlyout` 159 × 73 px at 100 % with two items 28 px high — "Task Manager" (glyph
  E9D9) and "Taskbar settings" (E713) — and a separator between them. NeoShell's now has the same items, glyphs and
  separator; alongside Explorer it adds a separator and "Exit NeoShell" (no glyph), as before. The toggles it had
  (alignment, Search submenu, Show widgets, Combine submenu, backdrop submenu, auto-hide, hidden icon menu, all
  displays) and "Window frames..." are gone; "Taskbar settings" opens NeoShell's Settings on Taskbar. Compared
  side by side: the same text, glyphs and order; NeoShell's items are a few pixels taller (WinUI 3's menu metrics,
  as its other menus).
- **Widget sidebar menu**: "Add widget" (submenu, now with the add glyph), a separator, "Widget settings". "Show panel
  background" moved to the Widgets page. Vista's sidebar menu had Add Gadgets and Properties in the same places.

## AutomationIds

Window: `SettingsNavigation`, `<Page>NavigationItem` (`TaskbarNavigationItem`, `StartNavigationItem`,
`DesktopNavigationItem`, `WidgetsNavigationItem`, `WindowFramesNavigationItem`, `AgentsNavigationItem`,
`AboutNavigationItem`),
`SettingsPageTitle`, `SettingsPageScroller`. Taskbar: `TaskbarItemsExpander`, `SearchModeBox`, `ShowWidgetsToggle`,
`OtherTrayIconsExpander`, `HiddenIconMenuToggle`, `TaskbarBehaviorsExpander`, `TaskbarAlignmentBox`,
`AutoHideCheckBox`, `AllDisplaysCheckBox`, `CombineButtonsBox`, `ThemeBox`, `TaskbarBackdropBox`,
`WindowsTaskbarSettingsLink`.
Start: `ResetStartSizeButton`, `WindowsStartSettingsLink`, `StartFoldersLink`. Desktop: `BackgroundSettingsLink`,
`DesktopIconSettingsLink`. Widgets: `WidgetSidebarToggle`, `WidgetPanelToggle`, `WidgetSidebarWidthSlider`. Window
frames: as listed in [Window frames](window-frames.md). Agents: `AgentAccessToggle`, `AgentReadToggle`,
`AgentControlToggle`, `AgentScreenToggle`, `AgentPresenceToggle`, `AgentDisconnectButton` (one per agent, named
"Disconnect <client>"), `NoAgentsText`, `AgentCommandBox`; the taskbar's `AgentIndicator`. About: `NeoShellVersionText`, `RunModeText`,
`SwitchToExplorerSettingsButton`, `ExitNeoShellButton`, `DocumentationLink`. Elsewhere: `NeoShellSettingsButton`
(Start), `TaskbarSettingsMenuItem` (taskbar menu), `WidgetSettingsMenuItem` (sidebar menu).

## Tested

Unit: page titles and glyphs, the version text, the run mode texts, the settings the window writes round-tripping.
Live, both run modes at medium integrity: opened from Start, the taskbar menu and the sidebar menu (one window,
brought to the front on the page asked for); every taskbar setting changed and seen on the taskbar (alignment moved
Start from 774 to 13 px, search icon shown, labels with Never, Mica, auto-hide slid it away, all displays, widgets
hid and showed the sidebar), saved to settings.json; panel background and width followed by the sidebar; window
frames on/off and the preview; as the shell the hidden icon menu toggled the chevron, About showed Switch to Explorer
and no Exit, the taskbar menu had no Exit; keyboard (arrows and Enter in the navigation, Tab through a page); light
and dark switched live. Not run: Switch to Explorer from About (the same code as Start's button), Windows' Settings
links as the shell.
