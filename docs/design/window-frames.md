# Window frames

Restyling other applications' title bars and frames: their backdrop (Mica, Mica Alt, Acrylic, a solid colour),
colours, corners and borders, globally and per app, with presets that recall older Windows (XP's Luna, Vista's Aero
glass) and styles of the user's own. Part of the [NeoShell design](../design.md).

Status: **prototype built** (option A + B, title-bar-prototype branch, 2026-10-09; see
[The prototype as built](#the-prototype-as-built)). The findings below were measured on this VM (Windows 11 25H2,
build 26200) unless a source is named.

## What Windows lets another process change

DWM keeps a set of attributes per top-level window. They are documented for a window's own app, but **DWM accepts
them from any process of the same user**: NeoShell can set them on other apps' windows with no injection and no
elevation. Measured with a WinForms test window in one medium-IL process and the calls from another medium-IL
process, each result screenshotted (`CopyFromScreen` over the frame):

| Call | Result from another process | Readable back (`DwmGetWindowAttribute`) |
|---|---|---|
| `DWMWA_USE_IMMERSIVE_DARK_MODE` (20) | Works: dark frame and caption buttons | Yes |
| `DWMWA_SYSTEMBACKDROP_TYPE` (38): `DWMSBT_AUTO` 0, `NONE` 1, `MAINWINDOW` 2 (Mica), `TRANSIENTWINDOW` 3 (Acrylic), `TABBEDWINDOW` 4 (Mica Alt) | Works on the title bar and frame: Mica tinted by the wallpaper, Acrylic blurring what's behind, Mica Alt; light or dark per attribute 20 | Yes |
| `DWMWA_CAPTION_COLOR` (35), `DWMWA_TEXT_COLOR` (36), `DWMWA_BORDER_COLOR` (34), `COLORREF` (0x00BBGGRR) | Works. A caption colour is opaque and wins over the backdrop; `DWMWA_COLOR_NONE` (0xFFFFFFFE) on the border removes it; `DWMWA_COLOR_DEFAULT` (0xFFFFFFFF) puts Windows' colour back | **No**: `E_INVALIDARG`, so an app's own colour can't be saved before it's replaced |
| `DWMWA_WINDOW_CORNER_PREFERENCE` (33): default 0, square 1, round 2, small round 3 | Works | Yes |
| `DWMWA_NCRENDERING_POLICY` (2) = `DWMNCRP_DISABLED` | Works: DWM stops drawing the frame and the app's `DefWindowProc` draws a uxtheme frame instead, from `aero.msstyles`' basic parts: a pale blue gradient frame with Windows 7 style caption buttons ("Windows 7 Basic"). The title text's glow comes out garbled over it. `DWMNCRP_ENABLED` (2) or `USEWINDOWSTYLE` (0) brings DWM's frame back | No |
| `DwmExtendFrameIntoClientArea` (-1 margins), `DwmEnableBlurBehindWindow` | Accepted (`S_OK`); only visible where the app leaves its client area transparent, which GDI apps don't | — |
| `SetWindowCompositionAttribute` (`WCA_ACCENT_POLICY`, private user32 export): blur (3), acrylic (4) with a gradient colour | Accepted; changes nothing on a standard title bar (applies behind the client area only) | — |
| `DWMWA_MICA_EFFECT` (1029, Windows 11 21H2's undocumented Mica) | `E_INVALIDARG`: gone in 22H2 and later | — |

Also found:

- Mica and Acrylic show only while the window is active (or Windows thinks it is); an inactive window falls back to
  a solid colour (Mica: the plain title bar colour; Acrylic: a flat grey). Vista's glass stayed glassy when inactive.
  A `WM_NCACTIVATE(TRUE)` sent to the window shows the active look, but it lies to the app about activation and must
  not be used.
- The attributes stay on the window until it's destroyed or someone sets them again. If NeoShell exits or crashes
  the windows keep their look, which is harmless; NeoShell resets them when the feature is turned off.
- A medium-IL NeoShell styling an **elevated** window (Task Manager): DWM refuses `DwmSetWindowAttribute` (measured
  with the prototype), so such windows keep their look; the prototype logs it once and skips the window.
- On 25H2 a standard Win32 title bar already has Mica by default (`DWMSBT_AUTO`): setting `DWMSBT_MAINWINDOW` changes
  nothing measurable (the same pixels, active and inactive, light and dark). Mica Alt is visibly more tinted.
- An app that sets its own dark mode in `WM_ACTIVATE` does so *after* the out-of-context foreground event reaches
  NeoShell, so restyling on that event alone loses the race; restyling again 250 ms later wins (measured with a test
  window that resets itself to light on every activation).
- Windows whose apps draw their own title bar (Chromium and Electron apps, Edge, Office, Visual Studio, Windows
  Terminal, WinUI and UWP apps with `ExtendsContentIntoTitleBar`, Task Manager) keep their look: only the border
  colour and corners reach them, and a backdrop only where they paint nothing. Classic Win32, WinForms and WPF windows
  with the standard frame (Notepad's legacy dialogs, regedit, mmc consoles, Control Panel applets, most installers and
  older tools) take everything.
- Apps that set these attributes themselves (dark mode on a theme change is common) overwrite NeoShell's, with no
  notification.

### What no public call can do

- Tint or blend a system backdrop with a custom colour, or change its blur radius or opacity (Mica and Acrylic follow
  only light/dark). A tinted glass means an opaque caption colour or nothing.
- Change the title bar's height, font, text glow, text alignment, the caption buttons' look or size, or the frame's
  thickness (Windows 11's resize borders are invisible; Vista's and XP's were drawn, 4-8 px).
- Draw anything (gradients, images) in the title bar.

These need either code inside DWM (see DWMBlurGlass below), code inside every app (WindowBlinds), or NeoShell's own
windows over the title bars (see [Options](#options)).

## Prior art

- **[DWMBlurGlass](https://github.com/maplespe/DWMBlurGlass)** (Maplespe; LGPL-3.0; Windows 10 2004 to current
  Windows 11, not Insider builds). Read from its source:
  - `DWMBlurGlass.exe` (UI and host, MiaoUI) installs a scheduled task at logon with the highest run level
    (`TASK_RUNLEVEL_HIGHEST`, so admin), which runs `DWMBlurGlass.exe runhost`. The host downloads Microsoft's
    symbols for `dwmcore.dll` and `uDWM.dll` (DbgHelp, `SRV*<app>\data\symbols`), resolves the private functions it
    hooks to offsets, enables `SeDebugPrivilege` and injects `DWMBlurGlassExt.dll` into `dwm.exe` with
    `CreateRemoteThread(LoadLibraryW)`. It watches for a new `dwm.exe` (DWM restarts) and injects again. After a
    Windows update the symbols must be downloaded again.
  - The extension hooks (minhook) about 120 private DWM functions: `CTopLevelWindow::UpdateNCAreaBackground`,
    `UpdateSystemBackdropVisual`, `CalculateBackgroundType`, `GetBorderMargins`, `UpdateText`,
    `UpdateNCAreaButton`, `CButton::RedrawVisual`, `CText::SetColor`, `CGlassColorizationParameters::...`,
    `CCustomBlur::*`, `CVisual::*` and others. It talks to its host through a message-only window
    (`MDWMBlurGlassExtNotify`) and reloads `data\config.ini` on `WM_APP + 20`.
  - Effects: blur, Aero (Windows 7's glass recipe rebuilt with `Windows.UI.Composition` effects: a Gaussian blur of
    the backdrop with an exposure "blur balance", multiplied by the tint at a "colour balance" opacity, plus the tint
    composited over at an "afterglow balance" opacity; Windows 7's reflection texture `AeroPeek.png` and parallax),
    Acrylic, Mica, Mica Alt. Three methods: its own blur inside DWM ("CustomBlur"), DWM's accent blur, or simply
    `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` called from inside DWM on every window with a frame (the same
    as the public route above).
  - Options: blend colour per active/inactive and light/dark, text colour, blur amount, title text glow, Windows 7
    caption button height and glow, border extension (thick frames), all **global**: no per-app settings.
  - Risk: a bug in the extension crashes `dwm.exe`; Windows restarts it (the screen blinks), and the host injects
    again.
- **[OpenGlass](https://github.com/ALTaleX531/OpenGlass)** (ALTaleX531, who wrote DWMBlurGlass' blur and Aero
  recipe): the same approach (a DLL injected into DWM by a helper started from a scheduled task), aimed at a faithful
  Windows 7 glass; "advanced users only", no Insider builds. Also by him: AcrylicEverywhere (DWMBlurGlass' custom
  blur), TranslucentFlyouts (menus).
- **Aero Glass for Win8.1+** (bigmuscle, closed source): `DWMGlass.dll` loaded into DWM by a scheduled task, with
  symbol patterns per DWM build; last release 1.5.11 for Windows 10 1809, dead since Windows 10 2004.
- **[MicaForEveryone](https://github.com/MicaForEveryone/MicaForEveryone)** (MIT; 2.x is a WinUI 3 C# app):
  **the public route**, as measured above. An out-of-context `SetWinEventHook(EVENT_OBJECT_SHOW)` and `EnumWindows`
  at start; a filter (visible, top-level, not `WS_EX_NOACTIVATE`/`WS_EX_TRANSPARENT`, tool windows and popups only
  with `WS_BORDER | WS_DLGFRAME`); rules global, per process name and per window class (most specific wins), each
  with title bar colour mode (system/light/dark/custom), backdrop, corners, "extend frame into client area" and
  "blur behind" (`DwmEnableBlurBehindWindow` plus an accent policy). No injection, no admin.
- **Windhawk** mods (injected per process; "inject into critical system processes" for `dwm.exe`): Bring Back the
  Borders, Restore Windows 7 Caption Buttons, Center Titlebar (port of WinCenterTitle), Disable rounded corners,
  DWM Ghost Mods, Translucent Windows (system backdrop and accent blur). Each hooks private DWM functions found by
  symbols, as DWMBlurGlass.
- **BasicThemer / BasicThemer2** and Windhawk's Classic Theme Windows: `DWMNCRP_DISABLED` per window so uxtheme (or
  the classic theme) draws the frame, as measured above; known glitches with transparent title bars and text.
- **Visual styles**: a Luna port as an `.msstyles` (community ports exist for Windows 10/11) draws a real XP frame
  through uxtheme, with `DWMNCRP_DISABLED`; but Windows loads only Microsoft-signed styles without SecureUxTheme or
  UltraUXThemePatcher (which patch or hook the theme loader), and the style is system-wide, not per app.
- **WindowBlinds** (Stardock, commercial): injects into every process and paints the non-client area itself; per-app
  skins. The only per-app XP/Vista look on Windows 11, at the cost of being inside every app.

## Options

| | How | Per app | XP / Vista look | Cost and risk | Fits NeoShell's rules |
|---|---|---|---|---|---|
| A. DWM attributes | `DwmSetWindowAttribute` on other windows, as MicaForEveryone | Yes | Approximate only (colours, corners, backdrop) | Low; nothing runs inside other processes | Yes: `dwmapi.dll`, already used |
| B. Basic frame | A plus `DWMNCRP_DISABLED`: uxtheme draws the frame inside the app | Yes | Windows 7 Basic look for free; XP only with a patched theme loader | Text glitches; app-dependent | Yes (B without the theme patch) |
| C. Overlay windows | NeoShell's own click-through windows owned by each app window, over its title bar, drawing the style (WinUI with `DesktopAcrylicController` tint for glass, or images); positions from `WindowEvents.LocationChanged` | Yes | Close (text, buttons, gradients, tinted glass); caption buttons drawn over the real ones (`DWMWA_CAPTION_BUTTON_BOUNDS`) | Lags a frame or two behind a dragged window (out-of-context events), so it must hide during moves; hover states need the pointer polled; the real buttons' Snap flyout still opens; many windows | Yes, but fragile; prototype only behind a flag |
| D. DWM extension | A native DLL injected into `dwm.exe` hooking `uDWM.dll` by symbols (DWMBlurGlass/OpenGlass) | Possible (the extension sees each window's process) | Faithful glass, glow, Win7 buttons, thick frames | Admin at every logon, `SeDebugPrivilege`, a crash blinks the screen, breaks with Windows updates, symbol downloads | **No**: native C++ code and minhook, not hand-written C# interop; needs the owner's approval |
| E. Bundle DWMBlurGlass | Ship or detect DWMBlurGlass and write its `config.ini` from NeoShell's settings | No (global only) | Its Aero preset | As D, and its updates are someone else's | Needs approval (a third-party native component) |
| F. Per-process skinning | A global hook DLL (32- and 64-bit) subclassing every window's `WM_NCPAINT`, as WindowBlinds | Yes | Faithful | Inside every app; crashes take apps down; anti-cheat and protected processes | No |

**Recommendation:** build the prototype on A (and B as one more preset), with the rules, presets and editor below,
and treat the XP and Vista presets honestly as approximations. Try C as a separate spike only if the owner wants the
real looks without injection, measuring the lag first. D/E are the only route to faithful Aero glass and need the
owner's decision (a native component injected into DWM, run elevated at logon).

## Prototype spec (option A + B)

### What the user sets

- **Window frames** on or off (off by default: NeoShell never changes other apps unasked).
- A **global style** and **app rules**: a rule matches by process name (`notepad.exe`), optionally narrowed by window
  class; the most specific matching rule wins, then the global style. A rule can also say "leave alone".
- A **style** is a named set of optional values (unset = Windows' own):

  ```
  FrameStyle
    Name
    Backdrop        Default | None | Mica | MicaAlt | Acrylic      (DWMWA_SYSTEMBACKDROP_TYPE)
    Theme           Default | Light | Dark                         (DWMWA_USE_IMMERSIVE_DARK_MODE)
    CaptionColor    colour (opaque; replaces the backdrop)         (DWMWA_CAPTION_COLOR)
    TextColor       colour                                         (DWMWA_TEXT_COLOR)
    BorderColor     colour | None | Accent                         (DWMWA_BORDER_COLOR)
    Corners         Default | Square | Round | SmallRound          (DWMWA_WINDOW_CORNER_PREFERENCE)
    BasicFrame      bool: uxtheme draws the frame                  (DWMWA_NCRENDERING_POLICY)
  ```

- **Presets** (read-only styles; "Duplicate" makes an editable copy):

  | Preset | Values | What it looks like |
  |---|---|---|
  | Windows default | nothing set | Windows 11 |
  | Mica | Backdrop Mica | Windows 11's Mica on every frame |
  | Mica Alt | Backdrop MicaAlt | |
  | Acrylic | Backdrop Acrylic | Blurred see-through frame while active |
  | Dark / Light | Theme | |
  | Accent | CaptionColor and BorderColor from the accent colour (`ImmersiveColors`), text white or black by contrast | Windows 10's "accent colour on title bars", per app |
  | Glass (Vista-like) | Backdrop Acrylic, Theme Dark, BorderColor Accent, Corners Default | The nearest public look to Aero: blurred and translucent, but grey when inactive, no tint, glow or Vista buttons |
  | Luna (XP-like) | CaptionColor #0054E3, TextColor white, BorderColor #0831D9, Corners SmallRound | XP's blue, flat: no gradient, Trebuchet title, 25 px caption or Luna buttons |
  | Windows 7 Basic | BasicFrame | The uxtheme basic frame and Windows 7 caption buttons; title text glow glitches |

  (Luna's colours are from XP's `luna.msstyles` blue scheme; to be checked against a reference screenshot when built.)

- **Custom styles**: the same editor as a preset's copy, with a live preview on a NeoShell test window.

Settings (`ShellSettings`): `WindowFramesEnabled` (bool), `WindowFrameStyle` (the global style's name),
`WindowFrameStyles` (the user's styles), `WindowFrameRules` (process, class, style name or "leave alone").

### How it's built

- `NeoShell.Interop/Windowing/WindowFrame.cs`: `Apply(nint hwnd, FrameAttributes)` and `Reset(nint hwnd)` with
  .NET types (colours as `uint` ARGB converted to `COLORREF` inside), returning whether DWM accepted it; the
  attribute constants join `Dwmapi.cs`. `Reset` sets each attribute this feature touched back to its default
  (`DWMSBT_AUTO`, `DWMWA_COLOR_DEFAULT`, corners 0, `DWMNCRP_USEWINDOWSTYLE`) and dark mode to what it read before
  applying (colours can't be read back, so an app's own caption colour is lost until it sets it again: rules should
  "leave alone" such apps).
- `NeoShell/Frames/WindowFrames.cs` (new folder), created by `App` when enabled, in both run modes:
  - Pure logic, unit tested: rule matching (`FrameRules.StyleFor(process, class)`), eligibility
    (`FrameRules.IsEligible(style, exStyle, ...)`, MicaForEveryone's filter: visible, top-level, has a caption,
    not `WS_EX_NOACTIVATE`/`WS_EX_TRANSPARENT`, not a tool window or popup without `WS_CAPTION`, not NeoShell's own
    windows, not cloaked), and style to attributes.
  - Applies to every window at start (`TopLevelWindows`), to new ones on `WindowEvents.Shown`, and again on
    `WindowEvents.Foreground` (cheap, catches apps that reset their own attributes on activation or theme change).
    The work runs on the UI thread through `WindowEvents` (the calls are fast, one DWM ALPC each); a window whose
    call fails (elevated, gone) is remembered and skipped.
  - On a settings change: re-applies to all windows (reset first where a value went back to "unset").
  - On turning the feature off and on `/exit`: `Reset` on every window it styled. Not on a crash: the looks stay
    until the windows close, which is harmless.
- UI: a "Window frames" page in NeoShell's settings (or, until there is a settings window, a window opened from the
  taskbar's context menu): on/off, the global style, the rule list (add from running apps, as MicaForEveryone), the
  style editor with presets, AutomationIds on every control.
- `docs/architecture.md`: `dwmapi.dll`'s row gains "`DwmSetWindowAttribute` on other apps' windows (backdrop, dark
  mode, caption/text/border colour, corners, non-client rendering)"; no new DLL.

### Testing

- Unit tests: rule priority, eligibility, style to attributes, colour conversion, reset sets.
- Live: WinForms test windows of Claude's own (as this research did; never the owner's apps), screenshots of the frame
  with `CopyFromScreen` before and after each style, active and inactive, light and dark, over a striped background
  to see the blur; a test window that sets its own dark mode on activation, to check re-applying; a custom-title-bar
  app (Windows Terminal) to check that only border and corners change; an elevated window (Task Manager) to check
  that a refused call is skipped, not retried in a loop.

## The prototype as built

Option A + B as specified above; no injection, no new packages, no native code.

- `NeoShell.Interop/Windowing/WindowFrame.cs`: `Apply(hwnd, FrameAttributes)` sets only the parts given (the policy
  first, then dark mode, backdrop, colours, corners) and `Reset(hwnd, FrameParts, darkMode)` puts the given parts back
  (`DWMSBT_AUTO`, `DWMWA_COLOR_DEFAULT`, corners 0, `DWMNCRP_USEWINDOWSTYLE`, dark mode as read before the first
  apply); both return whether DWM accepted every call. Colours are 0xAARRGGBB; a transparent border colour is
  `DWMWA_COLOR_NONE`. `WindowFrame.Read` gives the facts rules and eligibility need (process, class, styles, cloaked).
- `NeoShell/Frames/`: `FrameRules` (rule matching, eligibility), `FrameColors` (style to attributes, the colour
  keywords `Accent`, `None` and `Contrast`, the parts to reset), `FramePresets`, `WindowFrames` (the live part,
  created by `App` while enabled, in both run modes), `WindowFramesWindow` (the settings window) and
  `FramePreviewWindow`.
- `WindowFrames` styles every top-level window at start, new ones on `Shown`/`Uncloaked`, and the foreground window on
  `Foreground` and once more 250 ms later (see above). It remembers what it gave each window; on a settings change it
  restyles all, first resetting parts the new style leaves unset (a part set either way is overwritten, so an app's
  own values of parts the style doesn't touch survive). A window DWM refuses is logged once and skipped until it's
  destroyed. Turning the feature off or `/exit` resets every window it styled; a crash leaves the looks until the
  windows close.
- A style's colours: `#RRGGBB`, `Accent` (the accent colour, `ImmersiveSystemAccent`, read on each restyle), `None`
  (border only), `Contrast` (text only: white or black by the caption colour's luminance). The Accent preset uses
  Accent caption and border with Contrast text.
- Luna's colours checked against XP's blue scheme: `ActiveCaption` 0,84,227 (#0054E3), caption text white,
  `InactiveCaption` 122,150,223 and `InactiveCaptionText` 216,228,248 (luna.msstyles' system colours, as tabulated in
  Brethorsting's "Windows System Colors"). The border #0831D9 is the dark blue of Luna's frame bitmap, kept from the
  spec; not checked against a pixel reference. DWM takes one caption colour, so Luna is flat and stays #0054E3 when
  inactive (only the caption buttons grey).
- The settings window (from the taskbar's menu, "Window frames...", until NeoShell has a settings window): on/off,
  the style for all windows, rules (added from a menu of the running apps' processes; optional class; style or
  "Leave alone"; remove), and the style editor: presets read-only, Duplicate makes an editable copy ("... copy"),
  rename (references follow), Delete (references fall back to Windows default), backdrop, light/dark, title bar, text
  and border colours (Windows default / Accent / Black or white / No border / Custom with a colour picker), corners,
  basic frame. Preview opens a NeoShell window with Windows' standard title bar beside the editor, wearing the edited
  style live. Mica backdrop, content drawn into the title bar, follows the app light/dark mode live; AutomationIds on
  every control (`WindowFramesMenuItem`, `WindowFramesToggle`, `GlobalStyleBox`, `AddRuleButton`, `AddRuleMenuItem`,
  `FrameRule`, `RuleClassBox`, `RuleStyleBox`, `RemoveRuleButton`, `EditStyleBox`, `PreviewToggle`,
  `DuplicateStyleButton`, `DeleteStyleButton`, `StyleNameBox`, `BackdropBox`, `ThemeBox`, `CaptionColorBox`,
  `CaptionColorButton`, `CaptionColorPicker`, the same for `TextColor` and `BorderColor`, `CornersBox`,
  `BasicFrameToggle`).

### What each preset looks like (measured)

On WinForms windows of a test app (light, dark, and one resetting itself to light on activation) over a striped
window, active and inactive, alongside Explorer:

- Windows default: unchanged. Mica: identical to the default on 25H2 (see above). Mica Alt: a stronger wallpaper tint,
  active only.
- Acrylic: the stripes blurred through the title bar while active; flat light grey (dark grey for a dark app) when
  inactive.
- Dark / Light: the frame, caption buttons and title text in that mode; the self-resetting window is dark again
  within 250 ms of each activation.
- Accent: the accent blue (#0063B1 here) title bar and border, white text, active and inactive alike.
- Glass (Vista-like): dark Acrylic, the stripes blurred while active, dark grey when inactive, accent border.
- Luna (XP-like): flat #0054E3 title bar, white text, blue border, small round corners; the same when inactive.
- Windows 7 Basic: uxtheme's pale blue gradient frame with Windows 7 caption buttons (a red close button when active)
  and drawn borders; the title text came out clean on WinForms here.
- Windows Terminal (draws its own title bar) with Luna: only the blue border and the corners change.

### Tested

Unit tests (`WindowFrameTests`, `SettingsStoreTests`): rule priority, eligibility, style to attributes, colours and
`COLORREF`, the parts reset, presets, copy names, settings round trip and defaults. Live, NeoShell at medium
integrity alongside Explorer, driven through UI Automation and `mouse_event` on the test app's own windows: every
preset active and inactive; the self-resetting window; styles applied at start; rules (leave alone, class narrowing,
a process rule over the global style); duplicate, rename, square corners, no border, a picked colour shown live in the
preview and saved; delete; turning off and `/exit` putting every window back (the dark app's own dark mode kept);
Windows Terminal; Task Manager elevated (refused, logged once, not retried); the settings window in the dark and light
app modes. Not done: a second monitor, other DPI scales, shell mode (the code path is the same).

## Open questions for the owner

1. Is the prototype's honest approximation of XP and Vista (option A) enough for now, or should the overlay spike (C)
   be tried for the real looks?
2. Is a native DWM extension (D) or bundling DWMBlurGlass (E) acceptable at all? Both need admin at logon and code
   inside `dwm.exe`, outside NeoShell's interop rules and approved packages.
3. Where should the feature's settings live, given NeoShell has no settings window yet?
