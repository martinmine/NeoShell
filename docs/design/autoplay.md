# AutoPlay

AutoPlay for inserted media while NeoShell is the shell. Part of the [NeoShell design](../design.md).

## AutoPlay (shell mode only; `AutoPlay/`, Interop `Shell/AutoPlay*`, `VolumeArrivals`, `OpticalDrives`)

**Why NeoShell has its own.** The Shell Hardware Detection service only reports arrivals; the AutoPlay work runs in
the shell process. `windows.storage` registers for hardware notifications only in a process in explorer server mode 3
(Explorer's desktop process); on arrival `shell32!CMountPoint::DoAutorun` → `CAutoPlayParams::PromptUser` creates
twinui's `CAutoPlayUI` (`HKLM\...\Explorer\AutoplayExtensions\ShellUI`) in-process. Without Explorer nothing reacts.
Putting NeoShell in that mode (`windows.storage!SetExplorerServerMode(3)` + its change notification server, ordinal
1002) brings the arrival in, but twinui's toast fails with `E_ACCESSDENIED` (it needs `CreateWindowInBand` in the
notification band, Explorer only) and the prompt waits invisibly; so NeoShell reimplements it from shell32's and
twinui's code (Windows 11 25H2, read with symbols in Ghidra) and Explorer's behaviour, measured on this VM with ISOs
mounted by `Mount-DiskImage` (no admin needed; the drive is a DVD drive).

**Explorer's behaviour**
- **Toast.** Banner only (never in the notification center), app `Windows.SystemToast.AutoPlay`, named "AutoPlay"
  (twinui `-9914`) with no logo: the notification UI draws its default app glyph (Segoe Fluent `ECAA`). Title: the
  drive's name ("DVD Drive (F:) NEOPICS"); body: twinui `-9992` "Select what happens with %1." with shell32's
  description of the content (see below). It shows ~0.4 s after the volume, stays as long as other toasts (~6 s) and
  goes when the media is removed. Notifications off for AutoPlay (or Do not disturb) means no toast and no prompt.
  Clicking it opens the flyout; its close button or timing out drops the prompt.
- **Flyout** (twinui `CAutoPlayHandlerChooser`, the DirectUI chooser of Windows 8, class `Shell_Flyout`, topmost,
  takes the foreground): white whatever the theme, 387×(content) px with a 1 px `#CCCCCC` border, 5 px from the top
  right of the screen (the policy `DisplayToastAtBottom` moves it to the bottom), square, no shadow, appears and goes
  without animation. Inside (effective px): the drive's name in Segoe UI Light 20 pt in a 66 px band (wrapped in
  U+202A/U+202C), "Choose what to do with %1." (twinui `-9978`, Segoe UI Semilight 11 pt) on a 20 px line, then the
  list: 10 px above, 20 below, 60 px rows. A row: a 40×40 tile at (20, 10) behind the 32 px icon, in the immersive
  colour `ImmersiveStartDesktopTilesBackground` (`#0060B7` for accent `#0063B1`; a packaged handler with an
  `AppUserModelID` uses its tile colour), the action in black and the provider in `#666666` on 20 px lines from x 73.
  Hover: row `#DEDEDE`, provider black. Pressed: the row tilts in (Windows 8's pointer-down). Keyboard: focus starts on
  the choice picked last (`EventHandlersDefaultSelection`), else the first, shown (after a key) as 2 px black lines
  at the row's top and bottom; Up/Down and Tab move without wrapping, Enter chooses, Esc or a click elsewhere closes
  without saving anything. Removing the media closes it. Headers (Semibold 11 pt, a 40 px block):
  "Install or run program from your media" (twinui `-9926`) or "Run enhanced content" (`-9927`) over a disc's
  program, "Other choices" (`-9904`) over the rest. The list is `60n+30` px for n ≤ 5 rows, else 280 (four and a half
  rows, scrolling), plus 80 with headers (`_CalculateScrollViewerHeight`).
- Seen live: a data disc of any content (pictures, music, video, mixed, documents, empty) → "removable drives":
  Configure storage settings (Settings), Open folder to view files (File Explorer), Take no action. `VIDEO_TS` →
  "DVD films": Play DVD movie (VLC), Find a new DVD app (Store), Take no action (no Open folder). `autorun.inf` →
  "this disc", its `label=` as the drive's name, "Run NeoShell test" (its `action=`) / "Publisher not specified"
  under the program header, then Other choices. With "Choose what to do with each type of media" for removable drives:
  "pictures" → Import Photos and Videos (Photos); mixed → Play audio files (VLC), Play (Windows Media Player), Play
  video files (VLC), Import Photos and Videos, then the general choices, scrolling.

**What it does** (shell32 `CAutoPlayParams`, `CAutoplayContentHandler`, `CAutoplayHandler`; twinui `CAutoplayDialog`)
- Skipped when `HKCU\...\AutoplayHandlers\DisableAutoplay` = 1, the drive is barred by policy (`NoDriveTypeAutoRun`
  bit per drive type, `NoDriveAutoRun`/`NoDrives` bit per letter; network drives never), a full-screen game runs,
  the window in front answers the registered `QueryCancelAutoPlay` message (wParam drive index, lParam `ARCONTENT_*`)
  with non-zero, or an `IQueryCancelAutoPlay` in the running object table returns `S_FALSE` (3 s at most).
- **Content** (shell32's `CT_*` values): from the volume, as shsvcs' `_UpdateSpecialFilePresence` looks: on optical
  drives `video_ts\video_ts.ifo`/`dvd_rtav\vr_mangr.ifo` (DVD movie), `audio_ts\audio_ts.ifo` (DVD audio),
  `VCD\entries.vcd`, `SVCD\entries.sv[cd]`, `BDMV`/`BDAV` on UDF (Blu-ray), audio tracks; on any drive `DCIM`, `AVCHD`,
  `PRIVATE\AVCHD` (memory card); `autorun.inf` with `open=`/`shellexecute=` on optical drives (not with policy
  `NoAutorun` = 1). Autorun with audio or a film is an enhanced CD/DVD. Then `CAutoPlayParams::Init`: unless the user's
  choice for `StorageOnArrival` is `MSUseAdvancedStorageOptions`, nothing else found means a **removable drive**
  (`StorageOnArrival`), whatever the files; with it, the files are walked (4 levels) and perceived types give music /
  pictures / videos, several = mixed, none = unknown content. Event names and descriptions are shell32's table
  (`AutorunINFLegacyArrival` "this disc", `PlayDVDMovieOnArrival` "DVD films", `StorageOnArrival` "removable drives",
  memory cards `ShowPicturesOnArrival` with choices under `CameraAlternate`…).
- **Saved choice**: `HKCU\...\AutoplayHandlers\UserChosenExecuteHandlers\[CameraAlternate\]<event>` (default value).
  None or `MSPromptEachTime` asks; `MSTakeNoAction` does nothing; a handler that still exists runs without a toast
  (also written to `EventHandlersDefaultSelection`); policy `NoAutorun` = 2 runs a disc's program. Not implemented:
  twinui's "You have new choices" prompt when a handler was installed after the choice was saved.
- **Choices**: `EventHandlers\<event>` value names, HKCU then HKLM, each read from `Handlers\<name>` (HKCU first; a
  user's needs `InvokeProgID`+`InvokeVerb`): `Action`, `Provider` (none for `MSTakeNoAction`, `MSPromptEachTime`,
  `MSAutoRun`), `DefaultIcon` (none: the drive's icon), resource strings resolved by `SHLoadIndirectString`. Each
  list is newest first by the `Handlers\<name>` key's last write time (`CAutoplayHandlerList::Add` inserts before the
  first older one), a name once. Groups in order: a disc's program; the content's choices (mixed content: each kind's,
  never `MixedContentOnArrival`'s; memory cards add the video ones; blank media add Take no action); the general
  ones, `UnknownContentOnArrival` + Take no action. Media discs (audio CD, DVD, VCD, Blu-ray) drop Open folder. With
  mixed content twinui adds each kind's choices as the walk finds that kind, after those listed, so the order follows
  the files (on this VM: audio VLC, Media Player, video VLC, Photos). No prompt unless there's more than taking no
  action.
- **Choosing**: `EventHandlersDefaultSelection\<event>` = the choice; `UserChosenExecuteHandlers\<event>` = the
  choice, except for a disc's program, mixed or unknown content and when the setting was "Ask me every time", where
  it's set to `MSPromptEachTime` (both confirmed in Explorer). Then it runs, on a thread of its own: `InvokeProgID` +
  `InvokeVerb` on the drive's root (`ShellExecuteEx` with the class; `Folder`/`open` opens the drive, VLC's
  `VLC.OPENFolder` plays it), a `CLSID` handler through `IHWEventHandler(2)` (`Initialize(InitCmdLine)`, then the
  drive, "" and "DeviceArrival", as `CAutoplayHandler::Invoke`), the disc's program from the drive's root.
- **Drive names.** Outside Explorer's process the shell can't use the hardware service's volume data and calls every
  optical drive "CD Drive" and ignores `autorun.inf`'s label. NeoShell asks the drive its MMC features
  (`IOCTL_CDROM_GET_CONFIGURATION`, allowed to users) as shsvcs' `_UpdateMMC2CDInfo` and names it as
  `CMtPtLocal::_GetCDROMName` (BD-RE/BD-R/BD-ROM, DVD RW/R, DVD/CD-RW, DVD/CD-R, DVD RAM, DVD, CD-RW, CD-R, CD Drive;
  `windows.storage.dll` strings), and puts the autorun label in place of the volume's.

**NeoShell.** `VolumeAutoPlay` (created by `ShellSession`) listens for `WM_DEVICECHANGE` volume arrivals and removals
on a hidden top-level window (broadcasts don't reach message-only windows), works the content and choices out off the
UI thread (`AutoPlayRules`, unit tested), shows the toast through `ToastPopups.ShowBanner` and the flyout
(`AutoPlayFlyout` + `AutoPlayChooser`). Compared side by side with Explorer at 100 %: flyout bounds identical
(1372,5–1759,303; with headers to 383; mixed to 373), text rows identical, columns within 1 px (WinUI lays text out
with fractional advances; `CharacterSpacing` 9 and 5 make up DirectUI's whole-pixel widths), colours, hover, focus
lines, keys, registry writes and toasts the same. Differences: the pressed tilt is WinUI's
`PointerDownThemeAnimation`; Explorer keeps the row grey when the pointer leaves it pressed; the scroll bar is WinUI's.
Not run live (no hardware or admin on the VM): USB sticks and memory cards (a VHD needs admin to attach), audio CDs,
Blu-ray, VCD and blank discs (blank media isn't detected at all: it needs IMAPI), `CLSID` handlers (none registered
for volumes here), packaged handlers' tile colours, WPD devices (phones, cameras over MTP), which Explorer handles
through the hardware service's device events, not volumes.
