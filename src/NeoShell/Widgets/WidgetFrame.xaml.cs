using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NeoShell.Interop.Windowing;
using NeoShell.Themes.Concept2088;
using Windows.Foundation;
using Windows.Graphics;

namespace NeoShell.Widgets;

internal sealed partial class WidgetFrame : UserControl
{
    // Effective pixels the pointer moves with the button down before a press becomes a drag.
    private const double DragThreshold = 4;

    // How near the buttons, in effective pixels, the pointer brings them up over content under them.
    private const double ButtonsReach = 16;

    // The 2088 concept's card (SetConcept): its plate and heading, and the palette the widget draws with.
    private HudFrame? _hud;
    private FrameworkElement? _heading;
    private ResourceDictionary? _palette;
    // The widget's text set in the concept's type, with the font it had; set again shortly after its layout changes
    // (a widget adds text as it loads and updates).
    private readonly ConditionalWeakTable<DependencyObject, Microsoft.UI.Xaml.Media.FontFamily> _fonted = new();
    private DispatcherQueueTimer? _fontTimer;
    private (PointInt32 Start, Point Grab)? _press;
    private bool _dragging;
    private bool _pointerOver;
    // Anywhere over the widget, even where its buttons don't show: the concept's outline lights up.
    private bool _pointerInside;

    /// <param name="floating">On the desktop: the window is the card, so the frame draws none of its own.</param>
    public WidgetFrame(WidgetView widget, bool floating)
    {
        InitializeComponent();
        Widget = widget;
        Card.Child = widget;
        AutomationProperties.SetAutomationId(this, "Widget");
        AutomationProperties.SetName(this, WidgetView.Title(widget.Settings.Kind));
        if (floating)
        {
            Card.Background = null;
            Card.BorderThickness = new Thickness(0);
            Card.CornerRadius = default;
        }
        if (widget.FillsCard)
        {
            Card.Padding = new Thickness(0);
            Card.BorderThickness = new Thickness(0);
        }
        // Also the moves the widget's own controls take (a resource row is a button).
        if (widget.ContentUnderButtons)
            Root.AddHandler(PointerMovedEvent, new PointerEventHandler((_, e) => SetPointerOver(NearButtons(e))), true);
    }

    public WidgetView Widget { get; }

    /// <summary>
    /// The card as the 2088 concept draws it, floating too (its window is see-through then): a cut-corner plate
    /// with the widget's name over it, and the widget in the concept's colours and type. Off, the theme's card again.
    /// </summary>
    public void SetConcept(bool on)
    {
        if (on == (_hud is not null))
            return;

        if (on)
        {
            _hud = new HudFrame(14, Hud.Panel, Hud.Line) { MarkerLength = 8 };
            _heading = HudWidgets.Heading(Widget.Settings.Kind);
            Root.Children.Insert(0, _hud);
            Root.Children.Insert(2, _heading);
            Resources.MergedDictionaries.Add(_palette = HudWidgets.Palette());
            FontFamily = Hud.Display;
            Foreground = Hud.Solid(Hud.Text);
            if (_fontTimer is null)
            {
                _fontTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
                _fontTimer.Interval = TimeSpan.FromMilliseconds(300);
                _fontTimer.IsRepeating = false;
                _fontTimer.Tick += (_, _) =>
                {
                    if (_hud is not null)
                        HudWidgets.ApplyFont(Card, _fonted);
                };
            }
            LayoutUpdated += OnLayoutUpdated;
            HudWidgets.ApplyFont(Card, _fonted);
            ToolTipService.SetToolTip(SettingsButton, HudWidgets.ToolTip("SETTINGS // 設定"));
            ToolTipService.SetToolTip(CloseButton, HudWidgets.ToolTip("CLOSE // 閉じる"));
        }
        else
        {
            Root.Children.Remove(_hud);
            Root.Children.Remove(_heading);
            Resources.MergedDictionaries.Remove(_palette);
            _hud = null;
            _heading = null;
            _palette = null;
            ClearValue(FontFamilyProperty);
            ClearValue(ForegroundProperty);
            LayoutUpdated -= OnLayoutUpdated;
            _fontTimer?.Stop();
            HudWidgets.RestoreFont(_fonted);
            ToolTipService.SetToolTip(SettingsButton, "Settings");
            ToolTipService.SetToolTip(CloseButton, "Close");
        }
        Widget.SetConcept(on);
        // The plate is drawn behind the card (the palette clears the card's own fill and outline), the widget under
        // the heading.
        Card.Padding = on ? Widget.FillsCard ? new Thickness(1, 32, 1, 1) : new Thickness(14, 34, 14, 14)
            : Widget.FillsCard ? new Thickness(0) : new Thickness(12);
        // The widget's ThemeResources are looked up again only when the theme changes: change it there and back.
        RequestedTheme = ElementTheme.Light;
        RequestedTheme = on ? ElementTheme.Dark : ElementTheme.Default;
    }

    public event Action<WidgetFrame>? CloseRequested;

    /// <summary>
    /// The pointer moved far enough with the button down to drag the widget; <c>grab</c> is where it holds the
    /// widget, in effective pixels from its top-left corner.
    /// </summary>
    public event Action<WidgetFrame, Point>? DragStarted;

    /// <summary>The pointer moved while dragging; where it is on screen, in pixels.</summary>
    public event Action<WidgetFrame, PointInt32>? DragMoved;

    /// <summary>The widget was let go; where the pointer is on screen, in pixels.</summary>
    public event Action<WidgetFrame, PointInt32>? DragEnded;

    /// <summary>
    /// Takes the view out of this frame, for a frame of the widget in its other place (the sidebar or the desktop): it
    /// goes there as it is, without loading again. WinUI moves an element to another window once it has left its old
    /// parent. Null if it was taken already; the frame is left empty.
    /// </summary>
    public WidgetView? Release()
    {
        if (Card.Child != Widget)
            return null;
        Card.Child = null;
        return Widget;
    }

    /// <summary>Whether the view is in this frame (it's not after <see cref="Release"/>).</summary>
    public bool HoldsWidget => Card.Child == Widget;

    /// <summary>Puts the view back after <see cref="Release"/>, from wherever it went since (it must have left there).</summary>
    public void Return() => Card.Child = Widget;

    // Once the layout settles, not on every pass: a drag lays the widget out each frame.
    private void OnLayoutUpdated(object? sender, object e)
    {
        if (_fontTimer is { IsRunning: false })
            _fontTimer.Start();
    }

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        SetPointerOver(NearButtons(e));
    }

    // Anywhere over the widget, unless its content runs under the buttons.
    private bool NearButtons(PointerRoutedEventArgs e)
    {
        if (!Widget.ContentUnderButtons)
            return true;
        Point point = e.GetCurrentPoint(Buttons).Position;
        return point.X >= -ButtonsReach && point.Y <= Buttons.ActualHeight + ButtonsReach;
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        SetPointerOver(false);
    }

    private void SetPointerOver(bool over)
    {
        _pointerOver = over;
        ShowButtons();
    }

    // Kept while the settings are open, though the pointer is over the flyout by then. Hidden, they take no clicks: a
    // pointer already over a widget as the shell starts brings no PointerEntered, and a click on the top right closed
    // the widget through its invisible close button.
    private void ShowButtons()
    {
        bool shown = _pointerOver || SettingsFlyout.IsOpen;
        Buttons.Opacity = shown ? 1 : 0;
        Buttons.IsHitTestVisible = shown;
        _hud?.SetLook(Hud.Panel, shown || _pointerInside ? Hud.Red : Hud.Line);
    }

    private void SettingsFlyout_Opening(object sender, object e)
    {
        var panel = new StackPanel { Width = 280, Spacing = 12 };
        if (_hud is not null)
        {
            // Before the controls go in, so they take the concept's colours as they're made.
            panel.Resources.MergedDictionaries.Add(HudWidgets.Palette());
            panel.RequestedTheme = ElementTheme.Dark;
            panel.Children.Add(HudWidgets.SettingsHeading(Widget.Settings.Kind));
            panel.Loaded += (_, _) => HudWidgets.ApplyFont(panel, new());
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = WidgetView.Title(Widget.Settings.Kind),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
        }
        panel.Children.Add(Widget.CreateSettings());
        SettingsFlyout.FlyoutPresenterStyle = _hud is null ? null : HudWidgets.FlyoutStyle();
        SettingsFlyout.Content = panel;
        ShowButtons();
    }

    private void SettingsFlyout_Closed(object sender, object e) => ShowButtons();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this);

    // Presses the widget's own controls take (buttons, text) don't get here, so those keep working.
    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
            return;

        _press = (Cursor.Position(), e.GetCurrentPoint(Root).Position);
        Root.CapturePointer(e.Pointer);
    }

    // On-screen positions rather than the pointer's in the window: the window itself moves while it's dragged.
    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_press is not { } press)
            return;

        PointInt32 cursor = Cursor.Position();
        if (!_dragging)
        {
            double threshold = DragThreshold * XamlRoot.RasterizationScale;
            if (Math.Abs(cursor.X - press.Start.X) < threshold && Math.Abs(cursor.Y - press.Start.Y) < threshold)
                return;

            _dragging = true;
            DragStarted?.Invoke(this, press.Grab);
        }
        DragMoved?.Invoke(this, cursor);
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag();
        Root.ReleasePointerCapture(e.Pointer);
    }

    private void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void EndDrag()
    {
        bool dragging = _dragging;
        _press = null;
        _dragging = false;
        if (dragging)
            DragEnded?.Invoke(this, Cursor.Position());
    }
}
