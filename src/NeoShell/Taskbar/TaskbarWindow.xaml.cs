using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Graphics;

namespace NeoShell.Taskbar;

/// <summary>The taskbar on one monitor.</summary>
internal sealed partial class TaskbarWindow : Window
{
    private readonly Taskbars _owner;
    private readonly DisplayMonitor _monitor;
    private readonly AcrylicBackdrop _backdrop = new();
    private readonly FramelessWindow _frameless;
    private readonly WindowSubclass _messages;
    private readonly PinnedWindow _placement;
    private readonly AppBar? _appBar;

    public TaskbarWindow(Taskbars owner, DisplayMonitor monitor, ShellSettings settings, ElementTheme theme)
    {
        _owner = owner;
        _monitor = monitor;
        InitializeComponent();

        SystemBackdrop = _backdrop;
        SetTheme(theme);
        AppsPanel.HorizontalAlignment =
            settings.TaskbarAlignment == TaskbarAlignment.Left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        ExitSeparator.Visibility = ExitItem.Visibility =
            owner.RunMode == RunMode.AlongsideExplorer ? Visibility.Visible : Visibility.Collapsed;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        nint hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        // Out of Alt+Tab, and clicking it leaves the focus in the app the user is working in.
        WindowStyles.AddExtended(hwnd, ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate);
        _frameless = new FramelessWindow(hwnd);
        _messages = new WindowSubclass(hwnd, OnMessage);

        RectInt32 bounds = TaskbarLayout.Bounds(monitor.Bounds, monitor.Dpi);
        if (owner.RunMode == RunMode.AlongsideExplorer)
        {
            // Explorer manages the screen space and puts this bar above its own taskbar.
            _appBar = new AppBar(hwnd);
            bounds = _appBar.DockBottom(monitor.Bounds, bounds.Height);
            _appBar.PositionChanged += () => _placement!.Bounds = _appBar.DockBottom(monitor.Bounds, bounds.Height);
        }
        else
        {
            WorkArea.Set(TaskbarLayout.WorkArea(monitor.Bounds, bounds));
        }
        _placement = new PinnedWindow(hwnd, bounds, PinnedLayer.Topmost);

        Closed += (_, _) =>
        {
            // Give the space back first, so windows can use it straight away.
            if (_appBar is not null)
                _appBar.Dispose();
            else
                WorkArea.Set(monitor.Bounds);
            _placement.Dispose();
            _messages.Dispose();
            _frameless.Dispose();
        };
    }

    public void SetTheme(ElementTheme theme)
    {
        Root.RequestedTheme = theme;
        _backdrop.Theme = theme;
    }

    public void UpdateClock() => Clock.Update();

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        // WinUI resizes the window for the new DPI; the taskbar needs a new height, so it's recreated. A window
        // moved onto its monitor right after creation gets this too, with the DPI it was created for.
        if (message == WindowMessages.DpiChanged && (uint)(wParam & 0xFFFF) != _monitor.Dpi)
            _owner.QueueRecreate();
        else
            _owner.OnBroadcast(message);
        return null;
    }

    private void TaskbarMenu_Opening(object sender, object e)
    {
        ShellSettings settings = _owner.Settings.Current;
        AlignCenterItem.IsChecked = settings.TaskbarAlignment == TaskbarAlignment.Center;
        AlignLeftItem.IsChecked = settings.TaskbarAlignment == TaskbarAlignment.Left;
        AllDisplaysItem.IsChecked = settings.ShowOnAllDisplays;
    }

    private void TaskManager_Click(object sender, RoutedEventArgs e) => _owner.OpenTaskManager();

    private void Alignment_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with
        {
            TaskbarAlignment = ReferenceEquals(sender, AlignLeftItem) ? TaskbarAlignment.Left : TaskbarAlignment.Center,
        });

    private void AllDisplays_Click(object sender, RoutedEventArgs e) =>
        _owner.Settings.Update(_owner.Settings.Current with { ShowOnAllDisplays = AllDisplaysItem.IsChecked });

    private void Exit_Click(object sender, RoutedEventArgs e) => _owner.Exit();

    private void ShowDesktopButton_Click(object sender, RoutedEventArgs e) => _owner.ToggleDesktop();
}
