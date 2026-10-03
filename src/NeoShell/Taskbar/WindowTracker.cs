using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Taskbar;

/// <summary>
/// The windows that have taskbar buttons, in the order they appeared, plus which one is active, which are flashing,
/// and their icons and app names. Fed by shell hooks and WinEvents; raises <see cref="Changed"/> once per burst.
/// </summary>
internal sealed class WindowTracker : IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly int _ownProcess = Environment.ProcessId;
    private readonly int _iconSize;
    private readonly List<WindowInfo> _windows = [];
    private readonly HashSet<nint> _flashing = [];
    private readonly Dictionary<nint, ImageSource?> _windowIcons = [];
    private readonly Dictionary<string, ImageSource?> _appIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _appNames = new(StringComparer.OrdinalIgnoreCase);
    private ShellHook? _shellHook;
    private WindowEvents? _windowEvents;
    private bool _changeQueued;

    /// <param name="iconSize">Icon size in pixels.</param>
    public WindowTracker(int iconSize)
    {
        _iconSize = iconSize;
    }

    public event Action? Changed;

    public IReadOnlyList<WindowInfo> Windows => _windows;

    public nint Foreground { get; private set; }

    public void Start()
    {
        _windowEvents = new WindowEvents();
        _windowEvents.Raised += OnWindowEvent;
        try
        {
            _shellHook = new ShellHook();
            _shellHook.Raised += OnShellHook;
        }
        catch (InvalidOperationException ex)
        {
            // WinEvents still cover everything but flashing.
            Log.Warn("Shell hook unavailable; flashing buttons won't be shown", ex);
        }

        // EnumWindows lists the top window first; older windows tend to be further down.
        foreach (nint hwnd in TopLevelWindows.GetAll().Reverse())
            Update(hwnd);
        Foreground = TopLevelWindows.GetForeground();
        Log.Info($"Tracking {_windows.Count} window(s)");
    }

    public void Dispose()
    {
        _windowEvents?.Dispose();
        _shellHook?.Dispose();
    }

    public bool IsFlashing(nint hwnd) => _flashing.Contains(hwnd);

    /// <summary>The window's own icon, or its app's while that's all there is.</summary>
    public ImageSource? WindowIcon(WindowInfo window)
    {
        if (_windowIcons.TryGetValue(window.Handle, out ImageSource? icon))
            return icon ?? AppIcon(AppFor(window));

        _windowIcons[window.Handle] = null;
        LoadIcon(() => WindowInfo.ReadIcon(window.Handle), loaded =>
        {
            if (_windowIcons.ContainsKey(window.Handle))
                _windowIcons[window.Handle] = loaded;
        });
        return AppIcon(AppFor(window));
    }

    /// <summary>The app's icon from the shell, or null until it has loaded.</summary>
    public ImageSource? AppIcon(PinnedApp app)
    {
        string item = ShellItemFor(app);
        if (_appIcons.TryGetValue(item, out ImageSource? icon))
            return icon;

        _appIcons[item] = null;
        LoadIcon(() => ShellItems.GetIcon(item, _iconSize), loaded => _appIcons[item] = loaded);
        return null;
    }

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

    private static string ShellItemFor(PinnedApp app) =>
        app.AppUserModelId is { } appId ? ShellItems.AppsFolderPath(appId) : app.Path ?? "";

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

    // Icons are read off the UI thread (WM_GETICON can wait on a hung window, the shell may read from disk) and
    // turned into bitmaps back on it.
    private async void LoadIcon(Func<IconBitmap?> read, Action<ImageSource?> store)
    {
        try
        {
            IconBitmap? icon = await Task.Run(read);
            if (icon is null)
                return;

            var bitmap = new WriteableBitmap(icon.Width, icon.Height);
            using (Stream stream = bitmap.PixelBuffer.AsStream())
                stream.Write(icon.Pixels);
            bitmap.Invalidate();
            store(bitmap);
            QueueChanged();
        }
        catch (Exception ex)
        {
            Log.Warn("Loading an icon failed", ex);
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
                if (hwnd != Foreground && IsTracked(hwnd) && _flashing.Add(hwnd))
                    QueueChanged();
                break;
            case ShellHookEvent.Redraw:
                // Title or icon changed.
                _windowIcons.Remove(hwnd);
                Update(hwnd);
                break;
            case ShellHookEvent.WindowDestroyed:
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
            _windows.Add(window);
        else
            return;
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
        QueueChanged();
    }

    private void SetForeground(nint hwnd)
    {
        Foreground = hwnd;
        _flashing.Remove(hwnd);
        QueueChanged();
    }

    private bool IsTracked(nint hwnd) => IndexOf(hwnd) >= 0;

    private int IndexOf(nint hwnd) => _windows.FindIndex(w => w.Handle == hwnd);

    private void QueueChanged()
    {
        if (_changeQueued)
            return;

        _changeQueued = true;
        _dispatcher.TryEnqueue(() =>
        {
            _changeQueued = false;
            Changed?.Invoke();
        });
    }
}
