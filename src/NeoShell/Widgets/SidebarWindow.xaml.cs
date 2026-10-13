using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Widgets;

/// <summary>
/// The widget sidebar: a strip along the right of the primary monitor, from the top to the taskbar, with the
/// taskbar's backdrop. It's part of the desktop: it sits just above it and takes no space, so windows maximize over it.
/// </summary>
internal sealed partial class SidebarWindow : Window
{
    private readonly Sidebar _owner;
    private readonly nint _hwnd;
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly RunMode _runMode;
    private ShellBackdrop _backdrop;
    // The menus keep theirs; a change of theme is applied to it.
    private readonly ShellBackdrop _addMenuBackdrop = new(Backdrop.Acrylic);
    private readonly ShellBackdrop _sidebarMenuBackdrop = new(Backdrop.Acrylic);
    private DisplayMonitor _monitor;
    private double _width;
    private ElementTheme _theme;
    private Color? _accent;
    // The right edge, in screen pixels, while the left one is dragged, and how far in from the left one it was grabbed.
    private int? _resizeRight;
    private int _resizeGrab;
    private bool _panelShown = true;
    // The cards the window is cut to while the panel is hidden, in pixels; null shows all of it.
    private List<RectInt32>? _region;
    // The card showing a widget dragged over the sidebar where it would go (ShowDropSlot), while it's among the cards.
    private WidgetFrame? _slot;
    // The 2088 concept's left edge and title (SetConcept).
    private FrameworkElement? _conceptEdge;
    private FrameworkElement? _conceptTitle;

    /// <param name="panelShown">The sidebar's own backdrop behind the widgets; otherwise only the widgets show.</param>
    public SidebarWindow(Sidebar owner, RunMode runMode, DisplayMonitor monitor, double width, bool panelShown, Backdrop backdrop, ElementTheme theme, Color? accent)
    {
        _owner = owner;
        _runMode = runMode;
        _monitor = monitor;
        InitializeComponent();

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        // Out of Alt+Tab, but it takes the focus when clicked: notes are typed into.
        WindowStyles.AddExtended(_hwnd, ExtendedWindowStyles.ToolWindow);
        // Alt+F4 while it has the focus; the taskbar's menu hides it.
        AppWindow.Closing += (_, e) => e.Cancel = true;
        _frameless = new FramelessWindow(_hwnd);
        // Stays while peeking at the desktop, as gadgets did.
        Peek.Exclude(_hwnd);
        AddMenu.SystemBackdrop = _addMenuBackdrop;
        SidebarMenu.SystemBackdrop = _sidebarMenuBackdrop;
        _backdrop = new ShellBackdrop(backdrop);
        SystemBackdrop = _backdrop;
        WindowTransparency.SetSeeThrough(_hwnd, _backdrop.Kind is Backdrop.Translucent or Backdrop.Transparent);
        SetTheme(theme, accent);

        _placement = new PinnedWindow(_hwnd, default, PinnedLayer.Desktop);
        Place(monitor, width);
        SetPanelShown(panelShown);
        // The cards move with layout (a widget grows, one is added) and with scrolling.
        Root.LayoutUpdated += (_, _) => UpdateRegion();
        Scroller.ViewChanged += (_, _) => UpdateRegion();
    }

    /// <summary>Closes the window (see <see cref="WindowClosing.IgnoreMoves"/>).</summary>
    public void Shut()
    {
        _placement.Dispose();
        _frameless.Dispose();
        WindowClosing.IgnoreMoves(_hwnd);
        Close();
    }

    public DisplayMonitor Monitor => _monitor;

    /// <summary>The width it was last placed at, in effective pixels.</summary>
    public double Width => _width;

    public RectInt32 ScreenBounds => _placement.Bounds;

    public IEnumerable<WidgetFrame> Frames => Cards.Children.OfType<WidgetFrame>();

    /// <summary>Where among the widgets the drop slot is (see <see cref="ShowDropSlot"/>).</summary>
    public int DropSlotIndex { get; private set; }

    /// <summary>Docks the sidebar on <paramref name="monitor"/>, <paramref name="width"/> effective pixels wide.</summary>
    public void Place(DisplayMonitor monitor, double width)
    {
        _monitor = monitor;
        _width = width;
        // As the shell, from what the taskbar and app bars reserved, which Windows may not have taken yet.
        RectInt32 workArea = _runMode == RunMode.Shell ? ShellWorkArea.Get(monitor.Bounds) : monitor.WorkArea;
        _placement.Bounds = SidebarLayout.Bounds(workArea, SidebarLayout.PhysicalWidth(width, monitor.Dpi));
    }

    public void SetTheme(ElementTheme theme, Color? accent)
    {
        _theme = theme;
        _accent = accent;
        Root.RequestedTheme = accent is { } color ? SystemTheme.ThemeOn(color) : theme;
        _backdrop.Theme = Root.RequestedTheme;
        _backdrop.Tint = accent;
        _addMenuBackdrop.Theme = _sidebarMenuBackdrop.Theme = Root.RequestedTheme;
    }

    /// <summary>
    /// The 2088 concept's strip (see docs/design/theme-2088.md): a dark plate the Field shows through (the window's
    /// backdrop is see-through then), a red rule with a ruler's ticks down its left edge and its name at the top. Each
    /// card's look is its frame's (<see cref="WidgetFrame.SetConcept"/>).
    /// </summary>
    public void SetConcept(bool on)
    {
        if (on == (_conceptEdge is not null))
            return;

        if (on)
        {
            Root.Background = Themes.Concept2088.Hud.Solid(Themes.Concept2088.Hud.Void, 0.72);
            _conceptEdge = Themes.Concept2088.HudWidgets.Edge();
            Grid.SetRowSpan(_conceptEdge, 2);
            Root.Children.Insert(0, _conceptEdge);
            _conceptTitle = Themes.Concept2088.HudWidgets.Title();
            Header.Children.Insert(0, _conceptTitle);
        }
        else
        {
            // Transparent, not null: right-clicks between the widgets open the sidebar's menu.
            Root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Transparent);
            Root.Children.Remove(_conceptEdge);
            Header.Children.Remove(_conceptTitle);
            _conceptEdge = _conceptTitle = null;
        }
    }

    /// <summary>
    /// Shows the sidebar's own backdrop behind the widgets, or only the widgets: the window is then cut to its cards,
    /// each keeping the backdrop behind it, and the rest of it is as if it weren't there.
    /// </summary>
    public void SetPanelShown(bool shown)
    {
        _panelShown = shown;
        // Widgets are added from the sidebar's menu then.
        Header.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        UpdateRegion();
    }

    public void SetBackdrop(Backdrop kind)
    {
        if (kind == _backdrop.Kind)
            return;

        SystemBackdrop = _backdrop = new ShellBackdrop(kind);
        WindowTransparency.SetSeeThrough(_hwnd, kind is Backdrop.Translucent or Backdrop.Transparent);
        SetTheme(_theme, _accent);
    }

    // Effective pixels below each card.
    private const double CardGap = 12;

    /// <summary>Puts <paramref name="frame"/> as the <paramref name="index"/>-th widget.</summary>
    public void Insert(WidgetFrame frame, int index)
    {
        Lift(frame, false);
        Cards.Children.Insert(ChildIndex(index), frame);
    }

    /// <summary>
    /// Takes a widget dragged off the sidebar out of the column, leaving no gap, or puts it back. It stays in the tree,
    /// though (without height, invisible): otherwise it would lose the pointer it's being dragged with.
    /// </summary>
    public static void Lift(WidgetFrame frame, bool lifted)
    {
        // Without height rather than drawn over: its view leaves it while the widget is off the sidebar.
        frame.Opacity = lifted ? 0 : 1;
        frame.Height = lifted ? 0 : double.NaN;
        frame.Margin = new Thickness(0, 0, 0, lifted ? 0 : CardGap);
    }

    public void Remove(WidgetFrame frame) => Cards.Children.Remove(frame);

    /// <summary>
    /// Shows <paramref name="slot"/>, the card of the widget being dragged (its own, or a new one for a widget from the
    /// desktop), among the others where it would go if let go at <paramref name="screen"/>; the others slide to make
    /// room. Returns its place among them.
    /// </summary>
    public int ShowDropSlot(PointInt32 screen, WidgetFrame slot)
    {
        if (_slot is not null && _slot != slot)
            HideDropSlot();
        _slot = slot;

        double y = (screen.Y - _placement.Bounds.Y) / Root.XamlRoot.RasterizationScale;
        int from = Cards.Children.IndexOf(slot);
        List<double> middles = [];
        foreach (WidgetFrame frame in Frames)
        {
            if (frame == slot)
                continue;
            // Where the card would be without the slot, so the slot doesn't chase the pointer.
            double top = frame.TransformToVisual(Root).TransformPoint(default).Y;
            if (from >= 0 && Cards.Children.IndexOf(frame) > from)
                top -= slot.ActualHeight + CardGap;
            middles.Add(top + frame.ActualHeight / 2);
        }

        int index = SidebarLayout.DropIndex(middles, y);
        int to = ChildIndex(index);
        if (from < 0)
        {
            Lift(slot, false);
            Cards.Children.Insert(to, slot);
        }
        else if (SidebarLayout.MovedIndex(from, to) != from)
        {
            // Moved rather than taken out and put back: it keeps the pointer it's being dragged with, and its pictures
            // (an element put back in the tree draws them anew, missing for a few frames); it slides there as the
            // others do.
            Cards.Children.Move((uint)from, (uint)SidebarLayout.MovedIndex(from, to));
        }
        DropSlotIndex = index;
        return index;
    }

    /// <summary>Takes a new card shown as the drop slot out again (its widget was dragged off the sidebar).</summary>
    public void HideDropSlot()
    {
        if (_slot is not null)
            Cards.Children.Remove(_slot);
        _slot = null;
    }

    /// <summary>The drop slot stays where it is, as a card (or lifted, its widget dragged off the sidebar).</summary>
    public void EndDropSlot() => _slot = null;

    // Where the index-th widget (the drop slot left out) is among the panel's children.
    private int ChildIndex(int index)
    {
        int widgets = 0;
        for (int i = 0; i < Cards.Children.Count; i++)
        {
            if (Cards.Children[i] is WidgetFrame frame && frame != _slot)
            {
                if (widgets == index)
                    return i;
                widgets++;
            }
        }
        return Cards.Children.Count;
    }

    private void UpdateRegion()
    {
        List<RectInt32>? rects = null;
        if (!_panelShown && Root.XamlRoot is { } root)
        {
            double scale = root.RasterizationScale;
            Rect viewport = Scroller.TransformToVisual(Root).TransformBounds(new Rect(0, 0, Scroller.ActualWidth, Scroller.ActualHeight));
            rects = [];
            foreach (WidgetFrame frame in Frames)
            {
                // Only what's scrolled into view.
                Rect card = frame.TransformToVisual(Root).TransformBounds(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight));
                double top = Math.Max(card.Top, viewport.Top);
                double bottom = Math.Min(card.Bottom, viewport.Bottom);
                if (bottom <= top || card.Width <= 0 || frame.Opacity == 0)
                    continue;
                rects.Add(new RectInt32(
                    (int)Math.Round(card.X * scale),
                    (int)Math.Round(top * scale),
                    (int)Math.Round(card.Width * scale),
                    (int)Math.Round((bottom - top) * scale)));
            }
        }

        if (rects is null ? _region is null : _region is not null && rects.SequenceEqual(_region))
            return;
        _region = rects;
        // Rounded as the cards are (8 epx).
        WindowRegion.SetRoundedRects(_hwnd, rects, (int)Math.Round(8 * (Root.XamlRoot?.RasterizationScale ?? 1)));
    }

    private void Root_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.TryGetPosition(Root, out Point point))
            SidebarMenu.ShowAt(Root, new FlyoutShowOptions { Position = point });
        else
            SidebarMenu.ShowAt(Root);
    }

    private void SidebarMenu_Opening(object sender, object e) => FillAddMenu(AddSubMenu.Items);

    private void WidgetSettings_Click(object sender, RoutedEventArgs e) => _owner.OpenSettings();

    private void AddMenu_Opening(object sender, object e) => FillAddMenu(AddMenu.Items);

    private void AddMenu_Closed(object sender, object e) => AddButton.Opacity = 0;

    private void AddButton_PointerEntered(object sender, PointerRoutedEventArgs e) => AddButton.Opacity = 1;

    private void AddButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!AddMenu.IsOpen)
            AddButton.Opacity = 0;
    }

    private void FillAddMenu(IList<MenuFlyoutItemBase> items)
    {
        items.Clear();
        foreach (WidgetKind kind in Enum.GetValues<WidgetKind>())
        {
            var item = new MenuFlyoutItem
            {
                Text = WidgetView.Title(kind),
                Icon = new FontIcon { Glyph = WidgetView.Glyph(kind) },
                IsEnabled = _owner.CanAdd(kind),
            };
            AutomationProperties.SetAutomationId(item, $"Add{kind}WidgetMenuItem");
            item.Click += (_, _) => _owner.Add(kind);
            items.Add(item);
        }
    }

    private void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _resizeRight = _placement.Bounds.X + _placement.Bounds.Width;
        _resizeGrab = Cursor.Position().X - _placement.Bounds.X;
        Grip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    // Only the window follows the pointer; the width is saved once it's let go.
    private void Grip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeRight is not { } right)
            return;

        int width = Math.Clamp(
            right - (Cursor.Position().X - _resizeGrab),
            SidebarLayout.PhysicalWidth(SidebarLayout.MinWidth, _monitor.Dpi),
            SidebarLayout.PhysicalWidth(SidebarLayout.MaxWidth, _monitor.Dpi));
        _placement.Bounds = _placement.Bounds with { X = right - width, Width = width };
    }

    private void Grip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndResize();
        Grip.ReleasePointerCapture(e.Pointer);
    }

    private void Grip_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndResize();

    private void EndResize()
    {
        if (_resizeRight is null)
            return;

        _resizeRight = null;
        _owner.SetWidth(_placement.Bounds.Width * 96.0 / _monitor.Dpi);
    }
}
