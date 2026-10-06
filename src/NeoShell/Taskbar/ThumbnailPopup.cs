using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using NeoShell.Themes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Taskbar;

/// <summary>
/// Live previews of a button's windows above the taskbar: hover one to peek at its window, click it to switch to it,
/// or close it.
/// </summary>
internal sealed class ThumbnailPopup : Window
{
    // Effective pixels.
    private const double CellWidth = 220;
    private const double MinCellWidth = 120;
    private const double HeaderHeight = 32;
    private const double PreviewHeight = 124;
    // Under the preview, for the app's thumbnail toolbar.
    private const double ToolbarHeight = 36;
    private const double Padding = 8;
    private const double Gap = 8;
    private static readonly TimeSpan PeekDelay = TimeSpan.FromMilliseconds(400);
    // Moving from one preview to the next leaves the first before entering the second; the peek waits this long
    // before ending so it moves across instead of flashing every window back in between.
    private static readonly TimeSpan PeekEndDelay = TimeSpan.FromMilliseconds(100);

    private readonly nint _hwnd;
    private readonly nint _taskbar;
    private readonly WindowTracker _tracker;
    private readonly ShellBackdrop _backdrop = new(Backdrop.Acrylic);
    private readonly Grid _root = new() { Padding = new Thickness(Padding) };
    private readonly StackPanel _cells = new() { Orientation = Orientation.Horizontal, Spacing = Gap };
    private readonly List<DwmThumbnail> _thumbnails = [];
    // The thumbnail toolbars shown, by window, to follow the app's changes while open.
    private readonly Dictionary<nint, StackPanel> _toolbars = [];
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly WindowSlide _slide;
    private readonly DispatcherQueueTimer _peekTimer;
    private nint _peekTarget;
    private bool _peeking;
    private bool _visible;
    private int _taskbarTop;
    // Where the previews are cut off, in pixels from the popup's top.
    private int _thumbnailsBottom;

    /// <param name="taskbar">The taskbar the popup belongs to; it slides out from behind it.</param>
    public ThumbnailPopup(WindowTracker tracker, nint taskbar)
    {
        _tracker = tracker;
        _taskbar = taskbar;
        _root.Children.Add(_cells);
        Content = _root;
        SystemBackdrop = _backdrop;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(_hwnd, ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate);
        Peek.Exclude(_hwnd);
        _frameless = new FramelessWindow(_hwnd, roundedCorners: ShellTheme.Current.RoundedCorners);
        _placement = new PinnedWindow(_hwnd, default, PinnedLayer.Topmost);
        _placement.SetLayer(PinnedLayer.Topmost, above: taskbar);
        _slide = new WindowSlide(_placement);
        // The previews are cut off at the taskbar's edge where the popup is or is about to be, whichever shows less:
        // the window and the previews move in separate steps.
        _slide.Moving += next => ClipThumbnails(Math.Max(_placement.Bounds.Y, next.Y));
        _peekTimer = DispatcherQueue.CreateTimer();
        _peekTimer.IsRepeating = false;
        _peekTimer.Tick += (_, _) => UpdatePeek();
        _tracker.ThumbBarChanged += OnThumbBarChanged;

        Closed += (_, _) =>
        {
            _tracker.ThumbBarChanged -= OnThumbBarChanged;
            EndPeek();
            _slide.Stop();
            ClearCells();
            _placement.Dispose();
            _frameless.Dispose();
        };
    }

    /// <summary>The button whose windows are shown, or null when hidden.</summary>
    public TaskButton? Button { get; private set; }

    public UIElement Root => _root;

    /// <summary>Shows the button's windows centred above <paramref name="anchor"/> (screen pixels).</summary>
    public void Show(TaskButton button, RectInt32 anchor, DisplayMonitor monitor, ElementTheme theme)
    {
        Button = button;
        EndPeek();
        _root.RequestedTheme = theme;
        _backdrop.Theme = theme;
        ClearCells();
        _taskbarTop = anchor.Y;
        ClipThumbnails(_visible ? _placement.Bounds.Y : _taskbarTop);

        double scale = monitor.Dpi / 96.0;
        int count = button.Windows.Count;
        double maxCellWidth = (monitor.WorkArea.Width / scale - 2 * Padding - (count - 1) * Gap) / count;
        double cellWidth = Math.Max(MinCellWidth, Math.Min(CellWidth, maxCellWidth));
        // All the previews get a toolbar's room if one has a toolbar, so they stay in line.
        bool toolbars = button.Windows.Any(window => _tracker.ThumbBarOf(window.Handle).Buttons.Any(b => !b.IsHidden));
        foreach (WindowInfo window in button.Windows)
            _cells.Children.Add(CreateCell(window, cellWidth, theme, toolbars));

        int width = (int)Math.Ceiling((2 * Padding + count * cellWidth + (count - 1) * Gap) * scale);
        int height = (int)Math.Ceiling((2 * Padding + HeaderHeight + PreviewHeight + (toolbars ? ToolbarHeight : 0)) * scale);
        int x = Math.Clamp(anchor.X + anchor.Width / 2 - width / 2, monitor.WorkArea.X, monitor.WorkArea.X + monitor.WorkArea.Width - width);
        int y = anchor.Y - height - (int)(8 * scale);
        var bounds = new RectInt32(x, y, width, height);
        // Out of the taskbar, as in Windows 11, shown only above its edge; once open, it follows the pointer along
        // the taskbar.
        _placement.VisibleBottom = _taskbarTop;
        if (!_visible)
        {
            _visible = true;
            _placement.Bounds = bounds with { Y = _taskbarTop };
            AppWindow.Show(activateWindow: false);
            _slide.To(bounds, TimeSpan.FromMilliseconds(200), decelerate: true, () => ClipThumbnails(bounds.Y));
        }
        else
        {
            _slide.To(bounds, TimeSpan.FromMilliseconds(150), decelerate: true, () => ClipThumbnails(bounds.Y));
        }
    }

    /// <summary>Slides back into the taskbar; showing again on the way turns it round.</summary>
    public void Hide()
    {
        Button = null;
        EndPeek();
        if (!_visible)
            return;

        _slide.To(_placement.Bounds with { Y = _taskbarTop }, TimeSpan.FromMilliseconds(120), decelerate: false, () =>
        {
            _visible = false;
            AppWindow.Hide();
            ClearCells();
        });
    }

    private Grid CreateCell(WindowInfo window, double width, ElementTheme theme, bool toolbar)
    {
        // Explorer's plate behind the preview under the pointer. The colours are the Fluent SubtleFillColorSecondary
        // and Tertiary tokens: a brush from the app's resources would follow the app's theme, not the popup's.
        bool dark = theme == ElementTheme.Dark;
        var hover = new SolidColorBrush(dark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x09, 0, 0, 0));
        var pressed = new SolidColorBrush(dark ? Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x06, 0, 0, 0));
        var idle = new SolidColorBrush(Colors.Transparent);

        var cell = new Grid
        {
            Width = width,
            CornerRadius = new CornerRadius(4),
            Background = idle,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(HeaderHeight) },
                new RowDefinition { Height = new GridLength(PreviewHeight) },
            },
        };
        AutomationProperties.SetName(cell, window.Title);
        AutomationProperties.SetAutomationId(cell, "ThumbnailCell");
        cell.PointerEntered += (_, _) =>
        {
            cell.Background = hover;
            PeekAt(window.Handle);
        };
        cell.PointerExited += (_, _) =>
        {
            cell.Background = idle;
            PeekAt(0);
        };
        cell.PointerPressed += (_, _) => cell.Background = pressed;
        cell.PointerReleased += (_, _) => cell.Background = hover;
        cell.Tapped += (_, _) =>
        {
            // Brought to the front first, so ending the peek doesn't show the old front window in between.
            TopLevelWindows.Activate(window.Handle);
            Hide();
        };

        var header = new Grid { Padding = new Thickness(6, 0, 0, 0), ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new Image { Source = _tracker.WindowIcon(window), Width = 16, Height = 16 });
        var title = new TextBlock
        {
            Text = window.Title,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var close = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 10 },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(close, "Close " + window.Title);
        AutomationProperties.SetAutomationId(close, "ThumbnailCloseButton");
        close.Click += (_, _) =>
        {
            if (_peekTarget == window.Handle)
                EndPeek();
            TopLevelWindows.Close(window.Handle);
        };
        close.Tapped += (_, e) => e.Handled = true;
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        cell.Children.Add(header);

        // The preview is drawn by DWM over this placeholder, once layout says where it is.
        var placeholder = new Border { Margin = new Thickness(6) };
        Grid.SetRow(placeholder, 1);
        cell.Children.Add(placeholder);

        if (toolbar)
        {
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ToolbarHeight) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetRow(buttons, 2);
            cell.Children.Add(buttons);
            _toolbars[window.Handle] = buttons;
            FillToolbar(buttons, window.Handle);
        }
        var thumbnail = new DwmThumbnail(_hwnd, window.Handle);
        thumbnail.Clip(_thumbnailsBottom);
        _thumbnails.Add(thumbnail);
        placeholder.SizeChanged += (_, _) => PlaceThumbnail(thumbnail, placeholder);
        return cell;
    }

    /// <summary>
    /// The app's thumbnail toolbar under its window's preview, as in Explorer: its buttons' images, tooltips and
    /// states; a click goes to the window as <c>THBN_CLICKED</c>.
    /// </summary>
    private void FillToolbar(StackPanel panel, nint window)
    {
        panel.Children.Clear();
        ThumbBar bar = _tracker.ThumbBarOf(window);
        foreach (ThumbButton thumbButton in bar.Buttons.Where(b => !b.IsHidden))
        {
            var button = new Button
            {
                Width = 32,
                Height = 28,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                IsEnabled = !thumbButton.Flags.HasFlag(ThumbButtonFlags.Disabled),
                IsHitTestVisible = !thumbButton.Flags.HasFlag(ThumbButtonFlags.NonInteractive),
            };
            if (thumbButton.Image(bar.Images) is { } image)
                button.Content = new Image { Source = AppIcons.ToImageSource(image), Width = 16, Height = 16 };
            if (thumbButton.Tooltip.Length > 0)
                ToolTipService.SetToolTip(button, thumbButton.Tooltip);
            AutomationProperties.SetName(button, thumbButton.Tooltip.Length > 0 ? thumbButton.Tooltip : $"Button {thumbButton.Id}");
            AutomationProperties.SetAutomationId(button, "ThumbBarButton");
            uint id = thumbButton.Id;
            bool dismiss = thumbButton.Flags.HasFlag(ThumbButtonFlags.DismissOnClick);
            button.Click += (_, _) =>
            {
                TopLevelWindows.ClickThumbButton(window, id);
                if (dismiss)
                    Hide();
            };
            // Not a tap on the preview, which would switch to the window.
            button.Tapped += (_, e) => e.Handled = true;
            panel.Children.Add(button);
        }
    }

    private void OnThumbBarChanged(nint window)
    {
        if (_toolbars.TryGetValue(window, out StackPanel? panel))
            FillToolbar(panel, window);
    }

    private void PlaceThumbnail(DwmThumbnail thumbnail, FrameworkElement placeholder)
    {
        if (placeholder.XamlRoot is null)
            return;

        double scale = placeholder.XamlRoot.RasterizationScale;
        Point origin = placeholder.TransformToVisual(_root).TransformPoint(default);
        double areaWidth = placeholder.ActualWidth * scale;
        double areaHeight = placeholder.ActualHeight * scale;
        SizeInt32 source = thumbnail.SourceSize;
        if (source.Width <= 0 || source.Height <= 0)
            return;

        // Fit the window's aspect ratio inside the area, centred.
        double factor = Math.Min(areaWidth / source.Width, areaHeight / source.Height);
        double width = source.Width * factor;
        double height = source.Height * factor;
        thumbnail.Show(new RectInt32(
            (int)(origin.X * scale + (areaWidth - width) / 2),
            (int)(origin.Y * scale + (areaHeight - height) / 2),
            (int)width,
            (int)height));
    }

    /// <summary>Peeks at <paramref name="window"/> after a pause, or straight away if already peeking; 0 ends it.</summary>
    private void PeekAt(nint window)
    {
        _peekTarget = window;
        _peekTimer.Stop();
        if (_peeking && window != 0)
            UpdatePeek();
        else if (_peeking || window != 0)
        {
            _peekTimer.Interval = _peeking ? PeekEndDelay : PeekDelay;
            _peekTimer.Start();
        }
    }

    private void UpdatePeek()
    {
        if (_peekTarget == 0)
        {
            EndPeek();
            return;
        }
        // DWM ignores a peek at another window while one is on; ending it first crossfades from one to the other.
        if (_peeking)
            Peek.End(_taskbar);
        _peeking = true;
        Peek.Show(_peekTarget, _taskbar);
    }

    private void EndPeek()
    {
        _peekTarget = 0;
        _peekTimer.Stop();
        if (!_peeking)
            return;
        _peeking = false;
        Peek.End(_taskbar);
    }

    // Cuts the previews off at the taskbar's edge for the popup at screen row <paramref name="top"/>.
    private void ClipThumbnails(int top)
    {
        _thumbnailsBottom = _taskbarTop - top;
        foreach (DwmThumbnail thumbnail in _thumbnails)
            thumbnail.Clip(_thumbnailsBottom);
    }

    private void ClearCells()
    {
        foreach (DwmThumbnail thumbnail in _thumbnails)
            thumbnail.Dispose();
        _thumbnails.Clear();
        _toolbars.Clear();
        _cells.Children.Clear();
    }
}
