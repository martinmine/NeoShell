using Microsoft.UI.Dispatching;
using NeoShell.AutoPlay;
using NeoShell.Capture;
using NeoShell.Desktop;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.QuickSettings;
using NeoShell.Snap;
using NeoShell.Switcher;
using NeoShell.Taskbar;

namespace NeoShell;

/// <summary>
/// What NeoShell takes on as the shell, beyond its windows: telling Windows the shell is ready, the Windows key and
/// shortcuts, startup apps and the end of the session. Shell mode only; alongside, Explorer does all of this.
/// </summary>
internal sealed class ShellSession : IDisposable
{
    private readonly ShellRegistration _registration;
    private readonly Taskbars _taskbars;
    private readonly Func<nint, WallpaperWindow?> _wallpaperOn;
    private readonly Action _endSession;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly StartKeyDetector _startKeys = new();
    private readonly PanelKeys _panelKeys = new();
    private readonly AltTabKeys _altTabKeys = new();
    private readonly PeekKeys _peekKeys = new();
    private readonly InputSwitchKeys _inputSwitchKeys = new();
    private readonly SnapKeys _snapKeys;
    // What each hotkey does, by its ID less one.
    private readonly List<Action> _hotkeyActions = [];
    private WindowSwitcher? _switcher;
    private WindowSnapping? _snapping;
    private Hotkeys? _hotkeys;
    private KeyboardHook? _keyboardHook;
    private ShellServiceObjects? _serviceObjects;
    private VolumeAutoPlay? _autoPlay;
    private ShellUndoServer? _undoServer;

    /// <param name="wallpaperOn">The wallpaper's window on a monitor, which Snap Assist shows behind its cards.</param>
    /// <param name="endSession">Saves and cleans up before Windows ends the process at sign-out or shutdown.</param>
    public ShellSession(ShellRegistration registration, Taskbars taskbars, Func<nint, WallpaperWindow?> wallpaperOn, Action endSession)
    {
        _registration = registration;
        _taskbars = taskbars;
        _wallpaperOn = wallpaperOn;
        _endSession = endSession;
        _snapKeys = new SnapKeys(key => _snapping?.Takes(key) ?? false);
    }

    public void Start()
    {
        _registration.TaskListRequested += _taskbars.ToggleStartMenuFromKeyboard;
        _registration.SessionEnding += OnSessionEnding;

        _registration.Complete();
        Log.Info("Shell ready");

        // The session's undo history for file operations lives with the shell, as with Explorer's desktop: other
        // processes' operations go to it, and the desktop's menu can tell what Undo would undo.
        _undoServer = ShellUndoServer.Start();

        // Without Explorer, Windows snaps no windows: dragged against an edge or with Win+arrows, Snap layouts, Snap
        // Assist and snap groups (which the taskbar and Alt+Tab show).
        _snapping = new WindowSnapping(_taskbars.Tracker, () => _taskbars.Theme, _wallpaperOn);
        _taskbars.Snapping = _snapping;
        RegisterHotkeys();

        // Without Explorer, Alt+Tab is Windows' old icon grid; the hook takes it for NeoShell's switcher.
        var switcher = new WindowSwitcher(_taskbars.Tracker, () => _taskbars.Theme, _snapping);
        switcher.Dismissed += _altTabKeys.Close;
        _switcher = switcher;

        // The Windows key on its own isn't a hotkey: only a hook sees it pressed and released by itself. Keys pass
        // through unchanged, so Windows still knows the key is down for Win+ shortcuts; only the panels' own
        // shortcuts, the switcher's keys, Win+Comma's comma, Win+Space's space and Snap's arrows are taken (see
        // PanelKeys, AltTabKeys, PeekKeys, InputSwitchKeys, SnapKeys).
        try
        {
            _keyboardHook = new KeyboardHook
            {
                Key = (key, down) =>
                {
                    if (_startKeys.OnKey(key, down))
                        _dispatcher.Post(_taskbars.ToggleStartMenuFromKeyboard);
                    bool switcherKey = _altTabKeys.OnKey(key, down, out SwitcherCommand? switcherCommand);
                    if (switcherCommand is { } command)
                    {
                        if (command is SwitcherCommand.Open or SwitcherCommand.OpenBackwards)
                            KeyboardHook.MaskModifierKeys();
                        _dispatcher.Post(() => switcher.Run(command));
                    }
                    if (switcherKey)
                        return true;
                    bool peekKey = _peekKeys.OnKey(key, down, out bool? peek);
                    if (peek is { } on)
                    {
                        if (on)
                            KeyboardHook.MaskModifierKeys();
                        _dispatcher.Post(() => _taskbars.PeekAtDesktop(on));
                    }
                    if (peekKey)
                        return true;
                    // Without Explorer nothing answers Win+Space; Alt+Shift still switches, in Windows itself.
                    bool inputKey = _inputSwitchKeys.OnKey(key, down, out InputSwitchCommand? inputCommand);
                    if (inputCommand is { } input)
                    {
                        if (input is InputSwitchCommand.Open or InputSwitchCommand.OpenBackwards)
                            KeyboardHook.MaskModifierKeys();
                        _dispatcher.Post(() => _taskbars.RunInputSwitch(input));
                    }
                    if (inputKey)
                        return true;
                    bool snapKey = _snapKeys.OnKey(key, down, out SnapKey? arrow);
                    if (arrow is { } pressedArrow)
                    {
                        KeyboardHook.MaskModifierKeys();
                        _dispatcher.Post(() => _snapping?.SnapForeground(pressedArrow));
                    }
                    if (snapKey)
                        return true;
                    if (!_panelKeys.OnKey(key, down, out PanelShortcut? shortcut))
                        return false;
                    if (shortcut is { } pressed)
                    {
                        KeyboardHook.MaskModifierKeys();
                        if (PanelKeys.PageFor(pressed) is { } page)
                            _dispatcher.Post(() => _taskbars.ShowQuickSettings(page));
                        else if (pressed == PanelShortcut.QuickLinks)
                            _dispatcher.Post(_taskbars.ToggleQuickLinks);
                        else
                            _dispatcher.Post(_taskbars.ToggleNotificationCenter);
                    }
                    return true;
                },
            };
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Ctrl+Esc still reaches the shell window; only the Windows key alone, Quick Settings' keys, Alt+Tab and
            // Win+Comma are lost.
            Log.Warn("Keyboard hook unavailable; the Windows key won't open Start", ex);
        }

        // Safely Remove Hardware and the objects other components register to run with the shell, as Explorer starts
        // them once its taskbar is up.
        _serviceObjects = ShellServiceObjects.Start(ex => Log.Error("Shell service objects failed", ex));
        Log.Info("Shell service objects started");

        // Without Explorer nothing reacts to inserted media.
        _autoPlay = new VolumeAutoPlay(() => _taskbars.Toasts);

        if (StartupApps.HaveRunThisSession())
        {
            Log.Info("Startup apps already ran in this session");
        }
        else
        {
            // After the tray is up, so the apps' icons have somewhere to go.
            _dispatcher.Post(DispatcherQueuePriority.Low, StartupApps.RunAll);
        }
    }

    public void Dispose()
    {
        _registration.TaskListRequested -= _taskbars.ToggleStartMenuFromKeyboard;
        _registration.SessionEnding -= OnSessionEnding;
        _keyboardHook?.Dispose();
        _hotkeys?.Dispose();
        _switcher?.Close();
        _taskbars.Snapping = null;
        _snapping?.Dispose();
        _autoPlay?.Dispose();
        _undoServer?.Dispose();
        // Explorer waits for them however long they take; a shell that's exiting can't.
        if (_serviceObjects is not null && !_serviceObjects.Stop(TimeSpan.FromSeconds(3)))
            Log.Warn("Shell service objects didn't close within 3 seconds");
    }

    /// <summary>
    /// Explorer's Win+ shortcuts that have to work without it. Those Windows' own components keep after Explorer has
    /// gone (Quick Settings', Snap's arrows, Win+L) are left to them, or taken by the keyboard hook (see PanelKeys).
    /// </summary>
    private void RegisterHotkeys()
    {
        const HotkeyModifiers Shift = HotkeyModifiers.Shift, Control = HotkeyModifiers.Control, Alt = HotkeyModifiers.Alt;
        const uint VK_PAUSE = 0x13, VK_SNAPSHOT = 0x2C, VK_HOME = 0x24;
        _hotkeys = new Hotkeys();
        _hotkeys.Pressed += id => _hotkeyActions[id - 1]();

        Register("Win+D", 'D', _taskbars.ToggleDesktop);
        Register("Win+M", 'M', _taskbars.MinimizeAll);
        Register("Win+Shift+M", 'M', _taskbars.RestoreMinimized, Shift);
        Register("Win+Home", VK_HOME, _taskbars.ToggleAllButForeground);
        Register("Win+T", 'T', () => _taskbars.FocusTaskbar());
        Register("Win+Shift+T", 'T', () => _taskbars.FocusTaskbar(last: true), Shift);
        Register("Win+B", 'B', _taskbars.FocusTray);
        Register("Win+S", 'S', _taskbars.OpenStartMenu);
        Register("Win+Q", 'Q', _taskbars.OpenStartMenu);
        Register("Win+E", 'E', Launcher.OpenFileExplorer);
        Register("Win+R", 'R', _taskbars.ShowRunDialog);
        Register("Win+I", 'I', () => Launcher.OpenSettings(RunMode.Shell, "Settings", "ms-settings:"));
        Register("Win+Pause", VK_PAUSE, () => Launcher.OpenSettings(RunMode.Shell, "System", "ms-settings:about", "sysdm.cpl"));
        Register("Win+Alt+D", 'D', _taskbars.ToggleNotificationCenter, Alt);
        Register("Win+Alt+K", 'K', _taskbars.ToggleMicrophoneMute, Alt);
        Register("Win+Shift+S", 'S', ScreenSnip.Start, Shift);
        Register("Win+PrtScn", VK_SNAPSHOT, () => Screenshots.CaptureScreen(_taskbars.PrimaryHandle));
        Register("Win+Z", 'Z', () => _snapping?.OpenLayouts(TopLevelWindows.GetForeground()));
        for (int n = 1; n <= 9; n++)
        {
            int index = n - 1;
            uint digit = (uint)('0' + n);
            Register($"Win+{n}", digit, () => _taskbars.ActivateTask(index));
            Register($"Win+Shift+{n}", digit, () => _taskbars.LaunchTask(index, elevated: false), Shift);
            Register($"Win+Ctrl+Shift+{n}", digit, () => _taskbars.LaunchTask(index, elevated: true), Control | Shift);
            Register($"Win+Ctrl+{n}", digit, () => _taskbars.ActivateLastWindow(index), Control);
            Register($"Win+Alt+{n}", digit, () => _taskbars.ShowJumpList(index), Alt);
        }
    }

    // Win with the key (and modifiers), once per press however long it's held.
    private void Register(string name, uint key, Action action, HotkeyModifiers modifiers = HotkeyModifiers.None)
    {
        _hotkeyActions.Add(action);
        if (!_hotkeys!.Register(_hotkeyActions.Count, modifiers | HotkeyModifiers.Windows | HotkeyModifiers.NoRepeat, key))
            Log.Warn($"{name} is taken by another app");
    }

    private void OnSessionEnding()
    {
        Log.Info("Session ending");
        _endSession();
    }
}
