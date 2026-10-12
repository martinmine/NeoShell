# Desktop icons

The desktop icons: contents, order and places, view settings, drag and drop, menus, undo and rename. Part of the
[NeoShell design](../design.md).

## Desktop icons (`Desktop/`)

Shell mode only, like the wallpaper: alongside Explorer, Explorer's desktop has its own icons.

- **Contents.** `DesktopFolder` (Interop) enumerates the desktop's `IShellFolder` (`SHGetDesktopFolder`), which
  merges the user's and the public Desktop folders and also lists the namespace (This PC, Libraries, OneDrive,
  drives…). `DesktopContents.IsShown` (unit tested) keeps what Explorer's desktop shows: items directly in one of the
  two Desktop folders, and the five icons of "Desktop icon settings" (This PC, User's Files, Network, Recycle Bin,
  Control Panel) as chosen in `HKCU\…\Explorer\HideDesktopIcons\NewStartPanel` (1 hides, 0 shows; only the Recycle
  Bin shows by default). Hidden and protected files follow Explorer's `Hidden` and `ShowSuperHidden` values.
- Each item is kept as its desktop-relative ID list (a byte array; a child of the desktop is also an absolute PIDL),
  so one menu can cover several items, even from both Desktop folders.
- **Order** (unit tested): system icons first in Explorer's order, then folders, then files, by the Sort by choice
  (Name, Size, Item type, Date modified; `ShellSettings.DesktopSortOrder`) and then by name, numbers compared by
  value.
- **Places** (`DesktopGrid`, `DesktopLayout`, unit tested): like Explorer's, one desktop spans every monitor. Each
  monitor's work area is a grid of cells from its top left corner (no margin); an icon's place is a monitor and a
  point of its grid in cells, whole while icons are aligned to the grid (`IconPosition`; its nearest cell is
  `IconPlace`). New icons fill the primary monitor's columns from the top left, then
  the other monitors', then columns past the primary's edge (they scroll). Icons can be dragged anywhere, to any
  monitor: a drag moves them by as much as the pointer moved, each to the cell nearest to where it lands (halfway
  rounds up, as in Explorer) on the monitor it lands on, or the nearest free one. Files dropped on the desktop, and
  items made with New, go to the cell where that happened, as in Explorer. A rename keeps the place. Sort by packs
  each monitor's icons again in that order on that monitor, and Auto arrange keeps them packed so (Explorer keeps each
  icon on its monitor for both; checked live). Arrow keys move the selection to the nearest icon that way on the
  screen, across monitors, keeping to the row or column where they can.
- **"Align icons to grid" off** (View menu, `FWF_SNAPTOGRID`; `DesktopGrid.ArrangeFree`, unit tested), as measured on
  Explorer's desktop with `IFolderView::GetItemPosition` through `IShellWindows`:
  - A dragged icon moves by exactly as much as the pointer (a drag of 1492,65 moved it by 1492,65), over other icons
    too: Explorer lets icons overlap. It stays wholly in the work area: Explorer stopped one at 1702,839 in 1764x940
    with 76x101 cells, so a position is kept within 0 to (work area / cell) - 1 cells (`DesktopWorkspace.Clamp`).
  - New icons (a file created, one restored) still take the first free cell in Explorer's order, and a cell is free
    only while no icon overlaps any of it: an icon 0.6 of a column or 0.7 of a row off its cell kept the cells on both
    sides taken.
  - Turning it on again puts each icon on its nearest cell, or the nearest free one (of two icons wanting the same
    cell one moves next to it), as `DesktopGrid.Arrange` does with saved places. Auto arrange and Sort by pack on the
    grid either way.
  - Moving icons on the desktop is not part of the undo history (Explorer's menu still offered "Undo Delete" after
    several moves).
- **Explorer's saved places** (`IconLayouts`, unit tested with a value Explorer wrote): positions live where Explorer
  keeps them, `HKCU\Software\Microsoft\Windows\Shell\Bags\1\Desktop\IconLayouts` (binary; `IconNameVersion` = 1),
  so icons stay put when switching shells (checked both ways on the VM). Read from shell32's `IconLayoutEngine`,
  `DesktopDictionary`, `DesktopData`, `WorkspaceData`, `IconNameTable` (Ghidra with the PDB):
  - 16 bytes of the property bag (zero), then the dictionary: `int` version 0x10003, the name table (`int` 0x10001,
    `uint64` count, strings), `uint64` desktop count (at most 256). A string is a `uint64` length counting its
    terminating null, then UTF-16; 0 for none. A name is the item's parent-relative parsing name (file name, or
    `::{CLSID}` for a system folder) followed by `>`, `\` for a folder or a space, `|` for the public Desktop or a
    space (flags 1, 2, 4; a system folder has no attributes, so only 1).
  - A desktop (one monitor arrangement): `int` version 0x10001/0x10002, linked key (string), `uint64` workspace count
    (at most 16), the workspaces, and for a linked desktop of version 2 an `int` per workspace. A workspace (one
    monitor, in work-area order: by left, then top): `int` version, for version 2 a shift (two `int`s, kept as read),
    columns, rows, flags (1 = primary), `uint64` icon count (at most 4096), icons: column and row as `float`s, then a
    `uint16` index into the name table.
  - Explorer finds a desktop by its key, each workspace as `%02d:(%03dx%03d)` (flags, columns, rows) joined by `_`.
    When the arrangement is new it initialises it from the saved desktop that fits best: monitors mapped primary to
    primary, then same grid size, then in order; rated 4 for the same grid, 3 when the icons' bounding box fits, 1
    when not; best rating, then the same monitor count, wins (NeoShell's `SavedPlaces` follows this, simplified from
    `DesktopMatcher::GetCompatibility`). Icons of a monitor with no counterpart join the new icons (on the primary).
    So unplugging a monitor brings its icons to the primary (the single-monitor layout), and plugging it back puts
    them back (checked live in both shells).
  - Desktops that differ only in the primary monitor's grid "look the same to the user"
    (`DesktopData::LooksTheSameToTheUser`): Explorer links them and shows one's icons for both. NeoShell's widget
    sidebar narrows the primary as the shell (19 columns here instead of 23), so a desktop it hasn't seen takes such a
    twin's places (the latest); one it has its own key for (or a link of Explorer's) takes its own. It saves only the
    desktop with its own key, never over another: until T40 it replaced every twin with its own, removing Explorer's
    full-width desktop, and Explorer then once laid its icons out afresh. Measured on the VM (T40): with only
    NeoShell's 19-column layout saved, Explorer took its places and, on a clean exit, saved into it; after an icon was
    moved (to column 22) it saved a desktop of its own key beside it, the other left as it was; later, with both
    there, a move made it take the narrowed one's newer places into its own and drop the other. NeoShell now leaves
    Explorer's as they are: switching shells with the sidebar open and closed (both ways) kept Explorer's places,
    an icon at column 22 included; icons moved in NeoShell with the sidebar open are saved for the narrowed desktop,
    which Explorer may or may not take up, as with its own twins.
  - Off the grid an icon's column and row are fractions. Explorer saves an item at listview position x,y as
    ((x - 14) / 76, y / 101) for medium icons: the 14 is the icon's inset in its cell, but its 2 pixel inset from the
    cell's top isn't taken off, so an icon left where the grid put it reads row + 0.0198 once saved off the grid.
    NeoShell saves the point it shows the icon at (whole rows for those), so such icons differ by those 2 pixels.
  - Explorer writes the value when its desktop closes (a clean exit, or now and then after a move), and reads it when
    it starts; NeoShell reads it at start and writes 0.5 s after icons move, keeping every other desktop as read.
    A value in another format isn't read and is never written over.
- **Grid** (`DesktopLayout.Spacing`, unit tested against `LVM_GETITEMSPACING` on the VM): shell32's
  `CListViewHost::UpdateIconSpacing`. At 96 DPI a cell is the icon's box, `icon + 6 + MulDiv(9, icon - 16, 240)`, at
  least 75 wide (`SM_CXICONSPACING`), and the box plus two label lines (44) high: 75x99 for medium icons. It's then
  stretched to the work areas (in 96-DPI pixels): with one monitor, across by the remainder shared out among the
  columns, down by what's over beyond 30% of a row (`CalculateOptimalSpacingForPrimaryMonitor`; 76x101 for the VM's
  1764x940); with several (`FindOptimalSpacing`), across by the first work area's stretch unless a width between
  the plain one and the smallest stretch leaves less over in all of them, down by the tallest any wants, up to the
  smallest stretch (76x103 with the second 1280x752 monitor). One spacing serves every monitor, scaled to each one's
  DPI in whole pixels (`dpi * spacing / 96`, as `IconLayoutEngine::WorkAreaToGridWorkspace`); columns and rows are
  the work area over the cell. The icon is centred in its cell; WinUI's per-monitor DPI scales icons and labels on a
  125% monitor as Explorer's do (an image loaded at that monitor's pixel size).
- **View settings** live where Explorer keeps them, so they carry over when switching shells: the icon size in
  `HKCU\Software\Microsoft\Windows\Shell\Bags\1\Desktop\IconSize` (32/48/96), "Auto arrange icons" as `FWF_AUTOARRANGE`
  (bit 0x1) and "Align icons to grid" as `FWF_SNAPTOGRID` (bit 0x4; checked against Explorer's
  `IFolderView2::GetCurrentFolderFlags`: 0x40200224 on, 0x40200220 off; on is also Explorer's default) of that key's
  `FFlags` (each monitor's icons stay packed in sort order on it), "Show
  desktop icons" in `Explorer\Advanced\HideIcons`.
- **Images** come from `IShellItemImageFactory` without `SIIGBF_ICONONLY`, so pictures get thumbnails, loaded off the
  UI thread at physical pixel size. Shortcuts get the stock link overlay (`SHGetStockIconInfo(SIID_LINK)`) in the
  corner, at most medium-icon size. Labels are white over a dark copy offset by a pixel, readable on any wallpaper.
- **Updates.** `FileSystemWatcher`s on both Desktop folders and on each fixed drive's `$Recycle.Bin\<SID>` (the
  Recycle Bin icon shows whether it's empty), and every `WM_SETTINGCHANGE` (folder options, Desktop icon settings,
  the work area), queue a debounced refresh. A refresh enumerates off the UI thread and updates the
  `ObservableCollection` in place (remove, move, insert), so the selection and loaded images survive.
- **View** (`DesktopIconsView`): one per monitor, in its `WallpaperWindow`: a `GridView` (extended selection;
  `DesktopIconPanel` places each container at its icon's cell) of the icons on that monitor (`DesktopIcons.IconsOn`).
  The views share one selection (`DesktopIcon.IsSelected`): a plain click selects only there and deselects the other
  monitors' icons, as in Explorer's one window; Ctrl+A selects all of them. The selection rectangle goes on across
  monitors (the pointer stays captured by the window where it started) and each view draws its part and selects its
  icons; the arrow keys move the focus to another monitor's window. An icon dragged to another monitor is dropped on
  that monitor's window (WinUI's drop events reach any window of the process), which moves it. Double-click or Enter opens; Delete, F2, F5, Ctrl+C/X/V and Alt+Enter work as in Explorer; a
  click on the empty desktop clears the selection. Dragging from the empty desktop draws a selection rectangle (accent
  coloured) and selects every icon it touches; with Ctrl held it adds to the selection.
- **Drag and drop** as on Explorer's desktop (`DesktopDragDrop`, Interop). Whatever is dropped on an icon that takes
  drops (`SFGAO_DROPTARGET`: a folder, the Recycle Bin, an app) or on the desktop itself goes to the shell's own
  `IDropTarget` for it (`GetUIObjectOf` for an icon, `CreateViewObject` for the desktop: the Desktop folder), so
  moving, copying and linking by the keys held, confirmations and progress are Explorer's. The icon under a drag is
  highlighted. An icon whose target refuses the drag counts as the desktop, as in Explorer's view: nearly every file
  claims drops (`SFGAO_DROPTARGET`; a handler registered for all files takes only some data), so the desktop's own
  icons let go over a text file move there (over it, with Align icons to grid off) rather than nowhere.
  - From other apps: a native OLE drop target, registered on WinUI's content window
    (`Microsoft.UI.Content.DesktopChildSiteBridge`) — OLE looks only at the window under the pointer, so one on the
    top-level window is never asked. The shell's targets need the drag's own data object, which WinUI's drop events
    don't give. `IDropTargetHelper` draws the source's drag image over the desktop.
  - Out to other apps: WinUI's drag (`StartDragAsync` on the icon's container, the files and folders as storage
    items, copy, move and link allowed), once the pointer has moved 4 epx with the button down on an icon; an icon
    that isn't selected is selected first. OLE's own drag loop (`SHDoDragDrop`) can't be used: WinUI turns on mouse
    in pointer, and the loop never sees the mouse move or the button go up. The storage API won't open anything in
    a hidden folder, so the public Desktop's shortcuts go as copies made in `%TEMP%\NeoShell\Dragged` (a streamed
    file instead hangs WinUI as it's added to the drag). System folders drag only within the desktop. A throwing
    async `DragStarting` handler ends the app, and an empty storage item list throws.
  - The desktop's own icons over the desktop: WinUI's drag reaches only WinUI's drop events in its own process, not
    OLE targets, so those events pass it on to the same logic with the shell's data object for the items
    (`GetUIObjectOf(IDataObject)`) in place of the drag's.
- **Menus**: Explorer's full menu (what its "Show more options" shows) in one WinUI menu, without that item.
  - `ShellMenu` (Interop) builds the shell's own `IContextMenu` (`GetUIObjectOf` for icons, `CreateViewObject` for
    the desktop, `CMF_NODEFAULT` there as Explorer does), fills the submenus filled on opening (New, Send to, Open
    with) by sending `WM_INITMENUPOPUP` through `IContextMenu3`, and reads the `HMENU` into items: label (access
    keys and shortcut text removed), verb, state, the item's bitmap and submenus. NeoShell shows them as
    `MenuFlyout` items; the handler's image, else a glyph for standard verbs (cut, copy, delete...).
  - Building the menu, the handlers show the wait cursor and then put back the UI thread's cursor from before, which
    is the wait cursor too (WinUI shows its own over its windows); `ShellMenu` sets the arrow once it's built, or the
    spinner stayed over the menu until the pointer moved.
  - The chosen command is invoked by its ID once the WinUI menu has closed, so it behaves exactly as in Explorer
    (Recycle Bin, confirmations, progress, installed apps' commands). The `ShellMenu` is released after that.
  - **First menu after a start.** It took about a second and missed items; now it's as quick and full as later
    ones. Measured as the shell on the VM (2026-10-12), from the button going up to the menu drawn (Stopwatch logging
    around each step, `QueryPerformanceCounter` shared with the test that clicks; the screen polled for Explorer):

    | Step (ms) | Desktop, first | Desktop, warm | jpg file, first | jpg, warm | Folder, first | Folder, warm |
    |---|---|---|---|---|---|---|
    | Click to `ContextRequested` (first click activates the window) | 53-79 | 1-2 | 79 | 2 | 66 | 2 |
    | View, Sort by, Paste, Undo items (JIT, first XAML types, undo text) | 24 | 3 | – | – | – | – |
    | `CreateViewObject` / `GetUIObjectOf` | 5 | 0.2 | 3 | 0.5 | 1 | 0.5 |
    | `QueryContextMenu` | 356-420 | 25-40 | 531-610 | 112-136 | 336 | 67-89 |
    | Submenus' `WM_INITMENUPOPUP` (New; Open with, Cast to Device, Give access to) | 334 | 6-9 | 248 | 80-100 | 4 | 3 |
    | Reading the `HMENU` (texts, verbs, bitmaps) | < 2 | < 1 | < 3 | < 1 | < 2 | < 1 |
    | WinUI items | 9 | 1 | 10 | 2-3 | 5 | 2 |
    | `ShowAt` to drawn (presenter template, first popup) | 150-160 | 90-120 | 194 | 110 | 226 | 105-130 |
    | **Total** | **~1000** | **140-165** | **~1000** | **310-340** | **680** | **180-225** |

    The Recycle Bin's menu is cheap either way (240 first, 100-125 warm). Where the shell's time goes (cdb stack
    samples of a test process building the menus as `ShellMenu` does, and each registered handler timed alone):
    - Desktop `QueryContextMenu`: about two thirds the registry verbs (`CRegistryVerbsContextMenu`): the
      `DesktopBackground\shell` commands (Display, Personalize, Spotlight's, stickers) and packaged apps' verbs from
      the State Repository (Open in Terminal), activated through the SCM; the rest the `shellex` handlers, cold: OneDrive's
      FileSyncEx 39, Sharing (ntshrui) 48, Work Folders 17, New 4.5, DesktopSlideshow (stobject) 3.
    - New: `CNewMenu::_InitMenuPopup` mostly waits for `StateRepoNewMenuCache` (packaged apps' New entries from the
      State Repository), then each type's icon and name.
    - Files: Microsoft Defender's handler (`shellext.dll`, "Scan with Microsoft Defender") checks its own signature
      (`WinVerifyTrust`) as it loads its strings; packaged apps' verbs (Edit with Photos, Paint, Clipchamp, Edit in
      Notepad) are activated through the SCM, some in `dllhost` surrogates; Open with lists and draws its apps.
    - The first menu of a process also misses what the shell looks up in the background on first use, shown from the
      second menu on: Open in Terminal (desktop, folders), the jpg's three Edit with verbs (28 items of 31), Send to's
      places (3 of 6 for the jpg, none for the folder), Include in library ("Retrieving libraries...").
    - Explorer's own menu (Windows 11's, its full one is a click further), after restarting Explorer, from the screen:
      desktop 1460 ms first, 330 warm (to its first pixels; 410-430 to the end of its fade); a file (an .exe) 965 first,
      250-350 warm. NeoShell's warm menus were already quicker.

    The fix: `DesktopIcons`, two frames after the icons first show, has `ShellMenu.WarmUpAsync` build and throw away
    the desktop's menu and one menu per kind of icon (`DesktopContents.MenuKinds`, unit tested: a folder, a file per
    extension, each system folder; at most 8), read in full (submenus filled), twice: a type's second menu loads the
    handlers of the verbs the first only found out about. That runs on a thread of its own (an STA `MessageLoop`, below
    normal priority; 2.4-3.6 s here, the UI thread untouched), released there (`FinalRelease`; unique wrappers).
    - The thread stays, idle: COM unloads the DLLs an apartment loaded when it ends, so with a thread that ended, the
      first jpg menu still took 740 ms in `QueryContextMenu` (Defender's signature check again, the handlers loaded
      again); with it kept, 115-140, as warm.
    - Then, at low priority, the view builds the desktop's menu once on the UI thread without showing it (about 70 ms,
      once): the UI thread's own share, which another thread can't warm (the desktop's `QueryContextMenu` 117 ms on the
      first real menu without it, 30 with it), and compiling the menu code. Putting a menu presenter in the tree to
      load its template as well saved 40 ms more, but NeoShell once ended (a XAML fail-fast, `E_UNEXPECTED`) a few
      seconds later: left out.
    - Filling New, Send to and Open with only when they open, as Explorer's own popups do, isn't needed for speed and
      would change when handlers run; not done.

    After (same measurements, both themes; the desktop's menu is the same in each): desktop first 196-268 (warm
    130-165), jpg first 370-500 (warm 300-340), folder first 240-360 (warm 180-240), Recycle Bin first 176-189, every
    first menu with all its items. What's left: the first click activates the desktop's window (50-120 ms before
    `ContextRequested`; 4 ms when the desktop was clicked before), and packaged apps' handlers that run in `dllhost`
    surrogates (Photos, Paint, Clipchamp on pictures) end a while after their last menu, so a picture's menu after a
    long pause starts them again (870-930 ms, as Explorer's does).
  - Icons: the shell's menu as is (Open, Open with, Send to, Give access to, Cut, Copy, Create shortcut, Delete,
    Rename, Properties, installed apps' commands...).
  - Desktop: what Explorer's view adds itself, View (icon size, Show desktop icons), Sort by, Refresh, Paste and
    Paste shortcut, then the shell's menu: installed apps' commands, New (the shell's New menu: Folder, Shortcut and
    every registered file type) and Display settings / Personalize. View is Explorer's: Large, Medium, Small icons,
    a separator, Auto arrange icons, Align icons to grid, a separator, Show desktop icons. After Paste and Paste
    shortcut come "Undo <action>" (Ctrl+Z) and "Redo <action>" (Ctrl+Y), each only while there is something to undo
    or redo (see **Undo** below).
  - Display settings and Personalize open the Settings app, which can't start while NeoShell is the shell (the only
    time the desktop is NeoShell's), and Control Panel's pages for them open Settings too. They open the classic
    dialogs left instead: the display adapter's properties (`display.dll,ShowAdapterSettings`, with List All Modes)
    and Desktop icon settings (`desk.cpl,,0`).
  - Rename comes back to NeoShell (only the view can edit a name); an item made from New is renamed straight away,
    as in Explorer.
- **Undo** (`ShellUndo`, `ShellUndoServer`, Interop), as on Explorer's desktop: "Undo Delete", "Undo Rename", "Undo
  Copy", "Undo Move", "Undo New" and "Redo ..." for the session's latest file operation, whichever app did it.
  Read from shell32 with cdb and Ghidra (undomgr.cpp):
  - Shell file operations that keep an undo record (`IFileOperation` with `FOFX_ADDUNDORECORD`, which the shell's own
    menus, rename (`SetNameOf`) and drop targets use) add it to the calling thread's undo manager
    (`SHGetThreadUndoManager`, exported as `SHELL32_SHGetThreadUndoManager`; kept in TLS, freed with its last
    reference), which passes it on to the session's desktop undo manager: `CLSID_DesktopUndoManager`
    {3eef301f-b596-4c0b-bd92-013beafce793}, a local server. To get it shell32 sends `WM_USER+24` to the shell window
    (`GetShellWindow`) with the class's index in its table of desktop local servers (`_CreateDesktopLocalServer`):
    Explorer then serves it on a thread of its own process, so File Explorer windows, the desktop and every app share
    one history. Without an answer COM starts its `LocalServer32`, `rundll32 shell32.dll,SHCreateLocalServerRunDll
    {clsid}`, which asks the shell window once more and then serves it itself. (A newer path, behind a feature flag,
    looks for it in the running object table first.)
  - As the shell NeoShell serves it as Explorer does, by running that same rundll32 entry on a thread of its own
    (`ShellUndoServer`, started with the shell session, ended with `WM_QUIT`). It has to be in the shell's process: a
    test app enumerating the units from another process got `E_ABORT`, so only the serving process can ask a unit for
    its text, and the history lives as long as the shell, as Explorer's does.
  - The menu's items are shell32's `_InitEditUndoRedo`: whether there is something (`CanDo`: an undo or redo
    description exists and no undo is running; NeoShell asks File Explorer's Undo and Redo commands,
    `CLSID_UndoExplorerCommand` {0dbd7044-...} and `CLSID_RedoExplorerCommand` {1cc6b704-...}, through `GetState`),
    the text from the latest unit (`EnumUndoable`/`EnumRedoable`, then `GetUndoText` of the undocumented
    {33747358-8a56-4a62-9342-f4b86d97a8e4}, the slot after `IOleUndoUnit`'s: "&Undo %s\tCtrl+Z" from windows.storage's
    strings), else plain "Undo"/"Redo" (shell32's strings 0x1045/0x104A). On the desktop's menu an item that can't be
    done is left out (`DeleteMenu`), not greyed.
  - Undo and Redo run the same commands' `Invoke`: a new thread (`_UndoThread`) undoes the latest unit with the wait
    cursor and beeps when there's nothing; the shell's own progress and confirmations show. A redone operation can't
    be undone again (the history is empty after it, in Explorer too). Ctrl+Z and Ctrl+Y on the desktop do the same.
  - The UI thread keeps its undo manager (shell32 frees one with its last reference and clears its TLS slot), and
    releases the units' wrappers right away, each a wrapper of its own (`ComPointer.TakeOwnershipUnique`): a cached
    .NET wrapper of a freed manager, handed out again for the next one at the same address, dropped that one's
    reference and corrupted the heap.
- **Rename**: a text box in a flyout over the label, with the name selected without its extension; Enter or a click
  elsewhere renames through `IShellFolder::SetNameOf` (keeps a hidden extension, reports errors in the shell's
  dialogs), Esc cancels.
- After a file operation the desktop takes the foreground back: the shell's operation windows hand it to the next
  window in the z-order when they close, and that is never the bottom-most desktop.
- **Alt+F4** on the desktop (or on the taskbar while it has the keyboard) doesn't close the window
  (`AppWindow.Closing` is cancelled; NeoShell's own `Close` doesn't raise it) but opens the **Shut Down Windows**
  dialog (`ShutDownDialog`), as Explorer does: "What do you want the computer to do?" with the power menus' choices
  for it (Switch user, Sign out, Sleep, …; see Start's [Power menus](account-and-power-menus.md#power-menus-poweritems-interop-shellpoweroptions-shellpower)), chosen first as in Explorer, a line on
  what the choice does, OK and Cancel (Enter and Esc). shell32's own dialog
  (`ExitWindowsDialog`, ordinal 60) hands the request to Explorer's taskbar and shows nothing without it. A WinUI
  window with Mica, in the middle of the primary monitor; one at a time.
