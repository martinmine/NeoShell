# Themes

How NeoShell's look can be something other than Windows 11's: what a theme is, what it provides, what stays shared,
and how to add one. Part of the [NeoShell design](../design.md).

## What a theme is

A theme is which Windows the **taskbar and Start** look like and how their own parts behave (layout, sizes, button
shapes, menus, flyouts). Windows 11 is the default; Windows XP (Luna taskbar with the green Start button, Quick
Launch, the notification area and clock, the two-column Start menu) is the second, see [Theme: Windows XP](theme-xp.md);
Windows 98, Vista or 7 may follow.

A theme replaces whole views, not styles: XP's taskbar is not a restyled Windows 11 taskbar (30 px high, labelled
buttons that group when full, a Start button with text, a Quick Launch bar, a tray whose chevron expands in place),
and its Start menu is a two-column menu with cascading All Programs, not Windows 11's grid and search. What every
theme's views show and do underneath is shared, so a theme only draws it its own way:

| Shared, the same in every theme | Where |
|---|---|
| Taskbars per monitor, recreated on display, DPI and settings changes; hotkeys routed to the primary taskbar | `Taskbar/Taskbars` |
| Being a taskbar: frameless tool window that leaves the focus alone, app bar alongside Explorer or reserved work area as the shell, auto-hide, making way for full-screen apps, broadcasts | `Taskbar/TaskbarDock` |
| Window tracking, grouping, pinned apps, order, progress, overlays, badges, flashing | `WindowTracker`, `TaskListBuilder`, `TaskOrder`, `TaskButton` |
| A taskbar's buttons kept in step, clicks and Win+1…9 | `Taskbar/TaskButtonList` |
| Tray icons, their order and hidden icons, forwarding presses, moves and hover to their apps | `Tray/NotificationArea`, `TrayIconInput`, `TrayIconOrder` |
| Network, volume, battery, privacy and input state | `Tray/Indicators`, `IndicatorDisplay` |
| Clock settings, notifications, Do not disturb | `ClockSettings`, `ClockDisplay`, `Notifications/NotificationCenter` |
| Jump lists, the window's shell menus, launching | `JumpLists`, `JumpListMenu`, `ShellMenu`, `ShellContextMenu`, `Launcher` |
| Start's data: the app catalog, pins, search, launch history, account, places, power | `StartCatalog`, `StartPins`, `AppSearch` + `IndexSearch`, `UserAssist`, `UserAccount`, `StartPlaces`, `PowerItems`, `ShutDownDialog` |

Not themed (yet), and Windows 11's in every theme: the notification center and calendar, Quick Settings, toasts,
Alt+Tab, Snap, screenshots, AutoPlay, the desktop, the widget sidebar and NeoShell's Settings window. A theme's
taskbar decides whether it opens them (XP's clock opens no flyout; its volume icon would open a small slider of its
own). Other apps' window frames are styled separately ([Window frames](window-frames.md)); XP Luna frames are
there already, and the XP theme doesn't turn them on by itself.

"Theme" here is NeoShell's look. Windows' light/dark app mode and accent colour are `Taskbars.Theme` and
`Taskbars.Accent`: the Windows 11 views follow them, as Explorer's do; an XP view has fixed colours and ignores them.

## The setting

`ShellSettings.Theme` (`ShellTheme`: `Windows11`, the default; settings files from before it get Windows 11), chosen in
Settings > Taskbar > NeoShell > Theme (`ThemeBox`, listing `ShellThemes.All` by `ShellThemes.Name`).

Switching is **live**: `Taskbars.OnSettingsChanged` sees another theme and makes the taskbars again, as it does when
displays or the alignment change, and `CreateWindows` makes Start again first (`RecreateStartMenu`: the old one is
hidden and closed). No restart is needed because the taskbars are already throwaway views over shared state: the
tracker, the tray, the indicators and the notification center live in `Taskbars` and outlive their windows, and Start
is made once per theme only so it opens at once with its catalog loaded. Checked on the VM as the shell (with a
second, temporary theme value that made the same views): every switch made a new taskbar and Start, NeoShell's window
count stayed the same over repeated switches (nothing leaked), and Start, the tray and the buttons worked in the new
windows at once.

## The seams (`Themes/`)

- **`ITaskbarView`**: a theme's taskbar on one monitor, as `Taskbars` uses it: its monitor, window, where it is
  (`ScreenBounds`), showing and shutting it, revealing it (auto-hide), refreshing tasks, tray and clock, Windows' theme
  and the backdrop, full-screen apps, where a window's button and a tray icon are (minimize animations, tray icon
  rectangles, Snap and Alt+Tab ask), and the keys `Taskbars` routes to the primary taskbar (Win+1…9 and its variants,
  Win+T, Win+B, Win+A/K/P/Ctrl+V, Win+Space, Win+X). A theme does what its Windows did; for what its Windows didn't
  have (Quick Settings, the search box, the input switcher, the Quick Link menu) the call does nothing.
- **`IStartMenuView`**: `IsOpen`, `WasJustDeactivated` (a press on the Start button deactivates Start before its click
  arrives), `Show(taskbar)` above a taskbar, `Hide`, `Close`. `Taskbars` toggles it from the Start button, the Windows
  key, Ctrl+Esc, Win+S and the Copilot key, and closes it on a click elsewhere on the taskbar.
- **`ThemeViews`**: `CreateTaskbar` and `CreateStartMenu` pick the views for a `ShellTheme`; **`ShellThemes`**: the
  list and names for the Settings window. A `switch` per surface, no theme class: a theme is its views.

Windows 11's views are the existing ones, left where they were: `Taskbar/TaskbarWindow` (with `Clock`, `TaskBadge`,
`ThumbnailPopup`, `TaskbarFlyouts`, `QuickLinkMenu` and Quick Settings, the input switcher and the hidden icons it
opens) and `StartMenu/StartMenuWindow`. Other themes' views go in `Themes/<Name>/`.

### What moved out of the Windows 11 taskbar (the refactor, 2026-10-12)

Code moved as it was; only how it's reached changed:

- `TaskbarDock` took the window plumbing out of `TaskbarWindow`: the presenter, tool window and no-activate styles,
  re-adding no-activate (and closing the flyouts) when the taskbar loses the keyboard, Alt+F4 asking to shut down,
  the frameless frame, Peek's exclusion, the message subclass (DPI change → recreate, broadcasts → `Taskbars`), the
  app bar / work area / auto-hide placement with the theme's height (`TaskbarLayout.Bounds(…, height)`), the slide,
  the unhide and auto-hide timers, full-screen layering, `TakeKeyboard`, `BoundsOnScreen`/`FromScreen`, giving the
  space back on close and `Shut`. What keeps an auto-hidden taskbar up is the dock's (pointer, keyboard, open popups,
  Start, the notification center) plus the view's `busy` (Windows 11: its thumbnails).
- `TaskButtonList` (an `ObservableCollection<TaskButton>`) took the in-place update of the buttons, finding a
  window's button, and what a click does with no window or one (launch, Shift for another instance, bring forward,
  minimize when in front) and Win+1…9's cycling and Win+Ctrl+1…9's last window. A button of several windows is the
  view's: Windows 11 shows thumbnails, XP a menu of the windows.
- `TrayIconInput` took what a press, release, move and hover on a tray icon tell its app (double-click within the
  double-click time, the 400 ms hover that opens an app's pop-up). Dragging icons between the taskbar and the
  overflow stays Windows 11's.

Compared before and after on the VM, alongside Explorer and as the shell: the taskbar, Start, Quick Settings, the
notification center and calendar, the taskbar's menu, a jump list, a thumbnail and the hidden icons were captured
from the old and the new build in the same state, pixel for pixel identical (the clock and the sidebar's live widgets
left out). Live checks of the moved code: tray clicks, double-click, right-click and the tooltip on a test icon,
task button click and minimize, Win+7, Win+T and the taskbar going back to no-activate, auto-hide hiding and coming
back at the screen's edge, a full-screen window pushing the taskbar out of the topmost band, the work area.

### Pitfall found doing it

`Window.Content` throws (`0x800710DD`, "the WinUI Desktop Window object has already been closed") once the window is
closed, and the taskbar's `Activated` handler still runs after `Close` (its deactivation, on exit alongside Explorer):
the exception in a XAML callback ended NeoShell on every exit. The dock keeps the content element from construction,
as the view's own `Root` field always did.

## Adding a theme

1. A `ShellTheme` value and its name in `ShellThemes.Name` (the enum is saved by name: renaming a value loses the
   setting, and an unknown name makes the settings file unreadable for an older build).
2. Its views under `Themes/<Name>/`: a `Window` implementing `ITaskbarView` and one implementing `IStartMenuView`
   (XAML + code-behind), and their cases in `ThemeViews`. Give interactive controls AutomationIds, the Windows 11 ones
   where they mean the same (`StartButton`, `TaskList`, `TrayIcon`, `ClockButton`…) so the live checks carry over.
3. The taskbar view makes a `TaskbarDock` right after `InitializeComponent` (its content must exist) with its height,
   its `busy` and `hiding` callbacks; shows `TaskButtonList` items for `TaskListBuilder.Build` / `TaskOrder.Arrange`
   (keeping `Taskbars.TaskOrder` up to date, as `TaskbarWindow.RefreshTasks` does); and on the primary monitor the
   tray (`Taskbars.Tray`: `PromotedIcons`, `OverflowIcons`, `ChevronVisible`) with a `TrayIconInput`, and the
   indicators (`Taskbars.Indicators`) as its Windows showed them. It unsubscribes everything it subscribed to in
   `Closed`, and its `Shut` closes its flyouts, then `TaskbarDock.Shut`, then `Close`.
4. Anything that calls Windows goes in `NeoShell.Interop` as usual; what's pure (layout numbers, fitting, which menu
   items) gets unit tests.
5. This file (what the theme replaces and how), `CLAUDE.md`'s layout, and the area's own spec file for its numbers.

## The XP theme

Built as described above: `Themes/WindowsXp/`, measured and compared in [Theme: Windows XP](theme-xp.md). What it
added to the shared parts: `SystemMenu` and `TopLevelWindows.Cascade`/`Tile` in Interop (a task button's system menu,
the taskbar's and a group's arrange commands), italic fonts and `GdiText.Measure`, the Start Menu folders' reader
(`StartMenu/StartMenuFolders`), and a fix in `ShellBackdrop`: a see-through backdrop made before any acrylic or mica
one (XP's popups are all see-through) needs the thread's system dispatcher queue, which only those controllers had
been making (`EnsureSystemDispatcherQueue`; without it NeoShell failed to start in the XP theme).

How XP's views draw (one picture per surface, drawn in code from measured numbers, with icons and invisible buttons
over it) is the pattern for an older Windows' look; Windows 11's views stay XAML.
