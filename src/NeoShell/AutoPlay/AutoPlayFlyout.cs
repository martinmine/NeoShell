using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace NeoShell.AutoPlay;

/// <summary>
/// AutoPlay's choices for a drive, opened from its toast: at the top right of the primary monitor, 5 pixels in from
/// its edges, topmost and in front. It appears and goes at once, without animation; Esc, a click elsewhere or the
/// drive going away closes it, as Explorer's.
/// </summary>
internal sealed class AutoPlayFlyout : Window
{
    // Effective pixels, measured on Explorer's at 100 %.
    private const double FlyoutWidth = 387;
    private const double Gap = 5;

    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly nint _hwnd;
    private bool _closing;

    public AutoPlayFlyout(AutoPlayChooser chooser, DisplayMonitor monitor)
    {
        Title = "AutoPlay";
        Content = chooser;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        WindowStyles.AddExtended(_hwnd, ExtendedWindowStyles.ToolWindow);
        // Square, with its own border: Windows 8's flyouts have neither rounded corners nor a shadow.
        _frameless = new FramelessWindow(_hwnd);

        // At the screen's corner, as the toasts: over the widget sidebar, which Explorer doesn't have.
        double scale = monitor.Dpi / 96.0;
        RectInt32 area = monitor.Bounds;
        int gap = (int)Math.Round(Gap * scale);
        int width = (int)Math.Round(FlyoutWidth * scale);
        chooser.Measure(new Size(FlyoutWidth, double.PositiveInfinity));
        int height = Math.Min((int)Math.Ceiling(chooser.DesiredSize.Height * scale), area.Height - 2 * gap);
        _placement = new PinnedWindow(_hwnd, new RectInt32(area.X + area.Width - gap - width, area.Y + gap, width, height), PinnedLayer.Topmost);

        chooser.KeyDown += Chooser_KeyDown;
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                DispatcherQueue.TryEnqueue(Shut);
        };
        Closed += (_, _) =>
        {
            _placement.Dispose();
            _frameless.Dispose();
        };
    }

    public void Open()
    {
        Activate();
        // WinUI shows the window but may leave the foreground where it was: the click went to the toast, which never
        // takes it.
        TopLevelWindows.Activate(_hwnd);
    }

    /// <summary>Closes it once, whatever closes it first.</summary>
    public void Shut()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }

    private void Chooser_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
            return;
        e.Handled = true;
        Shut();
    }
}
