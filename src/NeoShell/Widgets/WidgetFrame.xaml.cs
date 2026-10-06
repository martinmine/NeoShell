using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Windowing;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Streams;

namespace NeoShell.Widgets;

/// <summary>A picture of a widget's content as it was shown, in effective pixels.</summary>
internal sealed record WidgetSnapshot(ImageSource Image, double Width, double Height);

internal sealed partial class WidgetFrame : UserControl
{
    // How long a new view stays covered at most, should it never say it's ready (a picture that won't load).
    private static readonly TimeSpan s_coverTimeout = TimeSpan.FromSeconds(2);

    // Effective pixels the pointer moves with the button down before a press becomes a drag.
    private const double DragThreshold = 4;

    private (PointInt32 Start, Point Grab)? _press;
    private bool _dragging;
    private bool _pointerOver;

    /// <param name="floating">On the desktop: the window is the card, so the frame draws none of its own.</param>
    public WidgetFrame(WidgetView widget, bool floating)
    {
        InitializeComponent();
        Widget = widget;
        Card.Child = widget;
        AutomationProperties.SetAutomationId(this, "Widget");
        AutomationProperties.SetName(this, WidgetView.Title(widget.Settings.Kind));
        Chrome.Title = WidgetView.Title(widget.Settings.Kind);
        Chrome.Subtitle = WidgetView.Subtitle(widget.Settings.Kind);
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
    }

    public WidgetView Widget { get; }

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
    /// A picture of the widget taken as it was last pressed, for its next place should the press become a drag. Taken
    /// on the press, while the widget still shows: rendering it takes a while (100 ms and more), and a docked widget
    /// dragged is lifted out of sight at once.
    /// </summary>
    public Task<WidgetSnapshot?> PressSnapshot { get; private set; } = Task.FromResult<WidgetSnapshot?>(null);

    /// <summary>A picture of the widget's content as it shows now; null if it isn't shown.</summary>
    private async Task<WidgetSnapshot?> SnapshotAsync()
    {
        double width = Widget.ActualWidth, height = Widget.ActualHeight;
        if (width <= 0 || height <= 0)
            return null;
        var bitmap = new RenderTargetBitmap();
        try
        {
            await bitmap.RenderAsync(Widget);
            // Copied out: a RenderTargetBitmap shows only in the window it was rendered in, and the picture goes to
            // the widget's next window.
            IBuffer pixels = await bitmap.GetPixelsAsync();
            ImageSource image = AppIcons.ToImageSource(new IconBitmap(bitmap.PixelWidth, bitmap.PixelHeight, pixels.ToArray()));
            return new WidgetSnapshot(image, width, height);
        }
        catch (Exception ex) when (ex is ArgumentException or COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Shows the widget as it looked in its other place (the sidebar or the desktop) until this new view of it is
    /// ready (<see cref="WidgetView.Ready"/>) and drawn: moved, the widget then stays as it was rather than showing
    /// empty and filling in. The view keeps the picture's height meanwhile.
    /// </summary>
    public void Cover(WidgetSnapshot snapshot)
    {
        var image = new Image
        {
            Source = snapshot.Image,
            Width = snapshot.Width,
            Height = snapshot.Height,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            // Where the card puts the view.
            Margin = new Thickness(
                Card.BorderThickness.Left + Card.Padding.Left,
                Card.BorderThickness.Top + Card.Padding.Top,
                0,
                0),
        };
        Root.Children.Insert(Root.Children.IndexOf(Card) + 1, image);
        double minHeight = Widget.MinHeight;
        Widget.MinHeight = snapshot.Height;
        Widget.Opacity = 0;

        DispatcherQueueTimer timeout = DispatcherQueue.CreateTimer();
        timeout.Interval = s_coverTimeout;
        timeout.IsRepeating = false;
        void Uncover()
        {
            timeout.Stop();
            if (!Root.Children.Remove(image))
                return;
            Widget.MinHeight = minHeight;
            Widget.Opacity = 1;
        }
        timeout.Tick += (_, _) => Uncover();
        timeout.Start();
        // Content that's ready may not be drawn yet (a new view's text takes a frame or two): the picture stays until it is.
        if (Widget.IsReady)
            AfterFramesDrawn(Uncover);
        else
            Widget.Ready += () => AfterFramesDrawn(Uncover);
    }

    /// <summary>Runs an action once the next two frames have been drawn: then what's in the tree now shows.</summary>
    public static void AfterFramesDrawn(Action action)
    {
        int frames = 0;
        EventHandler<object>? rendered = null;
        rendered = (_, _) =>
        {
            if (++frames < 2)
                return;
            CompositionTarget.Rendered -= rendered;
            action();
        };
        CompositionTarget.Rendered += rendered;
    }

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e) => SetPointerOver(true);

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e) => SetPointerOver(false);

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
    }

    private void SettingsFlyout_Opening(object sender, object e)
    {
        var panel = new StackPanel { Width = 280, Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = WidgetView.Title(Widget.Settings.Kind),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        panel.Children.Add(Widget.CreateSettings());
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
        PressSnapshot = SnapshotAsync();
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
