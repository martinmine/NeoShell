using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using Windows.Graphics;
using Windows.System;

namespace NeoShell.Snap;

/// <summary>
/// Win+Z as the shell: Windows 11's Snap layouts for the window in front, near its top right corner, where Explorer
/// shows them under the maximize button. A click on a zone, or a layout's number and then the zone's, puts the window
/// there; Esc or a click elsewhere closes them.
/// </summary>
internal sealed class SnapLayoutsWindow : Window
{
    // Effective pixels.
    private const double PreviewWidth = 96;
    private const double CaptionHeight = 20;
    private const double Spacing = 12;
    private const double Margin = 12;
    private const double ZoneGap = 3;

    private static SnapLayoutsWindow? s_open;

    private readonly nint _target;
    private readonly nint _hwnd;
    private readonly RectInt32 _workArea;
    private readonly IReadOnlyList<IReadOnlyList<SnapZone>> _layouts;
    private readonly List<List<Button>> _zoneButtons = [];
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private int? _pickedLayout;
    private bool _closing;

    /// <summary>Shows the layouts for <paramref name="target"/>; nothing for a window that can't be resized.</summary>
    public static void Open(nint target, ElementTheme theme)
    {
        s_open?.CloseOnce();
        if (target == 0 || !TopLevelWindows.Exists(target) || TopLevelWindows.IsDesktop(target)
            || TopLevelWindows.GetProcessId(target) == Environment.ProcessId || !TopLevelWindows.CanResize(target))
        {
            return;
        }

        s_open = new SnapLayoutsWindow(target, theme);
        s_open.Closed += (_, _) => s_open = null;
        s_open.Activate();
        // WinUI shows the window but leaves the foreground where it was: the keys went to the app in front.
        TopLevelWindows.Activate(s_open._hwnd);
    }

    private SnapLayoutsWindow(nint target, ElementTheme theme)
    {
        _target = target;
        nint monitorHandle = TopLevelWindows.MonitorOf(target);
        IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
        DisplayMonitor monitor = monitors.FirstOrDefault(m => m.Handle == monitorHandle) ?? monitors.First(m => m.IsPrimary);
        _workArea = monitor.WorkArea;
        _layouts = SnapLayouts.For(_workArea, monitor.Dpi);
        Title = "Snap layouts";

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        SystemBackdrop = new ShellBackdrop(Backdrop.Acrylic) { Theme = theme };

        nint hwnd = _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(hwnd, ExtendedWindowStyles.ToolWindow);
        _frameless = new FramelessWindow(hwnd, roundedCorners: true);

        // Previews in the work area's shape, three to a row when there are six of them.
        int columns = _layouts.Count > 4 ? 3 : 2;
        int rows = (_layouts.Count + columns - 1) / columns;
        double previewHeight = Math.Clamp(PreviewWidth * _workArea.Height / _workArea.Width, 40, 160);
        var root = new Grid
        {
            RequestedTheme = theme,
            Padding = new Thickness(Margin),
            ColumnSpacing = Spacing,
            RowSpacing = Spacing - CaptionHeight / 2,
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled,
        };
        for (int i = 0; i < columns; i++)
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PreviewWidth) });
        for (int i = 0; i < rows; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(previewHeight + CaptionHeight) });
        for (int i = 0; i < _layouts.Count; i++)
        {
            FrameworkElement layout = CreateLayout(i, previewHeight);
            Grid.SetColumn(layout, i % columns);
            Grid.SetRow(layout, i / columns);
            root.Children.Add(layout);
        }
        root.KeyDown += Root_KeyDown;
        // The keyboard starts on the first zone, for the arrow keys and Enter.
        root.Loaded += (_, _) => _zoneButtons[0][0].Focus(FocusState.Keyboard);
        Content = root;

        // At the window's top right, below its title bar, kept on the monitor.
        double scale = monitor.Dpi / 96.0;
        int width = (int)Math.Ceiling((2 * Margin + columns * PreviewWidth + (columns - 1) * Spacing) * scale);
        int height = (int)Math.Ceiling((2 * Margin + rows * (previewHeight + CaptionHeight) + (rows - 1) * (Spacing - CaptionHeight / 2)) * scale);
        RectInt32 window = TopLevelWindows.GetVisibleBounds(target);
        int x = Math.Clamp(window.X + window.Width - width - (int)(8 * scale), _workArea.X, _workArea.X + _workArea.Width - width);
        int y = Math.Clamp(window.Y + (int)(40 * scale), _workArea.Y, _workArea.Y + _workArea.Height - height);
        _placement = new PinnedWindow(hwnd, new RectInt32(x, y, width, height), PinnedLayer.Topmost);

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                CloseSoon();
        };
        Closed += (_, _) =>
        {
            _placement.Dispose();
            _frameless.Dispose();
        };
    }

    // A layout: its zones as buttons in the screen's shape, and its number below for the keyboard.
    private StackPanel CreateLayout(int index, double previewHeight)
    {
        var canvas = new Canvas { Width = PreviewWidth, Height = previewHeight };
        var buttons = new List<Button>();
        IReadOnlyList<SnapZone> zones = _layouts[index];
        for (int i = 0; i < zones.Count; i++)
        {
            SnapZone zone = zones[i];
            var button = new Button
            {
                Width = zone.Width * PreviewWidth - ZoneGap,
                Height = zone.Height * previewHeight - ZoneGap,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                FontSize = 12,
            };
            AutomationProperties.SetName(button, $"Layout {index + 1}, zone {i + 1}");
            AutomationProperties.SetAutomationId(button, $"SnapZone{index + 1}_{i + 1}");
            Canvas.SetLeft(button, zone.X * PreviewWidth + ZoneGap / 2);
            Canvas.SetTop(button, zone.Y * previewHeight + ZoneGap / 2);
            // The zone under the pointer or the keyboard takes the accent colour, as Explorer's.
            button.PointerEntered += (_, _) => Highlight(button, true);
            button.PointerExited += (_, _) => Highlight(button, button.FocusState != FocusState.Unfocused);
            button.GotFocus += (_, _) => Highlight(button, true);
            button.LostFocus += (_, _) => Highlight(button, false);
            button.Click += (_, _) => Snap(zone);
            canvas.Children.Add(button);
            buttons.Add(button);
        }
        _zoneButtons.Add(buttons);

        var caption = new TextBlock
        {
            Text = (index + 1).ToString(),
            FontSize = 12,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        return new StackPanel { Spacing = 2, Children = { canvas, caption } };
    }

    private static void Highlight(Button button, bool on)
    {
        if (on)
            button.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        else
            button.ClearValue(Control.BackgroundProperty);
    }

    // A number picks a layout, which then shows its zones' numbers; the next one picks the zone.
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CloseOnce();
            return;
        }

        int number = e.Key switch
        {
            >= VirtualKey.Number1 and <= VirtualKey.Number9 => e.Key - VirtualKey.Number0,
            >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => e.Key - VirtualKey.NumberPad0,
            _ => 0,
        };
        if (number == 0)
            return;

        e.Handled = true;
        if (_pickedLayout is not { } layout)
        {
            if (number > _layouts.Count)
                return;
            _pickedLayout = number - 1;
            List<Button> buttons = _zoneButtons[number - 1];
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].Content = (i + 1).ToString();
            buttons[0].Focus(FocusState.Keyboard);
        }
        else if (number <= _layouts[layout].Count)
        {
            Snap(_layouts[layout][number - 1]);
        }
    }

    private void Snap(SnapZone zone)
    {
        RectInt32 bounds = SnapLayouts.Bounds(zone, _workArea);
        Log.Info($"Snap: 0x{_target:X} to {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y})");
        TopLevelWindows.Place(_target, bounds);
        CloseOnce();
        TopLevelWindows.Activate(_target);
    }

    // Not from inside the activation change itself.
    private void CloseSoon() => DispatcherQueue.TryEnqueue(CloseOnce);

    // Deactivation follows every other way of closing.
    private void CloseOnce()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }
}
