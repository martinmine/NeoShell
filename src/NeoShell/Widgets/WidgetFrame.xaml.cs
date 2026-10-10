using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NeoShell.Interop.Windowing;
using Windows.Foundation;
using Windows.Graphics;

namespace NeoShell.Widgets;

internal sealed partial class WidgetFrame : UserControl
{
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

    /// <summary>
    /// Takes the view out as <see cref="Release"/> does, leaving a picture of it in its place, so the frame's window
    /// can stay until the view is drawn in its other place without showing it gone.
    /// </summary>
    public async Task<WidgetView?> ReleaseAsync()
    {
        if (Card.Child != Widget)
            return null;

        double width = Widget.ActualWidth, height = Widget.ActualHeight;
        var picture = new RenderTargetBitmap();
        try
        {
            await picture.RenderAsync(Widget);
        }
        catch (Exception ex) when (ex is ArgumentException or COMException)
        {
            return Release();
        }
        if (Card.Child != Widget)
            return null;
        Card.Child = new Image { Source = picture, Width = width, Height = height, Stretch = Stretch.Fill };
        return Widget;
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
