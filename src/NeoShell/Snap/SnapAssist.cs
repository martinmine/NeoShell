using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NeoShell.Desktop;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using NeoShell.Switcher;
using NeoShell.Taskbar;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace NeoShell.Snap;

/// <summary>
/// Snap Assist, as Explorer's: once a window has snapped, the space its layout leaves shows the other windows as
/// cards on the blurred wallpaper, one zone at a time (the others wait as empty panels). Picking a card (a click, or
/// the arrows and Enter) puts its window in the zone and moves on to the next; Esc, a click elsewhere or running out
/// of windows ends it. The windows placed join the snapped one in a snap group.
/// </summary>
/// <remarks>
/// One window over the monitor's work area, kept shown for good and cut to the zones it offers by its region (nothing
/// in between, so it neither shows nor takes the mouse): a WinUI window that's new, shown again or resized shows
/// black until it's drawn again (about 150 ms, over a good part of the screen here), and WinUI doesn't draw a window
/// that's hidden, off the screen or cloaked. It draws the cards before its region shows them.
/// </remarks>
internal sealed class SnapAssist : Window
{
    // Effective pixels, measured on Explorer's: panels 12 inside their zone, rounded 8; the chosen card ringed in the
    // accent colour 3 off it.
    private const double Inset = 12;
    private const double RingGap = 3;
    private const double RingThickness = 3;
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(300);

    private readonly WindowTracker _tracker;
    private readonly Func<nint, WallpaperWindow?> _wallpaperOn;
    private readonly nint _hwnd;
    private readonly ContentControl _root = new()
    {
        IsTabStop = true,
        UseSystemFocusVisuals = false,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly WindowEvents _foreground = new(WindowEvent.Foreground, WindowEvent.Foreground);
    private readonly List<Card> _cards = [];
    private IReadOnlyList<SwitcherSlot> _slots = [];
    // What's being offered: the monitor, the zones still empty (the first is offered), the window snapped, the theme,
    // and where the windows come from and go.
    private DisplayMonitor? _monitor;
    private readonly List<RectInt32> _zones = [];
    private nint _snapped;
    private ElementTheme _theme;
    private Func<IReadOnlyList<WindowInfo>> _candidates = () => [];
    private Action<nint, RectInt32> _place = (_, _) => { };
    private RectInt32 _bounds;
    private int _selected;
    private long _fadeStart;
    private bool _showing;
    // The panels and cards, which fade in over the wallpaper, and whether the region shows them yet.
    private Canvas? _overlay;
    private bool _shown;
    // Counts each showing, so a hide waiting for its see-through frame doesn't hide the next showing.
    private int _version;

    /// <param name="wallpaperOn">The wallpaper's window on a monitor, whose picture shows behind the cards.</param>
    public SnapAssist(WindowTracker tracker, Func<nint, WallpaperWindow?> wallpaperOn)
    {
        _tracker = tracker;
        _wallpaperOn = wallpaperOn;
        // Explorer's window's name, for UI Automation.
        Title = "Snap Assist";
        Content = _root;
        // It fades in over what's there, as Explorer's.
        SystemBackdrop = new ShellBackdrop(Backdrop.Transparent);
        _root.KeyDown += Root_KeyDown;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(_hwnd, ExtendedWindowStyles.ToolWindow);
        Peek.Exclude(_hwnd);
        _frameless = new FramelessWindow(_hwnd);
        IReadOnlyList<DisplayMonitor> monitors = ShellWorkArea.Monitors();
        _bounds = (monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0]).WorkArea;
        _placement = new PinnedWindow(_hwnd, _bounds, PinnedLayer.Normal);

        // A click on another window ends it; so does activating anything else.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                DispatcherQueue.TryEnqueue(Dismiss);
        };
        _foreground.Raised += (_, hwnd) =>
        {
            if (hwnd != _snapped)
                Dismiss();
        };
        tracker.Changed += OnWindowsChanged;

        WindowRegion.SetRoundedRects(_hwnd, [], 0);
        AppWindow.Show(activateWindow: false);
    }

    /// <summary>Offers the windows for the zones (screen pixels) left by <paramref name="snapped"/>, in order.</summary>
    /// <param name="candidates">The windows that could go in a zone, most recently used first.</param>
    /// <param name="place">Puts a window in a zone, as Snap does.</param>
    public void Offer(DisplayMonitor monitor, IReadOnlyList<RectInt32> zones, nint snapped, ElementTheme theme,
        Func<IReadOnlyList<WindowInfo>> candidates, Action<nint, RectInt32> place)
    {
        Dismiss();
        if (zones.Count == 0 || candidates().Count == 0)
            return;
        _monitor = monitor;
        _zones.Clear();
        _zones.AddRange(zones);
        _snapped = snapped;
        _theme = theme;
        _candidates = candidates;
        _place = place;
        _selected = 0;
        _showing = true;
        _version++;
        ShowZone();
    }

    /// <summary>Ends it, if it's showing.</summary>
    public void Dismiss()
    {
        if (!_showing)
            return;
        _showing = false;
        CompositionTarget.Rendering -= OnFadeFrame;
        ClearCards();
        WindowRegion.SetRoundedRects(_hwnd, [], 0);
        _shown = false;
        _overlay = null;
        _root.Content = null;
    }

    /// <summary>Closes the window for good.</summary>
    public void Shut()
    {
        _showing = false;
        _tracker.Changed -= OnWindowsChanged;
        CompositionTarget.Rendering -= OnFadeFrame;
        _foreground.Dispose();
        ClearCards();
        // Let go of the window first: WinUI can crash handling a move of a window it's tearing down.
        _placement.Dispose();
        _frameless.Dispose();
        WindowClosing.IgnoreMoves(_hwnd);
        Close();
    }

    // Lays out the first zone left with the windows that could go there, and the rest as empty panels.
    private void ShowZone()
    {
        IReadOnlyList<WindowInfo> windows = _candidates();
        if (_monitor is not { } monitor || _zones.Count == 0 || windows.Count == 0)
        {
            Dismiss();
            return;
        }

        double scale = monitor.Dpi / 96.0;
        // A new monitor's size is the one change of size, while nothing shows.
        if (monitor.WorkArea != _bounds)
        {
            WindowRegion.SetRoundedRects(_hwnd, [], 0);
            _bounds = monitor.WorkArea;
            _placement.Bounds = _bounds;
        }
        bool dark = _theme == ElementTheme.Dark;
        var canvas = new Canvas { RequestedTheme = _theme };

        // The wallpaper as it is behind the zones, sharp around the panels and blurred inside them.
        if (_wallpaperOn(monitor.Handle)?.CopyPicture() is { } picture)
        {
            Canvas.SetLeft(picture, (monitor.Bounds.X - _bounds.X) / scale);
            Canvas.SetTop(picture, (monitor.Bounds.Y - _bounds.Y) / scale);
            canvas.Children.Add(picture);
        }
        else
        {
            canvas.Background = new SolidColorBrush(Colors.Black);
        }

        ClearCards();
        bool first = !_shown;
        var overlay = new Canvas { Opacity = first ? 0 : 1 };
        canvas.Children.Add(overlay);
        _overlay = overlay;
        for (int i = 0; i < _zones.Count; i++)
        {
            Rect panel = PanelOf(_zones[i], scale);
            var border = new Border
            {
                Width = panel.Width,
                Height = panel.Height,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0, 0, 0)),
                Background = new AcrylicBrush
                {
                    TintColor = dark ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20) : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3),
                    TintOpacity = 0.15,
                    TintLuminosityOpacity = 0.1,
                    FallbackColor = dark ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20) : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3),
                },
            };
            Canvas.SetLeft(border, panel.X);
            Canvas.SetTop(border, panel.Y);
            overlay.Children.Add(border);
        }
        AddCards(overlay, windows, PanelOf(_zones[0], scale), scale);
        _root.Content = canvas;
        _selected = Math.Clamp(_selected, 0, _cards.Count - 1);

        if (!first)
        {
            ShowZones();
            return;
        }
        // Over every other window but the one just snapped, as Explorer's.
        _placement.SetLayer(PinnedLayer.Normal, above: _snapped);
        int version = _version;
        FirstFrame.Next(() =>
        {
            if (!_showing || version != _version)
                return;
            ShowZones();
            _shown = true;
            _placement.SetLayer(PinnedLayer.Normal, above: _snapped);
            // The keys went to the snapped window, not to NeoShell: take the foreground as Alt+Tab does.
            TopLevelWindows.SwitchTo(_hwnd);
            _root.Focus(FocusState.Programmatic);
            StartFade();
        });
    }

    // The window shows the zones left, and takes the mouse there only.
    private void ShowZones() =>
        WindowRegion.SetRoundedRects(_hwnd, [.. _zones.Select(z => z with { X = z.X - _bounds.X, Y = z.Y - _bounds.Y })], 0);

    private Rect PanelOf(RectInt32 zone, double scale) => new(
        (zone.X - _bounds.X) / scale + Inset, (zone.Y - _bounds.Y) / scale + Inset,
        Math.Max(0, zone.Width / scale - 2 * Inset), Math.Max(0, zone.Height / scale - 2 * Inset));

    // The windows' cards in rows in the middle of the panel, each the window's title over its live preview.
    private void AddCards(Canvas canvas, IReadOnlyList<WindowInfo> windows, Rect panel, double scale)
    {
        bool dark = _theme == ElementTheme.Dark;
        var cardBrush = new SolidColorBrush(dark ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20) : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
        var ring = new SolidColorBrush((Color)Application.Current.Resources[dark ? "SystemAccentColorLight2" : "SystemAccentColorDark1"]);
        var transparent = new SolidColorBrush(Colors.Transparent);

        foreach (WindowInfo window in windows)
            _cards.Add(new Card(window, TryRegister(window.Handle)));
        (double preview, IReadOnlyList<SwitcherSlot> slots, double gridWidth) = SnapAssistPlan.Arrange(
            [.. _cards.Select(Aspect)], panel.Width, panel.Height);
        _slots = slots;
        int rows = slots.Count == 0 ? 0 : slots.Max(s => s.Row) + 1;
        double cardHeight = SnapAssistPlan.HeaderHeight + preview;
        double gridHeight = rows * cardHeight + (rows - 1) * SnapAssistPlan.Spacing;
        double left = panel.X + (panel.Width - gridWidth) / 2;
        double top = panel.Y + (panel.Height - gridHeight) / 2;
        const double ringOffset = RingGap + RingThickness;

        for (int i = 0; i < _cards.Count; i++)
        {
            Card card = _cards[i];
            SwitcherSlot slot = slots[i];
            double x = left + slot.X;
            double y = top + slot.Row * (cardHeight + SnapAssistPlan.Spacing);

            var selection = new Border
            {
                Width = slot.Width + 2 * ringOffset,
                Height = cardHeight + 2 * ringOffset,
                CornerRadius = new CornerRadius(8 + ringOffset),
                BorderBrush = ring,
                BorderThickness = new Thickness(RingThickness),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            Canvas.SetLeft(selection, x - ringOffset);
            Canvas.SetTop(selection, y - ringOffset);
            canvas.Children.Add(selection);
            card.Ring = selection;

            var cell = new Grid
            {
                Width = slot.Width,
                Height = cardHeight,
                CornerRadius = new CornerRadius(8),
                Background = cardBrush,
                RowDefinitions =
                {
                    new RowDefinition { Height = new GridLength(SnapAssistPlan.HeaderHeight) },
                    new RowDefinition(),
                },
            };
            Canvas.SetLeft(cell, x);
            Canvas.SetTop(cell, y);
            AutomationProperties.SetName(cell, card.Window.Title);
            AutomationProperties.SetAutomationId(cell, "SnapAssistItem");

            var header = new Grid { Padding = new Thickness(12, 0, 0, 0), ColumnSpacing = 10 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new Image { Source = _tracker.WindowIcon(card.Window), Width = 16, Height = 16 });
            var title = new TextBlock
            {
                Text = card.Window.Title,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            var close = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 12 },
                Width = 40,
                Height = 39,
                Padding = new Thickness(0),
                Background = transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0, 8, 0, 0),
                // Only over the card, as in Explorer.
                Opacity = 0,
                IsTabStop = false,
            };
            AutomationProperties.SetName(close, "Close");
            AutomationProperties.SetAutomationId(close, "CloseButton");
            Grid.SetColumn(close, 2);
            header.Children.Add(close);
            cell.Children.Add(header);

            if (card.Thumbnail is null)
            {
                var icon = new Image { Source = _tracker.WindowIcon(card.Window), Width = 48, Height = 48 };
                Grid.SetRow(icon, 1);
                cell.Children.Add(icon);
            }

            Card current = card;
            cell.PointerEntered += (_, _) => close.Opacity = 1;
            cell.PointerExited += (_, _) => close.Opacity = 0;
            cell.Tapped += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && IsWithin(source, close))
                    return;
                Pick(_cards.IndexOf(current));
            };
            close.Click += (_, _) =>
            {
                TopLevelWindows.Close(current.Window.Handle);
                close.IsEnabled = false;
            };
            canvas.Children.Add(cell);

            // DWM draws the live preview under the title, the card's full width.
            if (card.Thumbnail is { } thumbnail)
            {
                thumbnail.Show(new RectInt32(
                    (int)Math.Round(x * scale), (int)Math.Round((y + SnapAssistPlan.HeaderHeight) * scale),
                    (int)Math.Round(slot.Width * scale), (int)Math.Round(preview * scale)));
            }
        }
        ShowSelection(keyboard: false);
    }

    // A window's shape, as its preview draws it; one without a preview shows its icon in a card of the usual shape.
    private static double Aspect(Card card)
    {
        RectInt32 source = card.Thumbnail?.SourceArea ?? new RectInt32(0, 0, card.Thumbnail?.SourceSize.Width ?? 0, card.Thumbnail?.SourceSize.Height ?? 0);
        if (source.Width <= 0 || source.Height <= 0)
            return 16.0 / 10;
        return Math.Clamp((double)source.Width / source.Height, 0.6, 2.4);
    }

    // The arrows move round the cards (and wrap), Home and End jump, Enter picks, Esc ends it.
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        int count = _cards.Count;
        switch (e.Key)
        {
            case VirtualKey.Escape:
                Dismiss();
                TopLevelWindows.Activate(_snapped);
                break;
            case VirtualKey.Enter or VirtualKey.Space:
                Pick(_selected);
                break;
            case VirtualKey.Right:
                Select(AltTabLayout.Step(_selected, count, 1));
                break;
            case VirtualKey.Left:
                Select(AltTabLayout.Step(_selected, count, -1));
                break;
            case VirtualKey.Up or VirtualKey.Down:
                Select(Vertical(e.Key == VirtualKey.Down ? 1 : -1));
                break;
            case VirtualKey.Home:
                Select(0);
                break;
            case VirtualKey.End:
                Select(count - 1);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    // Up and down go round from the top row to the bottom one and back, to the card nearest above or below, as in
    // Explorer.
    private int Vertical(int delta)
    {
        if (_selected < 0 || _selected >= _slots.Count)
            return -1;
        int rows = _slots.Max(s => s.Row) + 1;
        int row = ((_slots[_selected].Row + delta) % rows + rows) % rows;
        double middle = _slots[_selected].X + _slots[_selected].Width / 2;
        int best = _selected;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < _slots.Count; i++)
        {
            double distance = Math.Abs(_slots[i].X + _slots[i].Width / 2 - middle);
            if (_slots[i].Row == row && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _cards.Count)
            return;
        _selected = index;
        ShowSelection(keyboard: true);
    }

    private void ShowSelection(bool keyboard)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            if (_cards[i].Ring is { } ring)
                ring.Visibility = keyboard && i == _selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // The card's window goes into the zone; the next zone follows, or it's done.
    private void Pick(int index)
    {
        if (index < 0 || index >= _cards.Count || _zones.Count == 0)
            return;
        nint window = _cards[index].Window.Handle;
        RectInt32 zone = _zones[0];
        _zones.RemoveAt(0);
        _place(window, zone);
        if (_zones.Count > 0 && _candidates().Count > 0)
        {
            ShowZone();
            return;
        }
        Dismiss();
        TopLevelWindows.Activate(window);
    }

    // Windows that closed meanwhile leave; with none left it ends.
    private void OnWindowsChanged()
    {
        if (!_showing)
            return;
        var open = _tracker.Windows.Select(w => w.Handle).ToHashSet();
        if (_cards.All(card => open.Contains(card.Window.Handle)))
            return;
        ShowZone();
    }

    // Explorer's cards land over about 400 ms; NeoShell's fade in over the wallpaper, previews with them.
    private void StartFade()
    {
        _fadeStart = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnFadeFrame;
    }

    private void OnFadeFrame(object? sender, object e)
    {
        double progress = Math.Min(1, Stopwatch.GetElapsedTime(_fadeStart) / FadeIn);
        double eased = 1 - Math.Pow(1 - progress, 3);
        if (_overlay is not null)
            _overlay.Opacity = eased;
        foreach (Card card in _cards)
        {
            if (card.Thumbnail is { } thumbnail)
                thumbnail.Opacity = eased;
        }
        if (progress >= 1)
            CompositionTarget.Rendering -= OnFadeFrame;
    }

    // Some windows (elevated ones) can't be previewed; they show their icon.
    private DwmThumbnail? TryRegister(nint window)
    {
        try
        {
            var thumbnail = new DwmThumbnail(_hwnd, window) { Opacity = _shown ? 1 : 0 };
            // What's drawn of the window, without the invisible borders round it. DWM keeps the last picture of a
            // minimized window, as it was, which Explorer's cards show too.
            if (!TopLevelWindows.IsMinimized(window))
            {
                RectInt32 outer = TopLevelWindows.GetBounds(window);
                RectInt32 visible = TopLevelWindows.GetVisibleBounds(window);
                thumbnail.SourceArea = new RectInt32(visible.X - outer.X, visible.Y - outer.Y, visible.Width, visible.Height);
            }
            return thumbnail;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ClearCards()
    {
        foreach (Card card in _cards)
            card.Thumbnail?.Dispose();
        _cards.Clear();
        _slots = [];
    }

    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e == ancestor)
                return true;
        }
        return false;
    }

    private sealed class Card(WindowInfo window, DwmThumbnail? thumbnail)
    {
        public WindowInfo Window { get; } = window;
        public DwmThumbnail? Thumbnail { get; } = thumbnail;
        public Border? Ring { get; set; }
    }
}
