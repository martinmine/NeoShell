# Testing strategy

How NeoShell is tested: unit tests, live UI checks, shell mode. Part of the [NeoShell design](../design.md).

## Testing strategy

- **xunit** tests in `tests/NeoShell.Tests` for pure logic: taskbar window filter, grouping keys, NOTIFYICONDATA
  parsing (32/64-bit), app search ranking, indexer query building, startup entry parsing and `StartupApproved`,
  settings round-trip and corrupt-file handling, wallpaper style mapping, AppBar rect calculation, other apps' app
  bar messages (32/64-bit) and their placing and work area, which desktop
  items get icons and in which order, Quick Settings' paging, Wi-Fi network list and shortcut keys, the keys the
  hook takes (Start, panels, Alt+Tab, Win+Comma), Win+number's window choice, Snap layouts and screenshot names,
  the shell's work area, the sidebar's and floating widgets' placement and order, the widgets' number and colour
  formats, and MET's forecast parsing and symbols.
- Logic that touches Windows is split so the decision is a pure function over a snapshot (e.g. `WindowInfo`) that
  tests can construct.
- **Live UI checks** through UI Automation (`AutomationId`s on all interactive controls), never global keystrokes.
- **Shell mode** only on a test account or VM, via `tools/set-shell.ps1` (every sign-in) or `tools/start-shell.ps1`
  (this session: "Exit Explorer"'s message to `Shell_TrayWnd`, NeoShell started, then what's left of Explorer's
  process ended, since a later Explorer hangs on it).
