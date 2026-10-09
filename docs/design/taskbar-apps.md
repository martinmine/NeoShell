# Taskbar: pinned apps, jump lists and thumbnails

Pinned apps, jump lists, thumbnails, dragging over the taskbar, progress, overlay badges and thumbnail toolbars. Part
of the [NeoShell design](../design.md).

## Pinned apps

- Stored in settings as a list of `{ AppUserModelId or Path, Arguments, DisplayName }`.
- Launch packaged apps (AUMID `<family>!<app>`) with `IApplicationActivationManager::ActivateApplication`, on a
  background thread as it waits for the app, recording the start in UserAssist as Explorer does (see "How Explorer
  records a packaged app's start" under [Start](start-menu.md#start-menu-startmenu)); other apps through `shell:AppsFolder\<AUMID>` when there is an AUMID,
  otherwise `ShellExecuteEx` on the path. Opening `shell:AppsFolder\<packaged AUMID>` needs a handler hosted by
  Explorer and fails without it ("Class not registered").
- UWP (CoreWindow) apps, Settings and Calculator among them, can't show in shell mode: activation fails without
  Explorer's immersive shell, which only a Microsoft-signed process can host (see [UWP (CoreWindow) apps as the
  shell](lifecycle.md#uwp-corewindow-apps-as-the-shell-not-possible-t36)). Packaged desktop apps (Notepad, Terminal) work.
- Pinning from the Start menu and from a task button's context menu.
- The first time Start's catalog loads, Explorer's taskbar pins are added once (`ExplorerTaskbarPinsImported`),
  matched to catalog apps as Start's are. The shortcuts in `User Pinned\TaskBar` have no order and packaged apps
  have none, so they're read from `HKCU\…\Explorer\Taskband\Favorites`: a version byte, then per pin a 32-bit
  length, the ID list and a separator byte (0xFF after the last). Each ID list is resolved with
  `SHCreateItemFromIDList` to its AppUserModelID and shortcut target.

## Jump lists

The app's jump list heads the button's menu, read each time it opens (`JumpLists`, Interop): the entries pinned to
it, the app's own categories and Recent or Frequent, in the app's order, then its Tasks. Headings are text-only menu
items; entries have their icons (loaded in the background) and open on click. Start's app menus show the same list
(see [Start menu](start-menu.md)), with the same pin button and entry menus (`JumpListMenu`, shared).

- **AppID**: the button's AppUserModelID, or for an app without one the implicit AppID Windows gives it: its path
  starting with a known folder's GUID where it can (`{1AC14E77-…}\notepad.exe`), as in `shell:AppsFolder` (tested).
- **The app's list** (`ICustomDestinationList`) can't be read back through any API. Windows keeps it in
  `%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\<name>.customDestinations-ms`, where the name is a CRC-64 of the
  AppID in capitals as UTF-16 (polynomial 0x92C64265D32139A4, reflected, starting from all ones; hex without leading
  zeros; tested against Windows' own file names). `CustomDestinations` (tested) reads it: version 2, the category
  count; per category its kind (custom with a title, known with Frequent 1 / Recent 2, or tasks), the entries and a
  0xBABFFBAB footer. An entry is a CLSID and the object's persisted data, in practice a shell link; a link's data
  carries no length, so it's measured from its structure ([MS-SHLLINK]) and then loaded into the shell's own
  `ShellLink` with `IPersistStream::Load` (from `SHCreateMemStream`).
- An entry's title is the link's `System.Title`, else its description, resolved with `SHLoadIndirectString` when it's
  a resource reference (`@shell32.dll,-21817`); `System.AppUserModel.IsDestListSeparator` links are separators.
- **Pinned, recent and frequent entries** come from the shell's automatic destination list, the one Explorer's
  jump list broker uses (T42; read with symbols in windows.storage.dll and Windows.Internal.Shell.Broker.dll's
  `CJumpViewBroker`), so both shells read and change the same lists (`%APPDATA%\…\Recent\AutomaticDestinations\
  <same CRC-64>.automaticDestinations-ms`, a compound file NeoShell never parses itself). Class
  AutomaticDestinationListBoth `{656E51BD-CAD6-4683-AC07-3E3D50D7F453}`, `IAutomaticDestinationList`
  `{E9C5EF8D-FD41-4F72-BA87-EB03BAD5817C}` (the first of a chain of four; its vtable after IUnknown: `Initialize(appId,
  path, NULL)`, `HasList`, `GetList(type, max, flags, iid, out)`, `AddUsagePoint`, `PinItem(item, index)`,
  `IsPinned(item, out index)`, `RemoveDestination(item)`, …). `GetList` types: 0 pinned, 1 recent, 2 frequent; flags 1
  as the broker passes (links too). Recent and frequent include the pinned entries, so the broker asks for as many
  more as there are pins and skips those that `IsPinned` (S_OK and the place, else E_FAIL); NeoShell does the same,
  and also leaves pinned links out of the app's own categories. Items come back as `IShellItem`s, or `IShellLink`s
  for links pinned from an app's own categories (saved with `IPersistStream::Save` into the entry's data).
  `PinItem`'s index: -1 pins last (`CAutoDestList::PinLast`), -2 unpins, 0 or more pins at (or moves to) that place
  (`PinAt`); each change is persisted and announced (`NotifyDestinationChanged`) at once.
- **How many**: `JumpListItems_Maximum` (`HKCU`, else `HKLM`, `…\Explorer\Advanced`; 13 when unset, at most 60) is
  shared out in the broker's order (`JumpListBudget`, tested): the pins first, then the app's own categories item by
  item (a Recent or Frequent the app asked for with `AppendKnownCategory` takes one more), and Recent and Frequent
  share what's left; tasks don't count. Measured on Explorer: 13 recent files, 12 beside one pin, 11 beside two;
  2 pins, a custom category of 3 and Recent show 7 recent. An
  app without categories of its own (no list, or tasks only, as Edge's) gets Recent, as in Explorer's taskbar and
  Start, without the extra one; a known category other than 1 or 2 shows nothing (Settings writes one of -1, and
  Explorer shows it no Recent).
- **Pinning and removing** (Explorer's JumpViewUI.dll in ShellExperienceHost, measured with UI Automation and
  screen grabs on a test app with recent files, a custom category and a task):
  - Hovering an entry shows a pin button at its right end: 45 wide, as tall as the row (28), flush with its right
    edge, glyph E718 (Pin) or, under Pinned, E77A (Unpin), tooltip and name "Pin to this list" / "Unpin from this
    list". Files also get a Share button left of it, which NeoShell leaves out (no share UI of its own). Tasks
    get no button.
  - Right-clicking an entry opens its menu at the pointer: a file `Open` (E737), `Open file location` (ED43) ┃ `Pin
    to this list` (E718) or `Unpin from this list` (E77A), `Remove from this list` (E74D; not for pinned ones) ┃
    `Properties` (E90F); an app's own link `Open` ┃ Pin, Remove; a task just `Open`. (Explorer's comes from the
    JumpViewExecuteHelper local server's `IContextMenu` for the item, which can add a folder's shell verbs, and gives
    a web page Open file location too; NeoShell shows the fixed set above.) Open file location is
    `SHOpenFolderAndSelectItems`, Properties `ShellExecuteEx` with the `properties` verb.
  - Pinning moves the entry to the end of Pinned and unpinning back to its place by date; the menu is filled afresh in
    place and stays open, as Explorer's (neither animates the change in the grabs taken; not recorded at 60 fps). Remove goes, as the broker's `RemoveItem`, to both
    lists: the app's own (class DestinationListBoth `{38FE0CF4-6A59-4729-8E4A-2D580059EDE4}`,
    `IInternalCustomDestinationList` `{507101CD-F6AD-46C8-8E20-EEB9E6BAC47F}`: `SetApplicationID`, then
    `RemoveDestination`, which the app then sees in `GetRemovedDestinations`, tested) and the automatic one.
  - Pinned entries can't be reordered: JumpViewUI's list only drags entries out (its `OnDrop` does nothing), unlike
    Windows 10's; `PinItem` with an index would do it.
  - Checked both ways, alongside Explorer and as the shell: pins and removals made in NeoShell show in Explorer's
    jump list and the other way round, the same entries in the same order.
- Not done: a web page's short name (Explorer shows Edge's `https://…/login/device` as "device"; every `SIGDN` and
  `System.ItemNameDisplay` give the whole URL).
- **Opening**: a link through its own `IContextMenu` default command, which keeps its arguments, working directory and
  a packaged app's identity, as Explorer does; a recent file with `ShellExecuteEx` on its ID list (the default verb,
  which may not be the app the list belongs to).
- Icons: a link's icon location (`SHDefExtractIcon`), else its target's icon; `ms-appx:` icons of packaged apps
  aren't read, so those entries show the target's.
- **Tooltips**: a file's name without its extension and its folder, "Doc15 (E:\…\docs)"; a web page's address;
  links have none.
- **AppID gap**: an app that sets its AppID only for its process (`SetCurrentProcessExplicitAppUserModelID`), not on
  its windows, gets a button without its AppID (window tracking reads the window's property and the package's), so
  its jump list is looked up under its implicit AppID and comes up empty; Explorer finds it.
- **The menu** (T39a, measured on Explorer's for File Explorer, Terminal, Claude and Windows PowerShell): always
  296 px wide, border included, whatever it holds (its windows are all 318 with the shadow), so longer names end in
  an ellipsis; headings 16 px in, 8 px above and below; separators a pixel further from the items than WinUI's, and
  the menu a pixel taller at the bottom: NeoShell's rows now land on Explorer's to the pixel. The items' icons and
  text sit a pixel left of Explorer's (WinUI's padding for a menu opened by the mouse comes from its template's
  visual state and can't be widened). The launch item (the app's name) shows the app's 16 px icon, as Explorer's
  (`AppIcons.Read`), not a glyph.

## Thumbnails

- Hovering a button (500 ms) opens a popup with one live DWM thumbnail per window (`DwmRegisterThumbnail` on the
  popup's HWND, `DwmUpdateThumbnailProperties` to place each one over a XAML placeholder), title and close button.
  It closes 400 ms after the pointer leaves both the button and the popup; once open, it follows the pointer along
  the taskbar.
- The popup slides up out of the taskbar (200 ms) and back into it on closing, and slides sideways to the next button
  (`WindowSlide`, as Start): it's the window that moves, since DWM draws the thumbnails into the window. It sits
  just below the taskbar in the topmost band (`PinnedWindow.SetLayer(Topmost, above)`), so the taskbar covers it.
- Peek: hovering a thumbnail for 400 ms shows only its window (`DwmpActivateLivePreview`, dwmapi ordinal 113: the
  undocumented call Explorer's taskbar makes, as there is no public one); moving to the next thumbnail moves the peek
  straight away, and it ends when the pointer leaves the thumbnails, on a click (after switching) or when the popup
  closes. DWM ignores a second peek while one is on, so moving it ends the first, which crossfades. The taskbar,
  popup and wallpaper windows set `DWMWA_EXCLUDED_FROM_PEEK` to stay visible, as Explorer's do.
- As the shell, snap groups with one of the button's windows come first (see [Snap groups](windows.md#snap-groups-snapgroups-unit-tested)).

## Dragging over the taskbar (`TaskbarDrop`, unit tested)

What Explorer's taskbar does with a drag from any app (files from File Explorer or the desktop, text, images),
measured on 25H2 with `mouse_event` drags and frame recordings, and read from `Taskbar.View.dll`
(`ExternalTaskListDragDropController`, `TaskbarFrame::OnDragEnter`…: the XAML taskbar's own drop events, which take
the drag's `DataPackageView`; `Taskbar.dll`'s `CTaskListWnd`/`CTrayDropTarget` OLE targets only draw the drag image):

- **Nothing opens by dropping on a button.** A file over a running or pinned app's button, Start, the empty taskbar,
  the tray or the clock is refused (⊘, no caption); dropping does nothing. Windows 10's "Open with <app>" is gone.
- **Hovering a button brings its window forward**, ~410 ms after the drag enters it, moving or not (the hover
  time); the button shows its hover plate meanwhile. With several windows the previews open instead; hovering a
  preview ~1.1 s brings that window forward (no peek first) and closes them. A pinned app that isn't running does
  nothing.
- **Pinning by drag.** Exactly one program (`.exe`) or a shortcut to one, not pinned already, is "Link" (the system's
  default caption for the effect, with its glyph) anywhere from Start to the tray: the buttons open a one-button gap
  where it would go (before the first button whose middle is past the pointer; over Start, first) and it's pinned
  there on the drop, growing in. Over the tray and the clock it's refused. Anything else (a document, a shortcut to
  one, a `.bat`, a folder, several files, an app pinned already) is refused everywhere and only hovers buttons; a
  pinnable drag doesn't bring windows forward. A program is named by its file description (`charmap.exe` →
  "Character Map"), a shortcut by its file name; a shortcut's arguments go with its program, whose windows then
  share the button (as Explorer groups them after a restart).

NeoShell does the same with WinUI's drop events on the taskbar's root and on the previews' window, as Explorer's XAML
taskbar: the system then draws the drag as over Explorer's (the dragged image small, with the effect's glyph and
caption). An OLE target with `IDropTargetHelper` (as the desktop has) was tried first: it drew the source's big
image with the classic cursors instead. WinUI's events also see drags of NeoShell's own desktop icons. The dragged
items are read once on entering (`GetStorageItemsAsync`, under a deferral); a shortcut through its property store
(`ShellItems.ReadShortcut`). Pointer events don't come during a drag, so the hover plate, hover timers and the
previews' hide timer are driven from the drag events. The drag's input went to its source, so a window is brought
forward as Alt+Tab does (`TopLevelWindows.SwitchTo`): a plain `SetForegroundWindow` is refused. The gap moves the
buttons by `Translation` (150 ms, as reordering); centred, the row recentres once the app is in it. Not compared: a
centred Explorer taskbar (Explorer's here is left-aligned), a second monitor, and drags of packaged apps from Start
(Explorer pins those too, by their AppID).

## Progress, overlay badges, thumbnail toolbars

Apps call `ITaskbarList3`, which is implemented in `explorerframe.dll` inside the app process: it finds the task
band through the `TaskbandHWND` window property on `Shell_TrayWnd` and sends it messages (`HrInit` fails with
`E_NOTIMPL` without it). So in shell mode `TrayHost` creates an `MSTaskSwWClass` child of `Shell_TrayWnd` and sets
that property; alongside Explorer these calls go to Explorer. Messages (`TaskbarListCall`, unit tested):

| Message | Call | wParam | lParam |
|---|---|---|---|
| `WM_USER+65` | `SetProgressState` | window | `TBPF_*` |
| `WM_USER+64` | `SetProgressValue` | window | 0…0xFFFE (scaled by the caller) |
| `WM_USER+79` | `SetOverlayIcon` | window | `HICON`, 0 removes |
| `WM_USER+60` | `MarkFullscreenWindow` | flag | window |
| `WM_USER+85` | overlay description | window | atom (ignored) |
| `WM_USER+76` | `ThumbBarAddButtons` | window | shared memory: the buttons |
| `WM_USER+77` | `ThumbBarUpdateButtons` | window | shared memory: the buttons |
| `WM_USER+78` | `ThumbBarSetImageList` | window | shared memory: the image list |
| `WM_USER+81` | `SetThumbnailClip` | window | (ignored) |

Apps only start once told their button exists: the `TaskbarButtonCreated` registered message, sent with
`SendNotifyMessage` when a window is added to the task list (shell mode). The task button shows the first window's
progress (bar along the bottom; indeterminate, error and paused states) and overlay icon. The overlay sits where the
badge goes (below): 16 px over the icon's top-right corner, 6 px right of the icon and 7 above it (Explorer's
`OverlayIcon`, measured through UI Automation), and is hidden while the app has a badge.

**Badges** (an app's count or glyph from `BadgeUpdateManager`; `AppBadges`, `TaskBadge`, `BadgeLook`, unit tested).
Only apps with package identity can set one: for an unpackaged app with an AppUserModelID from a Start menu shortcut,
`BadgeUpdater.Update` fails with `ERROR_NOT_FOUND`. Any process may set a packaged app's badge, and Windows keeps it
whether or not the app runs, so a pinned app shows it too.

- **Reading them.** Explorer's taskbar (Taskbar.dll, `CTaskBand::UpdateBadgeAsync`) asks the undocumented Windows Runtime
  class `WindowsUdk.UI.StartScreen.BadgeProvider` (windowsudk.shellcommon.dll, in-process, base trust) for each task
  group's AppUserModelID: `GetForUser(user)` (null works: the process's user), `GetRegisteredBadge(appId)` → a `Badge`
  with `Kind` (0 none, 1 number, 2 glyph), `Number`, `Glyph` (`BadgeGlyphKind`: 1 activity, 2 alert, 3 alarm,
  4 available, 5 away, 6 busy, 7 newMessage, 8 paused, 9 playing, 10 unavailable, 11 error, 12 attention) and a
  `Changed` event. IIDs and method order are from the DLL's symbols (`Com/IBadgeProvider.cs`). The value arrives a
  moment after the first call (the provider subscribes to the notification platform's badge updates for that app).
  It works from a medium-integrity unpackaged process, alongside Explorer and as the shell. A count of 0 and
  `value="none"` show nothing.
- **Threads.** The provider raises `Changed` while holding its lock; a handler registered on the UI thread (an STA) is
  marshalled back to it, and a UI thread then calling `GetRegisteredBadge` waits on that lock forever (found the hard
  way). Explorer calls it from its task pool; `AppBadges` likewise only touches it on thread-pool threads, reads each
  change after the event returns, and the taskbar reads its cache.
- **"Show badges on taskbar apps"** is `TaskbarBadges` (DWORD) under `HKCU\…\Explorer\Advanced`, on unless 0
  (Taskbar.dll `IsTaskbarBadgingEnabled`). Settings just writes the value and Explorer follows within a moment without
  any message, so NeoShell watches the key (`RegistryWatcher`, `RegNotifyChangeKeyValue`).
- **Look** (Taskbar.View.dll `BadgeConfiguration`; sizes from UI Automation and screenshots at 96 DPI): a plate 16 px
  high, at least 16 wide, corner radius 8, its right edge 6 px right of the 24 px icon and its top 7 px above it (the
  overlay's spot), inside the icon's panel so it shrinks with the icon when pressed. Counts in Segoe UI Variable 11,
  padding 4 on each side, tight line bounds, centred: 1 → 16 wide, 10 → 19, 99 → 21, above 99 "99+" → 28. Glyphs in
  Segoe Fluent Icons 12: activity EDAB, alarm EDAC, alert EDAD, error EDAE, attention EDB1, newMessage EDB3, paused
  EDB4, playing EDB5. Colours: the plate is `SystemAccentColorLight2` with black text on a dark taskbar,
  `SystemAccentColor` with white text on a light one (accent on the taskbar or not); alert, attention and error are
  white on #D73B02; available, away, busy and unavailable are plain dots of #008117, #FFC20A, #D82128 and #999999.
  The button's help text is "Status N items" / "Status 1 item" / "Status Alert" (Explorer's `BadgeStatusText` strings).
- **Animation** (`SharedAnimations`): appearing, the plate's `Scale` goes 0 → 1.1 at a third (cubic-bezier 0.85,0,
  0.75,1) → 1 (0.35,0,0,1) over 500 ms about (8, 8), the centre of its left end; disappearing, → 0 over 167 ms
  (0,0,0,1) about its centre. A new count or glyph of the same width just swaps. A width change moves the left edge
  (the right one stays): Explorer's implicit `Offset` animation slides it there over 333 ms (0.55,0,0,1); NeoShell
  animates `Translation` from the old place, only for a badge already shown (a new one pops in where it belongs).
- Explorer once missed the badge of a UWP app whose button existed when Explorer started, until the app restarted;
  NeoShell shows it. A focus session hides badges by writing `TaskbarBadges` = 0 (see Focus in [Notifications and calendar](notifications.md#notifications-and-calendar-notifications)).

**Thumbnail toolbars** (a player's previous, play/pause and next under its preview; `ThumbBarCall`, `ThumbBar`, unit
tested). The thumbnail toolbar calls carry their data in shared memory from `SHAllocShared`, its handle duplicated
into the taskbar's process: `SHLockShared` with NeoShell's own process ID maps it, and it's copied within the message
(the app frees it afterwards). Only what's mapped is read (`VirtualQuery`): the data's own counts are checked against
it, as a bad count read past the mapping would end the shell.

- Buttons: a 32-bit count (at most 7), then packed `THUMBBUTTON`s of 540 bytes: mask, ID, image index, the `HICON`
  as 32 bits (copied at once), 260 characters of tooltip, flags. Adding sets the buttons; updating changes the masked
  parts of the buttons with the same IDs.
- The image list: a 32-bit size, then what `ImageList_Write` writes (`ImageListStream`, unit tested): `ILHEAD`, the
  image strip as a BMP file (any depth, palette or `BI_BITFIELDS`; images left to right, then down), and for
  `ILC_MASK` the mask as another BMP. `ImageList_Read` would need common controls 6, which NeoShell doesn't load.
- The previews give every window a row of 32×28 buttons under its preview when one of them has a toolbar: the
  button's own icon or its image from the list (16 epx), its tooltip, disabled, hidden and non-interactive as asked.
  They follow the app's updates while open (VLC's play button turning to pause). A click posts `WM_COMMAND` with
  `THBN_CLICKED` and the ID to the window, as Explorer sends; `THBF_DISMISSONCLICK` closes the previews. A tap on a
  button doesn't count as a tap on its preview (which would switch to the window).
