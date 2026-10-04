using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Taskbar;

/// <summary>Live previews of a button's windows above the taskbar: click one to switch to it, or close it.</summary>
internal sealed class ThumbnailPopup : Window
{
    // Effective pixels.
    private const double CellWidth = 220;
    private const double MinCellWidth = 120;
    private const double HeaderHeight = 32;
    private const double PreviewHeight = 124;
    private const double Padding = 8;
    private const double Gap = 8;

    private readonly nint _hwnd;
    private readonly WindowTracker _tracker;
    private readonly ShellBackdrop _backdrop = new(Backdrop.Acrylic);
    private readonly Grid _root = new() { Padding = new Thickness(Padding) };
    private readonly StackPanel _cells = new() { Orientation = Orientation.Horizontal, Spacing = Gap };
    private readonly List<DwmThumbnail> _thumbnails = [];
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly WindowSlide _slide;
    private bool _visible;
    private int _taskbarTop;

    /// <param name="taskbar">The taskbar the popup belongs to; it slides out from behind it.</param>
    public ThumbnailPopup(WindowTracker tracker, nint taskbar)
    {
        _tracker = tracker;
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
        _frameless = new FramelessWindow(_hwnd, roundedCorners: true);
        _placement = new PinnedWindow(_hwnd, default, PinnedLayer.Topmost);
        _placement.SetLayer(PinnedLayer.Topmost, above: taskbar);
        _slide = new WindowSlide(_placement);

        Closed += (_, _) =>
        {
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
        _root.RequestedTheme = theme;
        _backdrop.Theme = theme;
        ClearCells();

        double scale = monitor.Dpi / 96.0;
        int count = button.Windows.Count;
        double maxCellWidth = (monitor.WorkArea.Width / scale - 2 * Padding - (count - 1) * Gap) / count;
        double cellWidth = Math.Max(MinCellWidth, Math.Min(CellWidth, maxCellWidth));
        foreach (WindowInfo window in button.Windows)
            _cells.Children.Add(CreateCell(window, cellWidth, theme));

        int width = (int)Math.Ceiling((2 * Padding + count * cellWidth + (count - 1) * Gap) * scale);
        int height = (int)Math.Ceiling((2 * Padding + HeaderHeight + PreviewHeight) * scale);
        int x = Math.Clamp(anchor.X + anchor.Width / 2 - width / 2, monitor.WorkArea.X, monitor.WorkArea.X + monitor.WorkArea.Width - width);
        int y = anchor.Y - height - (int)(8 * scale);
        var bounds = new RectInt32(x, y, width, height);
        // Out of the taskbar, as in Windows 11; once open, it follows the pointer along the taskbar.
        _taskbarTop = anchor.Y;
        if (!_visible)
        {
            _visible = true;
            _placement.Bounds = bounds with { Y = _taskbarTop };
            AppWindow.Show(activateWindow: false);
            _slide.To(bounds, TimeSpan.FromMilliseconds(200), decelerate: true);
        }
        else
        {
            _slide.To(bounds, TimeSpan.FromMilliseconds(150), decelerate: true);
        }
    }

    /// <summary>Slides back into the taskbar; showing again on the way turns it round.</summary>
    public void Hide()
    {
        Button = null;
        if (!_visible)
            return;

        _slide.To(_placement.Bounds with { Y = _taskbarTop }, TimeSpan.FromMilliseconds(120), decelerate: false, () =>
        {
            _visible = false;
            AppWindow.Hide();
            ClearCells();
        });
    }

    private Grid CreateCell(WindowInfo window, double width, ElementTheme theme)
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
        cell.PointerEntered += (_, _) => cell.Background = hover;
        cell.PointerExited += (_, _) => cell.Background = idle;
        cell.PointerPressed += (_, _) => cell.Background = pressed;
        cell.PointerReleased += (_, _) => cell.Background = hover;
        cell.Tapped += (_, _) =>
        {
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
        close.Click += (_, _) => TopLevelWindows.Close(window.Handle);
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        cell.Children.Add(header);

        // The preview is drawn by DWM over this placeholder, once layout says where it is.
        var placeholder = new Border { Margin = new Thickness(6) };
        Grid.SetRow(placeholder, 1);
        cell.Children.Add(placeholder);
        var thumbnail = new DwmThumbnail(_hwnd, window.Handle);
        _thumbnails.Add(thumbnail);
        placeholder.SizeChanged += (_, _) => PlaceThumbnail(thumbnail, placeholder);
        return cell;
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

    private void ClearCells()
    {
        foreach (DwmThumbnail thumbnail in _thumbnails)
            thumbnail.Dispose();
        _thumbnails.Clear();
        _cells.Children.Clear();
    }
}
