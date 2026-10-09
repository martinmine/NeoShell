# Start menu

The Start menu: Pinned and folders of pins, All, app menus, the app catalog, search and the bottom row. Part of the
[NeoShell design](../design.md).

## Start menu (`StartMenu/`)

- One topmost popup window (`StartMenuWindow`), created at startup and shown above the taskbar that opened it:
  centred on the monitor, or at its left when taskbar items are left-aligned. Acrylic, rounded corners, activatable
  (unlike the taskbar) for the search box; hides on deactivation, Esc, a click elsewhere on the taskbar, or Win.
- Activation: `Window.Activate` alone doesn't take the foreground from the app the user was in, so Start calls
  `SetForegroundWindow` (allowed: the click or key was the last input). On closing it hands the foreground back to
  that app; otherwise Windows picks the next window in z-order, which can be Explorer's invisible Start/search host.
- Flies out of the taskbar (250 ms, decelerating) and back into it on closing (150 ms, accelerating), as in Windows 11.
  The window itself moves, frame by frame (`WindowSlide`, on `CompositionTarget.Rendering`): the acrylic belongs to the window, so
  sliding the content would leave an empty acrylic panel standing still. It sits just below the taskbar that opened it
  in the topmost band (`PinnedWindow.SetLayer(Topmost, above)`), and is cut off at the taskbar's edge
  (`PinnedWindow.VisibleBottom`, a window region) so it rises from behind the taskbar whatever is in front of the
  screen's bottom and however see-through the taskbar is; the thumbnails do the same. It's shown once, cut off
  entirely, at startup, so the first opening doesn't slide up a black window before WinUI's first frame. What it shows is
  reset (search, an open folder or category, the letters, scroll) once it's out of sight; opened again while closing, it turns back from where it is.
- Toggling: pressing the Start button deactivates Start before the button's click arrives, so a click within
  400 ms of a deactivation doesn't reopen it.
- Resizable by dragging a top corner (`ResizeGrip`, with the resize pointer): the bottom stays above the taskbar, a
  centred Start grows on both sides, a left-aligned one only has the top-right grip. Live while dragging; the size
  (as the monitor allowed it) is saved on release. 832 by 860 epx by default, at least 480 by 400, at most the
  monitor above the taskbar (`StartMenuLayout`, tests).
- Home is one scrolling page below the search box, as Explorer's 25H2 Start with Recommended off (Settings →
  Personalization → Start → Recent off, which T16 skips): **Pinned**, then **All**, each switched off by Settings'
  Pinned and All (`HKCU\…\CurrentVersion\Start` `ShowPinnedSection`, `ShowAllAppsSection`, 1 when missing; found with
  Procmon on SystemSettings.exe), followed live through a `RegistryWatcher` on the key. Without Pinned, All's heading
  takes Pinned's place (its text 93 from Start's top). The page scrolls from 64 below Start's top, as Explorer's.
  - **Pinned** grid (`ShellSettings.StartPins`: apps and folders of apps; the former apps-only `PinnedStartApps`
    is read once into it). Drag to reorder by hand (`GridReorder`, unit tested): a copy of the pin follows the
    pointer (in a layer above everything, so it can leave a folder's panel) and the icons between its old and new
    place shift one slot. Pinned icons shrink while
    pressed and a dragged one grows, as on the taskbar. The grid's own drag and drop keeps the
    dropped icon hidden until the drag operation winds down, and its item transitions animate the move as a removal
    and an addition, so both are off. Pinned is updated in place when Start opens, not refilled (which
    would replay every icon's entrance). The first time the catalog loads, Explorer's Start pins
    are added once (`ExplorerStartPinsImported`). Start keeps them encrypted in `start2.bin`, so they're read through
    `StartTileData.dll`'s `IStartLayoutCmdlet::ExportStartLayout` (the object behind `Export-StartLayout`), which
    writes `{"pinnedList":[{"packagedAppId":…},{"desktopAppLink":"%APPDATA%\…\x.lnk"}]}`. A shortcut is matched to
    the catalog by its `System.AppUserModel.ID`, else its `System.Link.TargetParsingPath`. The export flattens
    folders: a folder's apps are listed in its place, in its order, with nothing to say they were in one (checked
    with two folders, 25H2), so folders aren't carried over. Inside `start2.bin` they are there: caught in the
    debugger (`StartMenu!SlimObfuscationManager::Scramble`, called by `StartMenuPersistenceManager::SaveModel` →
    `StoreFileToDisk`), the plain text is JSON, `{"pinnedList":[{"id":"{guid}","pinType":2,"name":"","items":
    [{"id":"W~Microsoft.Windows.Explorer"},{"id":"P~Claude_…!Claude"}]},{"id":"P~windows.immersivecontrolpanel_…"}],
    …}` (an empty name is "Folder"; `W~` a desktop app's AUMID, `P~` a packaged app's). But the file is AES-CBC
    encrypted with a key Start derives in private code (`StableCryptoFunctions::GetSymmetricKeys`, from the
    FILETIME in the file's header and fixed seeds); reading it would mean re-implementing a private cipher any
    update can change, so it isn't done.
  - **No Recent row.** NeoShell's Start had a Recent row of the six apps started last (milestone 5); it's gone (T23).
    Explorer 25H2's "Recent" section is Recommended renamed (recently added apps, recent files, tips: T16, skipped),
    and with it off Start shows no recently started apps anywhere. UserAssist
    (`HKCU\…\Explorer\UserAssist\{CEBFF5CD-…}\Count`: ROT13 value names, AUMIDs or known-folder paths, the run count
    at offset 4, focus time in ms at 12, last run as a FILETIME at 60) is still read each time Start opens, for All's
    order of use (below). `Launcher` starts non-packaged apps with `ShellExecuteEx` + `SEE_MASK_FLAG_LOG_USAGE` so
    NeoShell's launches are recorded too, and records packaged apps' starts as Explorer does (below).
  - **How Explorer records a packaged app's start** (25H2; Procmon on the key, cdb breakpoints on
    `shell32!CUserAssist::FireEvent` in Explorer and Start): Explorer's process writes every value, always through
    shell32's UserAssist object (`CLSID_UserAssist` {DD313E04-FEFF-11D1-8ECD-0000F87A470C}, in-process, interface
    {49B36D57-5FD2-45A7-981B-06028D577A47}) as `FireEvent(&{CEBFF5CD-…}, UAE_LAUNCH = 0, AUMID, 0)`; shell32 bumps
    the value's run count and last run, the session totals in `UEME_CTLSESSION` (its second DWORD counts starts)
    and its own top lists, and skips it all when "Let Windows track app launches" is off.
    - From Start: `StartTileData!TileStoreTransformer::ActivateTileInternal` puts
      `AppActivation.ShouldNotifyUAOfLaunch = true` in the activation's property set and hands it to the shell
      broker in Explorer (`ImmersiveShellBroker`). A UWP app's value is written by twinui.pcshell's view manager
      when its view first shows (`CApplicationManager::_HandleViewNavigationRequested` →
      `AppActivationPropertySetHelpers::ConditionallyNotifyUAOfLaunch`); a packaged desktop app's by the broker
      (windows.immersiveshell.serviceprovider) after it starts it through daxexec. The broker's activation site also
      answers `SID_ExecuteLogUsage` {582B888F-80D5-4BC4-9A6D-5D7A58EFD60A}, so twinui.appcore's
      `DesktopAppXActivator` starts the desktop app with `ShellExecuteEx` + `SEE_MASK_FLAG_LOG_USAGE`, which adds a
      second value for the executable (`{6D809377-…}\WindowsApps\Microsoft.Paint_<version>_x64__…\mspaint.exe`),
      ~0.1-0.3 s before the app's. Each start adds one to each value's run count.
    - From the taskbar: `Taskbar!LaunchFromTaskbar` opens the pin's `shell:AppsFolder` item with `ShellExecuteEx`;
      windows.storage records the AUMID (`StoreHintsAndReportUserAssistInfo`) before the AppsFolder handler starts
      the app. One start, one count.
    - `IApplicationActivationManager::ActivateApplication` alone records nothing: no property set, no site.
    - NeoShell does the same (`PackagedApps.Activate(…, logUsage: true)` then `UserAssist.RecordLaunch`, for every
      launch through `Launcher`: Start's pins, All and search, taskbar pins and their menus, Win+1…9,
      toasts). The activation manager (twinui.appcore, in-process) takes an `IObjectWithSite` site; NeoShell's
      answers `SID_ExecuteLogUsage` as the broker's does, and UWP activation never asks for it. Compared value by
      value with the same app started from Explorer's Start: the same two values, counts and layout. Alongside
      Explorer, `UEME_CTLSESSION`'s totals don't move for NeoShell's starts (shell32 keeps the session per
      process, and Explorer's copy wins; the same holds for desktop apps NeoShell starts with
      `SEE_MASK_FLAG_LOG_USAGE`); as the shell they count as Explorer's do. Explorer's own Start doesn't reorder its
      category folders or drop "New" for an app only NeoShell started: those follow Start's own data, not UserAssist.
  - **All** (`AllApps`, unit tested; Interop `Shell/StartAppData`), as Explorer's 25H2 Start, on the page below
    Pinned. NeoShell's separate All apps page, its button beside Pinned and its Back button are gone (T23). Read
    from StartMenu.dll (`CategoryProvider`, `AllAppsViewModel`, `AppSortHelper`) and StartTileData.dll with their
    PDBs in Ghidra, Explorer's Start through UI Automation, and its registry and CloudStore data:
    - **Heading**: "All" (14 semibold) 31 below Pinned's grid, 32 tall, its text 63 in; at the right (its right edge
      52 from Start's) a flat `DropDownButton` "View: Category" (14 px, 32 tall; automation name "View selected,
      Category") with a menu below it, right-aligned: Category, Grid, List, the current one ticked. The view is
      Explorer's own `HKCU\…\Start\AllAppsViewMode` (0 Category, 1 Grid, 2 List), which NeoShell writes too, so both
      Starts show the same view; NeoShell follows it live, Explorer reads it as its Start starts.
    - **Category view**: cards 156 × 192, 27 apart and 12 between rows, four to a row from 64 in, 9 below the
      heading. A card is a 156 square (radius 8, `CardBackgroundFillColorDefault`, 1 px `CardStrokeColorDefault`) with
      four 68 places two by two from 10 in, the icon 32 in each, flat buttons with the app's name as tooltip; below it,
      from 160, a 156 × 32 flat button with the category's name (12 px). With more than four apps the fourth place
      shows the next four apps' icons (16, 2 apart, a 34 square) and opens the category, as the name does. Explorer's
      automation names ("Other category with 15 Items, Claude, Visual Studio Code, Microsoft News, and 12 others"). An
      open category is the folder panel (450 × 378, centred, its own acrylic) with the name as a 20 semibold title
      instead of the name box, the apps as 96 × 84 tiles four to a row; it grows out of the card, from the card's size,
      in 333 ms and shrinks back in 150 ms, as a folder (recorded at 60 fps: the same curves as the pin folders').
      The card isn't hidden behind it. A click beside it or Esc closes it.
    - **Categories**: Start's `AppCategory` values 0-31 (0 Other, 1 Accessibility, 2 Communication, 3 Games, 4 Travel,
      5 Security, 6 Personalization, 7 Photo & Video, 8 Social, 9 Utilities & Tools, 10 Kids & Family, 11 Medical,
      12 Health & Fitness, 13 Productivity, 14 Books & Reference, 15 Developer Tools, 16 Entertainment, 17 Music,
      18 Personal Finance, 19 Education, 20 Business, 21 Navigation & Maps, 22 Graphics, 23 Multimedia Design, 24
      Government & politics, 25 News & Weather, 26 Sports, 27 Lifestyle, 28 Shopping, 29 Food & Dining, 30
      Creativity, 31 Information & Reading; the names are its `AllApps_Category_N` strings). Where an app's comes
      from: StartTileData asks a web service ("APS") when a tile turns up and saves the answer in
      `HKCU\…\Start\TileProperties\<tile id>\Category` (a tile id is `P~` + a packaged app's AUMID, or `W~` + a desktop
      app's AppsFolder id; ids with backslashes are nested keys); StartMenu's `GetCategoryForTile` reads that
      (feature 55161678; else the mappings below), and an app without a saved category is Other — VS Code here,
      though Windows' mappings list it under Developer Tools. NeoShell reads the same keys. Only when Start has saved
      none at all (it never ran for the user) does it fall back, as `ExtendedProperties::FetchCategoryFromLocalMappingsAndStoreInRegistry`
      does, on Windows' own mappings: `%SystemRoot%\SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy\StartMenu\Assets\AllAppCategoryMappings`,
      LZMS-compressed with the Compression API's header (`cabinet.dll` `CreateDecompressor(COMPRESS_ALGORITHM_LZMS)`),
      4.4 MB of JSON `{"13": ["notepad", …], …}` with ~70 000 ids: a packaged app by its package family name, a
      desktop app by its target's path below `%systemroot%`, `%ProgramW6432%` or `%ProgramFiles(x86)%`
      (`\system32\narrator.exe`), else by its AUMID (`msedge`). The web service's answers differ (Paint 23 against the
      file's 7; osk 15 against 1).
    - **Merging** (`PruneAndCombineCategories`): 7, 23, 22 and 6 always go into Creativity; 2 and 5 into
      Productivity; 25, 24, 14 and 21 into Information & Reading; 3, 26 and 17 into Entertainment and 28, 12, 29, 11,
      18 and 4 into Lifestyle only while they have fewer than three apps; then every category but Other with two apps
      or fewer goes into Other (News & Weather's two apps end up there). Counts are of items, a folder being one; a
      category that is a single folder of three apps or more shows the folder's apps instead (Accessibility here).
    - **Order**: apps in a category by `Rank` (Start's tile `Relevance` × 1000, rounded) then name
      (`AppSortHelper::CompareRanks`); categories by the sum of their two first apps' ranks (`RankCategories`,
      descending). Start's relevance is a decaying count of launches it tracked (StartTileData's
      `LocalStartVolatileTileProperties`: `Relevance` float, launch count, last launch, launches per day; in the
      CloudStore value `…\CloudStore\Store\Cache\DefaultAccount\$de${6af0fa78-…}$$windows.data.unifiedtile.localstartvolatiletilepropertiesmap`,
      Claude 0.183, File Explorer 0.072, Settings 0.056 here). Explorer's figure counts only starts its own shell saw,
      so NeoShell measures use itself, from UserAssist: an app's starts plus its minutes in front, over every value
      that is the app. On this machine that gives Explorer's order exactly (categories Other, Productivity, Utilities &
      Tools, Developer Tools, Creativity, Entertainment, Accessibility; Most used Claude, File Explorer, Settings,
      Notepad, Terminal, Edge) except for apps started outside Explorer (Camera and Calculator, started by NeoShell,
      rank above apps Explorer saw started once).
    - **Grid and List views**: the apps by name under their letters, Explorer's letters in its order: &, #, A to Z,
      then other scripts under a globe (E12B); accents dropped (É under E). Each letter's header is a flat 728 × 40
      button 52 in, its letter 12 in at 14 px, 9 below the previous group and 4 above its apps; it zooms out to the
      letters. Grid tiles are 96 × 84 from 32 in (eight to a row), the icon 32 at 12 down, the name (12 px) on two lines,
      or on one above "New" or "System"; List rows are 728 × 40 from 52 in, the icon 24 at 12, the name 20 after it
      at 12 px with "New"/"System" below. **Most used** (Settings → Start → All → "Show most used apps (grid and list
      views only)", `ShowFrequentList`): a first group, its header "Most used", of the six apps with the highest rank
      above 0 (`TryAddTileToFrequentApps`).
    - **New and System** under an app's name (12 px; "New" in `AccentTextFillColorPrimary`, "System" secondary; in
      the category panel and the name views, and in the cells' automation names): StartMenuProperties.HasNewBadge is
      `RoamedTileProperties.FirstSeenTime == 0` (windowsudk.shellcommon), and Start sets the first-seen time when the
      app is started from Start. NeoShell reads Start's roamed tile map from the CloudStore
      (`…$$windows.data.unifiedtile.roamedtilepropertiesmap\Current` `Data`: a 16-byte header, then Bond compact
      binary v1: a map of tile id to properties, field 0 `StartTileProperties` with field 10 FirstSeenTime and 20
      IsUserPinned): an app is new when it has no first-seen time there and hasn't been opened from NeoShell's Start
      (`ShellSettings.StartAppsOpened`; NeoShell never writes Explorer's store). On this machine that marks the same
      apps as Explorer's, of those both list. Nothing is new when the map is missing. "System" (`IsSystemComponent`) wins over "New": Start's
      own lists (`ShellLists::s_packagedAppSystemCategorizationOverrides`, `…unPackaged…`: Store, Game Bar, Phone
      Link, Get Help, Windows Security, Dev Home, Windows Backup, File Explorer, WSL… yes; Camera, Edge, Media Player
      Legacy, Remote Desktop no), else a packaged app of `PackageOrigin` Inbox (`SignatureKind.System`), else for a
      desktop app the app resolver's flag — read here as a program in the Windows folder, plus Windows Tools.
    - **Letters** (Explorer's `SemanticZoom` zoomed out): the page gives way to "All" (63 in, 93 down) with a "‹ Back"
      button at the right (69 × 32) and the letters in the middle, four to a row in 48 squares 4 apart: Most used
      (clock E823), &, #, A-Z, the globe; those without apps dimmed (disabled). A letter goes back to the page
      scrolled to its group; Back or Esc goes back where it was. The zoom, recorded at 60 fps (T39c): out, the page
      shrinks towards its middle to about 0.55 and fades while the letters come in from 1.3× and fade in, all in
      ~167 ms, decelerating, "All" and Back there at once; back in, the letters grow to 1.3× and fade over ~250 ms,
      "All" and Back go at once, and the page fades in under them from ~100 ms, unscaled. Then the search box has the
      focus. NeoShell does the same (`HomeScale` on the page, `LetterGridScale`).
    - **Folders** (T39c; `AllApps.Items`, unit tested; read from StartTileData's `AppsListGeneratedCollection` and
      StartMenu's `AllAppsViewModel`, `CategoryProvider` with their PDBs in Ghidra): an app's folder is its Start Menu
      folder as the AppsFolder reports it, `System.Tile.SuiteDisplayName` ({86D40B4D-9069-443C-819A-2A54090DCCEC},16:
      the top folder below Programs, with its desktop.ini name; nested folders count as their top one, so
      Python\Python 3.14's apps are in "Python", and the per-user and all-users folders of a name are one). A
      folder is made once a second app of its name (any case) turns up: one app alone stays an app (Visual Studio Code
      here). A folder's apps go by name; it sorts by its name among the apps, after an app of the same name ("Visual
      Studio", then the "Visual Studio" folder). In Grid and List it shows Explorer's folder glyph
      (`SuiteFolderIconImageSource`, StartMenu\Assets\UnplatedFolder\UnplatedFolder.ico in Client.Core), its
      automation name "Accessibility folder, Collapsed"; it opens in the category panel (title, apps as tiles, first
      app focused) growing out of its icon. In Category view a folder is one item of the category its first app with
      a category is in (`TryGetCategoriesForTiles`; Git with Developer Tools, Python in Other), as used as its first
      app; the card shows it by its first app (`AddAppOrFirstAppOfSuiteToGroup`: Git Bash on Developer Tools' card),
      the open category as a plate of its first four apps' icons, as a folder of pins, and the card's count is of
      apps ("Other category with 15 Items"). A folder opened from an open category takes the panel's place (Explorer's
      may differ: not compared). No menu on a folder.
    - **Hidden apps** (`AllApps.IsHidden`, unit tested): the apps of the folders Windows Tools holds are left out —
      StartMenu's `AllAppsViewModel::IsHiddenTile` hides suites named "Windows PowerShell" and shell32's strings 21761
      (Windows Accessories), 21762 (Windows Tools) and 21788 (Windows System): Command Prompt, Control Panel, Run, Task
      Manager, Remote Desktop, Media Player Legacy, Character Map, the administrative tools… (and the Windows Tools
      item itself only where the `StartMenu-ShowWindowsTools` licensing value is 0, which NeoShell doesn't read). StartTileData's block lists push
      Power Automate, and Dev Home unless the user said they're a developer (`CloudExperienceHost\Intent\developer`)
      or developer mode is on, into Windows Accessories, so they're hidden too; Click to Do is hidden while the
      session's `Explorer\SessionInfo\<id>\ClickToDo\ClickToDoHideEntryPoint` is 1 or missing. Search still finds
      them all. With this, All lists exactly Explorer's apps and folders here, in its order, and the categories hold
      Explorer's items (Other 15, Productivity 9, Utilities & Tools 19, Developer Tools 26, Creativity 10,
      Entertainment 4, Accessibility 5). Not done: the A9 (Recall) and NPU block lists, the cloud-SKU path list.
    - **Keyboard** (T39c; `StartNavigation`, unit tested), as Explorer's (its All is one grouped GridView, so the
      letters' headers take the focus): Tab goes search box → Pinned (one stop) → View → All (one stop: where the
      focus last was in it, else its first item, not a header) → user → power, and around; Down in the empty search
      box goes to the first pin. Pinned is a GridView as Explorer's (arrows within it only). In All, Left and Right
      step through headers and items in order; Up and Down go by rows within a letter, from its first row up to its
      header and from its last row down to the next header, and from a header down to its first item or up to the
      last row above (in the column the focus came from); where nothing is straight above or below the focus stays and
      the page scrolls 16 px, as Explorer's. Home and End go to All's first header and last item, also from the pins
      (Explorer's Pinned and All are one list). Category cards take the focus as a whole (the card, not its places),
      arrows move by cards four to a row, Enter or Space opens the category; an open category or folder focuses its
      first app, keeps Tab on it, arrows and Home/End move in its rows of four, Esc closes it and gives the focus back
      to the card or folder (an open pin folder does the same with its tile). Enter opens, Shift+F10 or the menu key
      shows the app's menu; Esc closes just that menu. Typing anywhere goes to the search box.
  - **Folders of pins** (Windows 11 23H2+; `StartPins` and `GridReorder`, unit tested), as Explorer's:
    - Holding an app over the middle of another app or a folder (the pointer within 28 px of the cell's centre,
      either way; Explorer's zone measured at about +31/-23 px in a 96 px cell) shows the drop will group them; off
      the middle, the drop goes before or after the cell under the pointer and the cells in between make way. There
      is no delay: Explorer previews as soon as the pointer is there. An app under the pointer shows the folder to
      be: the plate fades in, the app's icon shrinks to half and slides into the plate's first place (9 left, 8 up)
      and its name fades, in 150 ms (Explorer: ~8 frames at 52 fps). Folders don't nest: a dragged folder only
      moves. Dropped, an app on an app makes a folder in the target's place, the target first; on a folder, it goes
      last in it. Explorer then saves and reloads its model, and shows it (recorded at 60 fps, T39c): the preview
      goes, the target app fades out in ~167 ms, nothing shows for ~633 ms, then the folder fades in over ~333 ms,
      easing in and out; the dragged app stays away. NeoShell does the same (`GroupWithFade`; it waits 533 ms, as
      laying the new folder out takes it ~0.1 s here). Anything that needs the pins in the meantime (a press, a
      menu, closing Start) makes the folder at once.
    - The tile: a 40 px plate (1 px `CardStrokeColorDefault` edge, `CardBackgroundFillColorDefault`, radius 4)
      centred where an icon would be, with the first four apps' icons at 16 px two by two, 2 apart, the first 3 in
      and 4 down from the plate's edge. The name below: "Folder" until renamed (stored as "").
    - Opening it: a 450 × 378 panel centred in Start, radius 8, a 1 px `SurfaceStrokeColorFlyout` edge and an
      acrylic of its own: StartMenu's `FolderModal` template (decoded from Client.Core's resources.pri, T39c) uses
      `AcrylicInAppFillColorDefaultBrush` (Start doesn't override it), so it is Start's colour behind it at that
      brush's luminosity. Measured on Explorer's (T39c): #2B2B2C dark, #F2F3F2 light, #14325D on a dark accent Start
      (#1E4277), #DEF1F6 on a light accent one (#B5E2EC). NeoShell's in-app `AcrylicBrush` can't see the window's
      backdrop, so `StartMenuLayout.FolderPanelColor` works the colour out from Start's tint (unit tested): the base
      colour at luminance 43 / 243, the accent at 47 two fifths of the way to grey; measured #2D2D2D, #F1F1F1 and
      #17355F. On a light accent Start NeoShell's Start itself stays dark accent (Explorer 25H2's turns light accent),
      so its panel does too. It grows out of the tile — from 38/450 of its size at the tile's centre to full size in 333 ms,
      `cubic-bezier(0,0,0,1)`, which fits Explorer's 0.48/0.65/0.84/0.92/0.99 at 33/67/133/200/267 ms — and
      shrinks back into it in 150 ms, accelerating. The tile is hidden while the panel stands for it; Start isn't
      dimmed. A click beside the panel, or Esc, closes it (Esc again closes Start). The name is a centred 20 px
      semibold `TextBox`, 320 × 40, 33 px from the top, flat until hovered or focused; Enter, leaving it or closing
      the folder renames the folder, and Esc in it puts the name back. The apps are 96 px cells, four to a row, from
      33, 94 (scrolling after three rows).
    - Apps are dragged within the panel to reorder them, or out of it onto Start behind it (the panel stays open)
      to take them out; they land before or after the cell under the pointer. A folder stays while it has an app
      (Explorer 25H2 keeps one-app folders, also after Start reopens); taking the last one out removes it and closes
      the panel.
  - **App menus** (right-click, or the menu key, on any app; `StartAppMenu`, unit tested), as Explorer's 25H2 Start
    (read through UI Automation, which sees inside Start's `CoreWindow` once found from its handle):
    - No Open item. The commands come in a fixed order, from `StartMenu.dll`'s `ContextMenuSorter` (the list its
      `GetContextMenuAsync` builds): Move to front, Move left, Move right, Create a new app folder, Move to app folder,
      Remove from app folder, Pin to / Unpin from Start ┃ Run as administrator, Open file location ┃ File Explorer's
      Manage, Properties, Map / Disconnect network drive ┃ Pin to / Unpin from taskbar, App settings, Uninstall.
      A separator parts groups that have something; then the jump list, without one. (The sorter also knows Rate and
      review and Share, only with extended verbs, Run as different user and Remove from list; NeoShell leaves them
      out, and Start has no More submenu in 25H2.)
    - Moves: in the grid Move to front only from the third place (from the second, Move left does it), Move left /
      right where there's room; a folder's own menu has only those. A pinned app also gets "Create a new app folder"
      (the app alone in a new folder in its place, not opened) and, with one folder, `Move to app folder "Name"`, with
      more a "Move to app folder ▸" submenu of the folders' names (text only); the app goes last in it. An app in a
      folder: Move left / right within it, "Remove from app folder" (it goes just after the folder).
    - Run as administrator, Open file location, Uninstall and File Explorer's verbs are the app's verbs in
      `shell:AppsFolder`, as Explorer's are (`AppResolverTransformer::GetVerbs` asks the desktop broker for the
      item's shell verbs): `ShellMenu.ForApp` binds the folder's item (by AppUserModelID, else full path, else
      known-folder path such as `{1AC14E77-…}\magnify.exe`; a full System32 path doesn't parse) to its
      `IContextMenu` (`BHID_SFUIObject`) and the verbs `runas`, `OpenFileLocation`, `Uninstall`, `Manage`,
      `ItemProperties`, `connectNetworkDrive`, `disconnectNetworkDrive` become items, run through that menu (Start
      closes first). So they're there exactly when Explorer shows them: no Uninstall for Edge or Settings, no Run as
      administrator for Settings, no Open file location for packaged apps. Open file location opens the shortcut's
      folder with it selected; Run as administrator elevates the shortcut, or a full-trust packaged app.
    - Uninstall: a packaged app gets Explorer's dialog in Start (`InterceptTileUninstallVerb`): a `ContentDialog`
      'Uninstall "Name"?' / "This app and its related information will be removed.", Uninstall and Cancel (the
      default), 374 wide, centred in Start, with a 1 px `SystemAccentColor` edge and no smoke over Start (WinUI moves
      the smoke into a popup of its own, collapsed on `Loaded`); then `PackageManager.RemovePackageAsync` for the
      user, the app's pins removed and the catalog reloaded. A desktop app's opens Settings the way Explorer's
      `SettingsUninstallVerb` does: Settings (`SystemSettingsAUMID`) activated with
      `page=SettingsPageInstalledApps&target=SystemSettings_StorageSense_AppSizesListFilter` (Installed apps, the
      search box ready).
    - App settings (packaged apps): Settings activated as Explorer's `SettingsVerb` does,
      `page=SettingsPageInstalledApps&target=SystemSettings_StorageSense_HiddenAppAdvancedPageLink&invoke=true&parameter=<family name>`,
      a system app's (`SignatureKind.System`, e.g. Settings) under `SettingsPageSystemComponents` /
      `…HiddenSystemComponentsAdvancedPageLink`: the app's advanced options page, as from Explorer's Start.
    - As the shell Settings can't show, so App settings is left out and a desktop app's Uninstall runs the folder's
      own verb, which opens Programs and Features (in an `explorer.exe /factory` window, which works without the
      shell); the folder's Run as administrator for a packaged app goes through Explorer and fails there, so it's
      left out too.
    - The jump list (`JumpLists`, as the taskbar's) below: each category under a 12 px secondary heading 16 in and
      32 tall, its entries with their icons; long names end in an ellipsis at Explorer's 290 px menu width.
    - Search results get the commands of Windows' search box instead: Run as administrator, Open file location (a
      folder glyph, E8B7), Pin to / Unpin from Start, Pin to / Unpin from taskbar, App settings, Uninstall, in one
      run without separators or jump list (the search box's Rate and review and Share, for Store apps, aren't done).
      Files in results keep Open.
    - Glyphs (Segoe Fluent Icons 16, read from Explorer's menus): Move to front E1AA, Move left E64E, Move right
      E64D, Create a new app folder E8F4, Move to app folder E8DE, Remove from app folder E8DA, Pin E718, Unpin
      E77A, Run as administrator E7EF, Open file location ED43, App settings E713, Uninstall E74D; File Explorer's
      verbs and folder names have none. Items are WinUI's (28 tall from the mouse, 32 apart, separators 35), which
      measure as Explorer's: menus of the same items come out the same size to the pixel.
- **App catalog** (`AppCatalog`): enumerate `shell:AppsFolder` (`SHCreateItemFromParsingName` → `BHID_EnumItems`),
  reading display name and parent-relative parsing name — an AUMID, or a path such as `{KnownFolder}\app.exe` that
  is resolved with `SHGetKnownFolderPath` — and, for shortcuts, `System.Link.TargetParsingPath`. Shortcuts may give
  an app an AUMID its windows don't carry, so the pinned app keeps the target path to match those windows. Icons
  load only for items being shown. Reloaded in the background when Start opens if older than two minutes.
- **Search** — typing anywhere in the open menu focuses the search box:
  - Apps: ranked exact > prefix > every-word-starts-a-word > contains (case-insensitive, unit tested). Shown first,
    instantly.
  - Indexer: `CSearchManager` → catalog `SystemIndex` → `ISearchQueryHelper` (`QuerySelectColumns`,
    `QueryContentProperties` = `System.ItemNameDisplay` so words match names like the Windows search box,
    `QueryWhereRestrictions` = files only and no `.lnk`, prefix term expansion, `QueryMaxResults`),
    `GenerateSQLFromUserQuery`, executed with `OleDbConnection`
    (`Provider=Search.CollatorDSO;Extended Properties='Application=Windows'`). OLE DB needs built-in COM interop,
    which the app has (a file-based or AOT build would not).
    Columns: `System.ItemNameDisplay`, `System.ItemPathDisplay`, `System.Kind`. Results grouped as Documents,
    Folders, Other (Settings pages aren't in `SystemIndex`).
  - Runs on a background thread, 150 ms debounce, cancelled by the next keystroke; index failures leave app results.
  - Enter opens the selected result (the best match is selected); arrow keys move the selection.
- **Bottom row**:
  - The user: a flat 94 × 40 button (Explorer's `UserTileButton`, "User account for <name>", the name as tooltip),
    52 from Start's left edge, padding 12,4: picture 32, 12 apart, name (`GetUserNameEx(NameDisplay)`, else the
    account name) at 12 px. The picture is `HKLM\...\AccountPicture\Users\<SID>\Image<size>`, else Windows' grey
    silhouette (`%ProgramData%\Microsoft\User Account Pictures\user-32/40/48/192.png`, `user.png` above), as
    Explorer's (StartDocked.dll names `user.png`). It opens the [**Account menu**](account-and-power-menus.md#account-menu-startmenuwindow-accountflyout-interop-shellaccountmenu).
  - Switch to Explorer button (see [Switch to Explorer](lifecycle.md#switch-to-explorer)), shell mode only, behind a confirmation dialog that defaults to Cancel.
  - **Folders beside the power button** (Settings → Personalization → Start → Folders; Interop
    `Shell/StartPlaces`, unit tested), read each time Start opens: Explorer too shows a change the next time it
    opens. Settings keeps them in `HKCU\Software\Microsoft\Windows\CurrentVersion\Start\VisiblePlaces`, a
    REG_BINARY of 16-byte GUIDs in the order they were switched on (empty or missing: none). Start shows them in its
    own fixed order whatever the order in the value. The MDM policies
    `HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Start\AllowPinnedFolder<Name>` (0 hidden, 1 shown,
    65535 not set; the names are in `StartDocked.dll`) override the choice.

    | Folder | GUID | Glyph | Opens |
    |---|---|---|---|
    | Documents | `2D34D5CE-FA5A-4543-82F2-22E6EAF7773C` | E8A5 | `shell:Personal` |
    | Downloads | `E367B32F-89DE-4355-BFCE-61F37B18A937` | E896 | `shell:Downloads` |
    | Music | `B00B0620-7F51-4C32-AA1E-34CC547F7315` | EC4F | `shell:My Music` |
    | Pictures | `383F07A0-E80A-4C80-B05A-86DB845DBC4D` | EB9F | `shell:My Pictures` |
    | Videos | `42B3A5C5-7D86-42F4-80A4-93FACA7A88B5` | E714 | `shell:My Video` |
    | Network | `FE758144-080D-42AE-8BDA-34ED97B66394` | EC27 | `shell:NetworkPlacesFolder` |
    | Personal folder | `74BDB04A-F94A-4F68-8BD6-4398071DA8BC` | EC25 | `shell:UsersFilesFolder` (the profile) |
    | File Explorer | `148A24BC-D60C-4289-A080-6ED9BBA24882` | EC50 | as Win+E |
    | Settings | `52730886-51AA-4243-9F7B-2776584659D4` | E713 | `ms-settings:`; in shell mode Control Panel (`control.exe`), as Settings is a UWP app |

    Each is a flat 40 × 40 button with a 16 px glyph (Segoe Fluent Icons, matched against Explorer's pixels) and its
    name as tooltip. Explorer's buttons, the power button's too, touch, 40 apart, the power glyph's right edge 65 px
    from Start's. Right-clicking them gives "Personalise this list" (`ms-settings:personalization-start-places`;
    Control Panel in shell mode). NeoShell's Settings button, always shown before, is now this list's.
  - Power button menu: see [Power menus](account-and-power-menus.md#power-menus-poweritems-interop-shellpoweroptions-shellpower). Its items are filled as the menu opens. The power button hides
    when the `HidePowerButton` MDM policy is set (`PolicyManager\current\device\Start`). While Windows Update waits
    for a restart (`WNF_USO_REBOOT_REQUIRED` ≠ 0, the state StartDocked.dll's `PowerOptionsViewModel` subscribes
    to) and Shut down or Restart is offered, its glyph becomes F1B1 with the orange dot. That look is inferred from
    StartDocked's resources and wasn't seen live: the WNF state can't be published without SYSTEM. Its accessible name
    then is `PowerButtonDisplayNameWithUpdate`, a string not found in any resource file, so NeoShell keeps "Power".
  - **Not here on 25H2:** Sign out, and switching to another account, are in the **Account menu** on the user.
