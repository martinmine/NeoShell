using Microsoft.UI.Dispatching;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.QuickSettings;
using NeoShell.Switcher;
using NeoShell.Taskbar;

namespace NeoShell;

/// <summary>
/// What NeoShell takes on as the shell, beyond its windows: telling Windows the shell is ready, the Windows key and
/// shortcuts, startup apps and the end of the session. Shell mode only; alongside, Explorer does all of this.
/// </summary>
internal sealed class ShellSession : IDisposable
{
    private const int ShowDesktopHotkey = 1;
    private const int FocusTaskbarHotkey = 2;
    private const int SearchHotkey = 3;
    // Win+1…9 use these IDs plus 0…8.
    private const int FirstTaskHotkey = 11;

    private readonly ShellRegistration _registration;
    private readonly Taskbars _taskbars;
    private readonly Action _endSession;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly StartKeyDetector _startKeys = new();
    private readonly PanelKeys _panelKeys = new();
    private readonly AltTabKeys _altTabKeys = new();
    private WindowSwitcher? _switcher;
    private Hotkeys? _hotkeys;
    private KeyboardHook? _keyboardHook;

    /// <param name="endSession">Saves and cleans up before Windows ends the process at sign-out or shutdown.</param>
    public ShellSession(ShellRegistration registration, Taskbars taskbars, Action endSession)
    {
        _registration = registration;
        _taskbars = taskbars;
        _endSession = endSession;
    }

    public void Start()
    {
        _registration.TaskListRequested += _taskbars.ToggleStartMenuFromKeyboard;
        _registration.SessionEnding += OnSessionEnding;

        _registration.Complete();
        Log.Info("Shell ready");

        _hotkeys = new Hotkeys();
        _hotkeys.Pressed += OnHotkey;
        const HotkeyModifiers WinKey = HotkeyModifiers.Windows | HotkeyModifiers.NoRepeat;
        foreach ((int id, uint key, string name) in (ReadOnlySpan<(int, uint, string)>)
            [(ShowDesktopHotkey, 'D', "Win+D"), (FocusTaskbarHotkey, 'T', "Win+T"), (SearchHotkey, 'S', "Win+S")])
        {
            if (!_hotkeys.Register(id, WinKey, key))
                Log.Warn($"{name} is taken by another app");
        }
        for (int n = 1; n <= 9; n++)
        {
            if (!_hotkeys.Register(FirstTaskHotkey + n - 1, WinKey, (uint)('0' + n)))
                Log.Warn($"Win+{n} is taken by another app");
        }

        // Without Explorer, Alt+Tab is Windows' old icon grid; the hook takes it for NeoShell's switcher.
        var switcher = new WindowSwitcher(_taskbars.Tracker, () => _taskbars.Theme);
        switcher.Dismissed += _altTabKeys.Close;
        _switcher = switcher;

        // The Windows key on its own isn't a hotkey: only a hook sees it pressed and released by itself. Keys pass
        // through unchanged, so Windows still knows the key is down for Win+ shortcuts; only the panels' own
        // shortcuts and the switcher's keys are taken (see PanelKeys, AltTabKeys).
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
            // Ctrl+Esc still reaches the shell window; only the Windows key alone and Quick Settings' keys are lost.
            Log.Warn("Keyboard hook unavailable; the Windows key won't open Start", ex);
        }

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
    }

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case >= FirstTaskHotkey and < FirstTaskHotkey + 9:
                _taskbars.ActivateTask(id - FirstTaskHotkey);
                break;
            case ShowDesktopHotkey:
                _taskbars.ToggleDesktop();
                break;
            case FocusTaskbarHotkey:
                _taskbars.FocusTaskbar();
                break;
            case SearchHotkey:
                _taskbars.OpenStartMenu();
                break;

        }
    }

    private void OnSessionEnding()
    {
        Log.Info("Session ending");
        _endSession();
    }
}
