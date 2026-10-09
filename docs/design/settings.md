# Settings

The settings record and its file. Part of the [NeoShell design](../design.md).

## Settings (`Settings/`)

A single `record Settings` serialized with `System.Text.Json` (source-generated context), loaded at startup,
saved on change. Unknown/missing values fall back to defaults; a corrupt file is renamed to `.bak` and defaults used.

```
TaskbarAlignment     Center | Left
CombineButtons       Always | WhenFull | Never
AutoHide             bool
ShowOnAllDisplays    bool
ShowSearchButton     (former; read once, see Search on the taskbar)
TrayMode             (former; read once, see Hidden icons under System tray)
TaskbarBackdrop      Acrylic | Mica | Translucent | Transparent
PinnedTaskbarApps    list
PinnedStartApps      list
ExplorerStartPinsImported  bool
ExplorerTaskbarPinsImported  bool
StartMenuWidth       double (epx)
StartMenuHeight      double (epx)
DesktopSortOrder     Name | Size | ItemType | DateModified
ShowWidgetSidebar    bool
WidgetSidebarWidth   double (epx)
Widgets              list (id, kind, X/Y and size while floating, the kind's options)
WindowFramesEnabled  bool (off)
WindowFrameStyle     the global frame style's name (Windows default)
WindowFrameStyles    list of the user's frame styles (see Window frames)
WindowFrameRules     list (process, optional class, style name or null = leave alone)
```
