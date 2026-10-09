using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Notifications;

/// <summary>
/// An acrylic surface of its own, as Explorer draws the notification center, the calendar and each toast: frameless
/// with Windows 11's rounded corners and border, in the topmost band, sliding in from the side of the screen.
/// </summary>
internal sealed class PanelWindow : Window
{
    private readonly ShellBackdrop _backdrop = new(Backdrop.Acrylic);
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly WindowSlide _slide;
    private readonly FrameworkElement _content;

    /// <param name="activates">
    /// False for toasts: clicking one mustn't take the focus from the app the user is in.
    /// </param>
    public PanelWindow(FrameworkElement content, bool activates)
    {
        _content = content;
        Content = content;
        SystemBackdrop = _backdrop;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(Handle, activates ? ExtendedWindowStyles.ToolWindow : ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate);
        _frameless = new FramelessWindow(Handle, roundedCorners: true);
        _placement = new PinnedWindow(Handle, default, PinnedLayer.Topmost);
        _slide = new WindowSlide(_placement);

        Closed += (_, _) =>
        {
            _slide.Stop();
            _placement.Dispose();
            _frameless.Dispose();
        };
    }

    public nint Handle { get; }

    public bool IsShown { get; private set; }

    public RectInt32 ScreenBounds => _placement.Bounds;

    /// <param name="accent">The panel's colour when Windows shows the accent colour on Start and taskbar.</param>
    public void SetTheme(ElementTheme theme, Color? accent)
    {
        _content.RequestedTheme = accent is { } color ? SystemTheme.ThemeOn(color) : theme;
        _backdrop.Theme = _content.RequestedTheme;
        _backdrop.Tint = accent;
    }

    /// <summary>
    /// Lets a toast take the keyboard after all, for its text box: a no-activate window can't become active, and only
    /// the active window gets the keys. The user just clicked it, so Windows lets it take the foreground.
    /// </summary>
    public void TakeKeyboard()
    {
        WindowStyles.RemoveExtended(Handle, ExtendedWindowStyles.NoActivate);
        TopLevelWindows.Activate(Handle);
    }

    /// <summary>The content's height in effective pixels at this width, up to <paramref name="maxHeight"/>.</summary>
    public double MeasureHeight(double width, double maxHeight)
    {
        _content.Measure(new Size(width, maxHeight));
        return Math.Min(maxHeight, _content.DesiredSize.Height);
    }

    /// <summary>Shows the window at <paramref name="bounds"/>, sliding in from <paramref name="fromX"/>.</summary>
    public void SlideIn(RectInt32 bounds, int fromX, TimeSpan duration)
    {
        // Opened again while sliding out: from where it is.
        _placement.Bounds = bounds with { X = IsShown || _slide.IsRunning ? _placement.Bounds.X : fromX };
        IsShown = true;
        AppWindow.Show(activateWindow: false);
        _slide.To(bounds, duration, decelerate: true);
    }

    /// <summary>Slides out to <paramref name="toX"/> and hides.</summary>
    public void SlideOut(int toX, TimeSpan duration, Action? hidden = null)
    {
        if (!IsShown)
            return;

        IsShown = false;
        _slide.To(_placement.Bounds with { X = toX }, duration, decelerate: false, () =>
        {
            AppWindow.Hide();
            hidden?.Invoke();
        });
    }

    /// <summary>
    /// Moves or resizes a shown window: at once, or sliding there (a toast making room for a new one). A slide under
    /// way is redirected.
    /// </summary>
    public void Place(RectInt32 bounds, TimeSpan? slide = null)
    {
        if (!IsShown)
            return;

        if (slide is not null || _slide.IsRunning)
            _slide.To(bounds, slide ?? TimeSpan.FromMilliseconds(150), decelerate: true);
        else
            _placement.Bounds = bounds;
    }
}
