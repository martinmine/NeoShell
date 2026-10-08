using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Notifications;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using Windows.Graphics;

namespace NeoShell.Taskbar;

/// <param name="Value">0 to 1.</param>
public sealed record TaskProgress(TaskbarProgressState State, double Value);

/// <summary>
/// The windows that have taskbar buttons, in the order they appeared, plus which one is active, which are flashing,
/// their icons and app names, and the apps' badges. Fed by shell hooks and WinEvents; raises <see cref="Changed"/> once
/// per burst.
/// </summary>
internal sealed class WindowTracker : IDisposable
{
    private const string ExplorerAdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly int _ownProcess = Environment.ProcessId;
    private readonly AppIcons _appIcons;
    private readonly bool _announceButtons;
    private readonly List<WindowInfo> _windows = [];
    private readonly HashSet<nint> _flashing = [];
    private readonly Dictionary<nint, ImageSource?> _windowIcons = [];
    private readonly Dictionary<string, string> _appNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<nint, TaskProgress> _progress = [];
    private readonly Dictionary<nint, ImageSource> _overlays = [];
    private readonly HashSet<nint> _markedFullScreen = [];
    private readonly Dictionary<nint, ThumbBar> _thumbBars = [];
    // Windows by when they were last in front, most recent first.
    private readonly List<nint> _activated = [];
    private readonly AppBadges _badges = new();
    private readonly RegistryWatcher _explorerSettings = new(ExplorerAdvancedKey);
    private bool _showBadges = ReadShowBadges();
    private bool _showFlashing = ReadShowFlashing();
    private ShellHook? _shellHook;
    private WindowEvents? _windowEvents;
    private bool _changeQueued;
    private bool _foregroundChangeQueued;

    /// <param name="announceButtons">
    /// Tell each window when its button exists (<c>TaskbarButtonCreated</c>), as the shell's taskbar does; apps wait
    /// for it before showing progress or badges. Alongside Explorer, Explorer already does.
    /// </param>
    public WindowTracker(AppIcons appIcons, bool announceButtons)
    {
        _appIcons = appIcons;
        _announceButtons = announceButtons;
        _appIcons.Loaded += QueueChanged;
        _badges.Changed += _ => _dispatcher.TryEnqueue(QueueChanged);
        // Explorer follows "Show badges on taskbar apps" and "Show flashing on taskbar apps" as Settings (or a focus
        // session) writes them, with no message.
        _explorerSettings.Changed += () => _dispatcher.TryEnqueue(() =>
        {
            _showBadges = ReadShowBadges();
            _showFlashing = ReadShowFlashing();
            // Explorer forgets the flashing buttons: they don't come back when flashing is shown again.
            if (!_showFlashing)
                _flashing.Clear();
            QueueChanged();
        });
    }

    public event Action? Changed;

    /// <summary>A window's thumbnail toolbar changed (a player's play button became pause), by its handle.</summary>
    public event Action<nint>? ThumbBarChanged;

    /// <summary>
    /// The foreground window changed, moved, resized, or was marked full screen: whether a full-screen app is in front
    /// may have changed. Separate from <see cref="Changed"/>, as dragging a window raises it many times a second.
    /// </summary>
    public event Action? ForegroundChanged;

    public IReadOnlyList<WindowInfo> Windows => _windows;

    public nint Foreground { get; private set; }

    /// <summary>
    /// Windows by when they were last in front, most recent first: Alt+Tab's order, as Explorer keeps it (each window's
    /// last activation). Windows that were there before NeoShell come in their z-order.
    /// </summary>
    public IReadOnlyList<nint> RecentlyActive => _activated;

    /// <summary>
    /// A window's taskbar button on screen, which Windows animates it to when it's minimized (and from when it's
    /// restored); null for none. Only the shell's taskbar answers: alongside, Explorer's does.
    /// </summary>
    public Func<nint, RectInt32?>? ButtonBounds { get; set; }

    public void Start()
    {
        _windowEvents = new WindowEvents();
        _windowEvents.Raised += OnWindowEvent;
        try
        {
            _shellHook = new ShellHook { MinimizeRect = hwnd => ButtonBounds?.Invoke(hwnd) };
            _shellHook.Raised += OnShellHook;
        }
        catch (InvalidOperationException ex)
        {
            // WinEvents still cover everything but flashing.
            Log.Warn("Shell hook unavailable; flashing buttons won't be shown", ex);
        }

        // EnumWindows lists the top window first; older windows tend to be further down.
        IReadOnlyList<nint> zOrder = TopLevelWindows.GetAll();
        foreach (nint hwnd in zOrder.Reverse())
            Update(hwnd);
        _activated.AddRange(zOrder.Where(IsTracked));
        SetForeground(TopLevelWindows.GetForeground());
        Log.Info($"Tracking {_windows.Count} window(s)");
    }

    public void Dispose()
    {
        _appIcons.Loaded -= QueueChanged;
        _explorerSettings.Dispose();
        _badges.Dispose();
        _windowEvents?.Dispose();
        _shellHook?.Dispose();
    }

    public bool IsFlashing(nint hwnd) => _flashing.Contains(hwnd);

    /// <summary>The progress the window's app reports through ITaskbarList3, or null for none.</summary>
    public TaskProgress? Progress(nint hwnd) => _progress.GetValueOrDefault(hwnd);

    /// <summary>The overlay badge the window's app set through ITaskbarList3, or null.</summary>
    public ImageSource? Overlay(nint hwnd) => _overlays.GetValueOrDefault(hwnd);

    /// <summary>
    /// The app's badge notification, or null for none or while badges are turned off. Apps without package identity
    /// can't set one.
    /// </summary>
    public AppBadge? Badge(PinnedApp app) =>
        _showBadges && app.AppUserModelId is { } appId ? _badges.Get(appId) : null;

    /// <summary>The buttons the window's app put under its preview through ITaskbarList3; empty for none.</summary>
    public ThumbBar ThumbBarOf(nint hwnd) => _thumbBars.GetValueOrDefault(hwnd) ?? ThumbBar.Empty;

    /// <summary>Applies an app's thumbnail toolbar call.</summary>
    public void Apply(ThumbBarCall call)
    {
        _thumbBars[call.Window] = ThumbBarOf(call.Window).Apply(call);
        ThumbBarChanged?.Invoke(call.Window);
    }

    /// <summary>Whether the window's app called ITaskbarList2::MarkFullscreenWindow for it.</summary>
    public bool IsMarkedFullScreen(nint hwnd) => _markedFullScreen.Contains(hwnd);

    /// <summary>
    /// Applies an app's ITaskbarList3 call. Called while the app waits, so an overlay icon is copied right away.
    /// </summary>
    public void Apply(TaskbarListCall call)
    {
        nint hwnd = call.Window;
        switch (call.Kind)
        {
            case TaskbarListCallKind.ProgressState when (TaskbarProgressState)(int)call.Value == TaskbarProgressState.None:
                _progress.Remove(hwnd);
                break;
            case TaskbarListCallKind.ProgressState:
                _progress[hwnd] = (_progress.GetValueOrDefault(hwnd) ?? new TaskProgress(TaskbarProgressState.Normal, 0))
                    with { State = (TaskbarProgressState)(int)call.Value };
                break;
            case TaskbarListCallKind.ProgressValue:
                // Setting a value without a state shows normal progress, as in Explorer.
                _progress[hwnd] = (_progress.GetValueOrDefault(hwnd) ?? new TaskProgress(TaskbarProgressState.Normal, 0))
                    with { Value = call.Value };
                break;
            case TaskbarListCallKind.OverlayIcon:
                IconBitmap? pixels = call.Value != 0 ? IconBitmap.FromIcon((nint)call.Value) : null;
                if (pixels is null)
                    _overlays.Remove(hwnd);
                else
                    _overlays[hwnd] = AppIcons.ToImageSource(pixels);
                break;
            case TaskbarListCallKind.FullScreen:
                if (call.Value != 0)
                    _markedFullScreen.Add(hwnd);
                else
                    _markedFullScreen.Remove(hwnd);
                QueueForegroundChanged();
                break;
        }
        QueueChanged();
    }

    /// <summary>The window's own icon, or its app's while that's all there is.</summary>
    public ImageSource? WindowIcon(WindowInfo window)
    {
        if (_windowIcons.TryGetValue(window.Handle, out ImageSource? icon))
            return icon ?? AppIcon(AppFor(window));

        _windowIcons[window.Handle] = null;
        AppIcons.Load(() => WindowInfo.ReadIcon(window.Handle), loaded =>
        {
            if (_windowIcons.ContainsKey(window.Handle))
            {
                _windowIcons[window.Handle] = loaded;
                QueueChanged();
            }
        });
        return AppIcon(AppFor(window));
    }

    /// <summary>The app's icon from the shell, or null until it has loaded.</summary>
    public ImageSource? AppIcon(PinnedApp app) => _appIcons.Get(app);

    /// <summary>The app a window belongs to, named as Explorer would name it.</summary>
    public PinnedApp AppFor(WindowInfo window)
    {
        string key = TaskGrouping.Key(window);
        if (!_appNames.TryGetValue(key, out string? name))
        {
            name = (window.AppUserModelId is { } appId ? ShellItems.GetDisplayName(ShellItems.AppsFolderPath(appId)) : null)
                ?? FileDescription(window.ProcessPath)
                ?? window.Title;
            _appNames[key] = name;
        }
        return TaskGrouping.AppFor(window, name);
    }


    private static bool ReadShowBadges()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKey);
        return BadgeLook.AreShown(key?.GetValue("TaskbarBadges"));
    }

    private static bool ReadShowFlashing()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKey);
        return TaskFilter.ShowsFlashing(key?.GetValue("TaskbarFlashing"));
    }

    private static string? FileDescription(string? path)
    {
        if (path is null)
            return null;
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(path) : description;
        }
        catch (FileNotFoundException)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }

    private void OnShellHook(ShellHookEvent kind, nint hwnd)
    {
        switch (kind)
        {
            case ShellHookEvent.WindowActivated:
            case ShellHookEvent.RudeAppActivated:
                SetForeground(hwnd);
                break;
            case ShellHookEvent.Flash:
                if (_showFlashing && hwnd != Foreground && IsTracked(hwnd) && _flashing.Add(hwnd))
                    QueueChanged();
                break;
            case ShellHookEvent.Redraw:
                // Title or icon changed.
                _windowIcons.Remove(hwnd);
                Update(hwnd);
                break;
            case ShellHookEvent.WindowDestroyed:
                _activated.Remove(hwnd);
                Remove(hwnd);
                break;
            default:
                Update(hwnd);
                break;
        }
    }

    private void OnWindowEvent(WindowEvent kind, nint hwnd)
    {
        switch (kind)
        {
            case WindowEvent.Foreground:
                SetForeground(hwnd);
                break;
            case WindowEvent.Destroyed:
                _activated.Remove(hwnd);
                Remove(hwnd);
                break;
            case WindowEvent.NameChanged:
                int index = IndexOf(hwnd);
                if (index >= 0)
                {
                    _windows[index] = _windows[index] with { Title = WindowInfo.ReadTitle(hwnd) };
                    QueueChanged();
                }
                break;
            case WindowEvent.MinimizeStart:
            case WindowEvent.MinimizeEnd:
                QueueChanged();
                QueueForegroundChanged();
                break;
            case WindowEvent.LocationChanged:
                if (hwnd == Foreground)
                    QueueForegroundChanged();
                break;
            default: // shown, hidden, cloaked, uncloaked
                // Tooltips and menus show and hide all the time; only windows that are or could be buttons matter.
                if (IsTracked(hwnd) || kind is WindowEvent.Shown or WindowEvent.Uncloaked)
                    Update(hwnd);
                break;
        }
    }

    /// <summary>Re-reads one window and adds, updates or removes its button.</summary>
    private void Update(nint hwnd)
    {
        if (!TopLevelWindows.Exists(hwnd))
        {
            Remove(hwnd);
            return;
        }

        WindowInfo window = WindowInfo.Read(hwnd);
        bool getsButton = TaskFilter.GetsButton(window, _ownProcess);
        int index = IndexOf(hwnd);
        if (index >= 0 && getsButton)
            _windows[index] = window;
        else if (index >= 0)
            Remove(hwnd);
        else if (getsButton)
        {
            _windows.Add(window);
            if (_announceButtons)
                TopLevelWindows.NotifyButtonCreated(hwnd);
        }
        else
        {
            return;
        }
        QueueChanged();
    }

    private void Remove(nint hwnd)
    {
        int index = IndexOf(hwnd);
        if (index < 0)
            return;

        _windows.RemoveAt(index);
        _flashing.Remove(hwnd);
        _windowIcons.Remove(hwnd);
        _progress.Remove(hwnd);
        _overlays.Remove(hwnd);
        _markedFullScreen.Remove(hwnd);
        _thumbBars.Remove(hwnd);
        QueueChanged();
    }

    private void SetForeground(nint hwnd)
    {
        Foreground = hwnd;
        // A dialog in front puts the app window it belongs to first.
        nint window = TopLevelWindows.RootOwner(hwnd);
        if (window != 0)
        {
            _activated.Remove(window);
            _activated.Insert(0, window);
        }
        _flashing.Remove(hwnd);
        QueueChanged();
        QueueForegroundChanged();
    }

    private void QueueForegroundChanged()
    {
        if (_foregroundChangeQueued)
            return;

        _foregroundChangeQueued = true;
        _dispatcher.Post(() =>
        {
            _foregroundChangeQueued = false;
            ForegroundChanged?.Invoke();
        });
    }

    private bool IsTracked(nint hwnd) => IndexOf(hwnd) >= 0;

    private int IndexOf(nint hwnd) => _windows.FindIndex(w => w.Handle == hwnd);

    private void QueueChanged()
    {
        if (_changeQueued)
            return;

        _changeQueued = true;
        _dispatcher.Post(() =>
        {
            _changeQueued = false;
            Changed?.Invoke();
        });
    }
}
