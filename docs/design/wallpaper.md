# Wallpaper

The wallpaper windows, Explorer's wallpaper state, the slideshow, `IDesktopWallpaper` and Windows spotlight. Part of
the [NeoShell design](../design.md).

## Wallpaper (`Desktop/`)

- One `WallpaperWindow` per monitor covering the full monitor bounds, kept there and at `HWND_BOTTOM` by
  `BottomWindow` (rewrites `WM_WINDOWPOSCHANGING`).
- **New windows of apps without the foreground** (T40). Windows shows the window of an app that may not take the
  foreground (started by a scheduled task, by a background process) just below the lowest visible window of the
  thread in front (win32kfull's `CalcForegroundInsertAfter`: from the last topmost window down, the last window of
  the foreground queue's active thread; `HWND_TOP` from such a thread goes there too). With NeoShell in front (its
  taskbar or Start clicked) that's the wallpaper, a window of the same thread, so the new window opened below it, out
  of sight, just above the hidden shell window (the very bottom). Explorer's desktop has a thread of its own: with its
  taskbar in front the window opens on top of the other windows, below the topmost ones (measured: Character Map
  started through a scheduled task). So `Wallpaper` watches windows being shown (`EVENT_OBJECT_SHOW`) and puts one
  that's below a wallpaper window just below the last topmost window (`TopLevelWindows.BringAboveOthers`; an explicit
  window to go after isn't redirected as `HWND_TOP` is). With another app in front, the window opens behind it under
  both shells.
- Frameless through `FramelessWindow`: a borderless `OverlappedPresenter` still keeps `WS_DLGFRAME` and restores it on
  every style change, so `WM_STYLECHANGING` strips the frame and isn't passed on; DWM border and rounded corners off.
- Not `AppWindow.IsShownInSwitchers`: it goes through the taskbar and throws when there is none; `WS_EX_TOOLWINDOW`
  keeps the window out of Alt+Tab instead.
- Reads `HKCU\Control Panel\Desktop`: `Wallpaper`, `WallpaperStyle`, `TileWallpaper`, and
  `HKCU\Control Panel\Colors\Background` for the fill colour; a picture per monitor from Explorer's per-monitor values
  (below).
- Layout (unit tested): `WallpaperLayout.Arrange` returns the image rectangles in physical pixels for each style —
  Fill, Fit, Stretch, Center (exact pixels), Tile (one rectangle per tile; WinUI has no tiled brush) and Span (Fill
  over the virtual screen, offset per monitor). The window places `Image` elements on a `Canvas` at those rectangles.
- Pictures are decoded before they're shown (`Pictures.DecodeAsync`, WinRT `BitmapDecoder` on the thread pool, at the
  size drawn, upright by EXIF) into a `WriteableBitmap`: a `BitmapImage` decodes only once drawn (not at all while
  hidden or at opacity 0) and tells nobody when it's done, so a change would show a half-drawn picture and a crossfade
  would be half over before the picture appears.
- Any `WM_SETTINGCHANGE` or `WM_SYSCOLORCHANGE` re-reads the settings and reloads only if path, style, colour,
  per-monitor pictures or the file's timestamp changed; `WM_DISPLAYCHANGE` recreates the windows. Bursts of broadcasts
  become one update.
- Shell mode only.

### Explorer's wallpaper state (`WallpaperRegistry`, `SlideshowSettings`)

Everything below is stored where and as Explorer stores it, so either shell takes over the other's wallpaper,
per-monitor pictures and slideshow (checked both ways: NeoShell continues Explorer's slideshow from its current
picture, Explorer continues NeoShell's and shows NeoShell's per-monitor picture).

- **Image cache values.** `TranscodedImageCache` describes the picture Explorer last applied; with a picture per
  monitor (or a slideshow) each monitor has `TranscodedImageCache_000`, `_001`… and `LastUpdated` is the index changed
  last (`0xFFFFFFFF` = all monitors). 800 bytes: magic `0x0001C37A`, the source's file size, width, height (DWORDs),
  its write time (FILETIME), the source path at 24 (260 WCHARs), the monitor's device path at 544 (128 WCHARs; empty
  in the all-monitors value). The device path is the monitor's interface path (`EnumDisplayDevices` with
  `EDD_GET_DEVICE_INTERFACE_NAME`), as `IDesktopWallpaper::GetMonitorDevicePathAt` returns it.
- **Transcoded copies.** Explorer draws `%APPDATA%\Microsoft\Windows\Themes\Transcoded_000`… and `TranscodedWallpaper`,
  not the source; in per-monitor mode `Wallpaper` names `TranscodedWallpaper`. On start it trusts a value whose header
  matches the source (it then draws the copy, whatever is in it) and drops one that doesn't (zero header: per-monitor
  values deleted, `TranscodedWallpaper` drawn everywhere). So when NeoShell sets pictures per monitor it writes the
  values with real headers and copies each source over its `Transcoded_00N` (Explorer decodes any format it supports;
  a straight copy shows correctly), the last one also over `TranscodedWallpaper`, then sets `Wallpaper` to that path
  through `SPI_SETDESKWALLPAPER`. One picture for all monitors goes straight through `SPI_SETDESKWALLPAPER` with the
  source path (per-monitor values deleted, `LastUpdated` = all), which Explorer transcodes itself when it next runs.
  NeoShell reads `Wallpaper` = `TranscodedWallpaper` as the source in `TranscodedImageCache`.
- **Slideshow.** `slideshow.ini` (hidden; replaced files can't be, so it's rewritten in place) has `[Slideshow]`
  `ImagesRootPIDL=` (the folder) and, for a set of files, `Item0=`, `Item1=`… (ID lists relative to the folder); an
  empty file means no slideshow. Each value is `ILSaveToStream`'s output (2-byte size, the ID list) in shell32's
  private-profile Base64: standard alphabet, but each character's six bits are taken lowest first and fill bytes
  lowest bit first (unit tested against Explorer's file). `ImagesRoot=` (a path) is shell32's fallback. Options in
  `HKCU\Control Panel\Personalization\Desktop Slideshow`: `Interval` (ms; default 1 800 000, at least 10 000),
  `Shuffle`, `LastTickHigh`/`LastTickLow` (FILETIME of the last change, written only for intervals ≥ 5 minutes),
  `AnimationDuration` (HKCU or HKLM; default 1000 ms, under 250 → 250), `Flags` (bit 2: don't align to midnight).

### Slideshow (`Slideshow`)

shell32's `CSlideshowWorker`, read with its PDB, runs it inside Explorer's desktop; without Explorer nothing advanced.
NeoShell does the same:

- **Pictures.** The folder's files (not subfolders: shell32 walks depth 0, no hidden files) with shell32's 23
  extensions (`.jpg .jpeg .bmp .dib .png .gif .jfif .jpe .tif .tiff .wdp .heic .heif .heics .heifs .hif .avci .avcs
  .avif .avifs .jxr .jxl .webp`) in view order (`StrCmpLogicalW` by name), or the given files in their order.
- **Order** (`SlideshowQueue`, unit tested). In turn: the pictures after the current one, then from the start leaving
  out the current one. Shuffled: every picture but the current one in random order, the current one put back at a
  random place other than first; a round lasts until all were shown. Each monitor gets the picture after the previous
  monitor's (the first, after the last monitor's current one).
- **Timing** (`SlideshowSettings.NextChange`, unit tested). Changes fall on whole intervals since midnight UTC
  (`_RoundNextTick`: the delay minus the time since the last boundary, plus an interval when that's under 4 s). From
  5-minute intervals up, on start the delay counts from `LastTick` (straight away when overdue or never changed). On
  start a current picture that isn't in the slideshow is replaced at once. Measured with Explorer and NeoShell at a
  10-second interval: changes at :00, :10, :20…
- **Paused** when the power plan says so for the current power source (Power Options → Desktop background settings →
  Slide show, `0d7dbae2…`/`309dce9b…`; Settings' "run on battery" off sets DC to Paused), checked at each change.
  Explorer also pauses while the display is off and refreshes a minute after it's back; NeoShell doesn't (nothing
  shows then).
- **Crossfade.** Explorer fades each slideshow change linearly over `AnimationDuration` (1000 ms): measured at 60 fps on
  a red→blue slideshow, R+B constant and the mix linear in time, 1.0 s, starting within ~80 ms of the boundary.
  NeoShell: the new picture goes under the old one and the old one fades out (Opacity 1→0, linear, same duration) —
  identical on screen (same measurement: linear, 1.03 s), starting ~0.2-0.3 s after the boundary once the new picture
  is decoded. XAML fades each element separately, so the old canvas's background is cleared first or it shows through
  the fading picture. Every other change (`SetWallpaper`, a new fit, a new colour) is instant, in Explorer too.

### `IDesktopWallpaper` (`WallpaperService`, `DesktopWallpaperServer`)

`CLSID_DesktopWallpaper` `{C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD}` has only an AppID (`RunAs` Interactive User) and no
server on disk: Explorer's desktop (shell32's `CDesktopWallpaper::RegisterClassObject`) registers the class object
while it runs. Without Explorer every `CoCreateInstance` failed with `REGDB_E_CLASSNOTREG` — Settings' Background page,
"Set as desktop background" (shell32's verb, which runs in the calling process and calls the API) and apps all broke.
shell32's object can't be created outside Explorer's desktop (`DllGetClassObject` of shell32 refuses the CLSID;
themeui's answers with an unrelated class), so as the shell NeoShell registers its own class object
(`CoRegisterClassObject`, `CLSCTX_LOCAL_SERVER`, `REGCLS_MULTIPLEUSE`, on the UI thread so calls arrive there) and
answers as Explorer does — compared call by call with a test client against both:

- `SetWallpaper(NULL, path)`: one picture everywhere, ends the slideshow (`slideshow.ini` emptied), adds it to
  `Explorer\Wallpapers\BackgroundHistoryPath0…4` (Settings' recent pictures). A missing file →
  `0x80070002`; `""` → no picture. With a monitor's path: that monitor only (per-monitor values, above); a monitor that
  doesn't exist → `S_OK`, nothing changed.
- `GetWallpaper`: per monitor, or with `NULL` the picture all monitors share, `""` when they differ and always during
  a slideshow; an unknown monitor → the common picture.
- `GetMonitorDevicePathAt` (out of range `E_FAIL`), `GetMonitorDevicePathCount`, `GetMonitorRECT` (the monitor's
  bounds; unknown monitor `E_INVALIDARG`).
- `SetBackgroundColor`: `SetSysColors(COLOR_BACKGROUND)` and `Colors\Background` = `"R G B"`.
- `SetPosition`: Center 0/0, Tile 0/1, Stretch 2/0, Fit 6/0, Fill 10/0, Span 22/0 (`WallpaperStyle`/`TileWallpaper`);
  other values `E_INVALIDARG`.
- `SetSlideshow` (a folder, or files in one folder; `NULL` → `S_OK`, nothing), `GetSlideshow` (an empty array when
  there is none), `Set`/`GetSlideshowOptions` (`DSO_SHUFFLEIMAGES`, interval in ms, stored unclamped as Explorer does).
- `AdvanceSlideshow(NULL, DSD_FORWARD)` changes now (crossfaded); a monitor or `DSD_BACKWARD` → `E_NOTIMPL`; no
  slideshow → `E_UNEXPECTED`.
- `GetStatus`: `DSS_ENABLED` while there is a picture, `DSS_SLIDESHOW` while a slideshow runs. `Enable(FALSE)` clears
  `Wallpaper` (and the per-monitor values); `Enable(TRUE)` sets it back to `TranscodedWallpaper` (`0x80070002` if gone).
- Explorer's `SetWallpaper` doesn't turn Windows spotlight off (Settings does that itself), so neither does NeoShell's.
- Differs: Explorer keeps some state in memory — `SetWallpaper(NULL, "")` leaves its status `DSS_ENABLED`
  (NeoShell's reads the registry: 0), `GetSlideshowOptions` echoes undefined option bits, `GetBackgroundColor` echoes
  the high byte it was given. Settings' Background page itself doesn't run in shell mode (`ms-settings:` fails), and
  its private `IDesktopWallpaperPrivate` calls (Spotlight, previews) aren't served.

### Windows spotlight

Explorer 25H2: Settings writes `Explorer\Wallpapers\BackgroundType` = 3 and `DesktopSpotlight\Settings\EnabledState` =
1; the pictures come from the CBS package's DesktopSpotlight component (a background task fetches them, VM online) and
Explorer sets them through its private `IDesktopWallpaperPrivate`. Explorer adds the "Learn about this picture" icon
(`Explorer\Desktop\NameSpace\{2cc5ca98-6485-489a-920e-b3e88a6ccce3}` and its `HKCU\Software\Classes\CLSID` key, with an
`InfoTip` and verbs whose `DelegateExecute` object Explorer registers at run time); clicking it opens a flyout (a
WinAppSDK XAML island in explorer.exe: three pictures to pick from, title, Learn more, like/dislike), and Next goes
through `WindowsUdk.UI.Shell.DesktopSpotlight.DesktopSpotlightExtension` (an app extension of the CBS package,
`ActiveWallpaperIndex` + 1). As the shell NeoShell shows the current Spotlight picture (it's an ordinary
`Wallpaper` path) but none of the rest: the flyout, the icon's commands and the rotation all live in Explorer or go
through private WinRT and COM interfaces with no public contract.
