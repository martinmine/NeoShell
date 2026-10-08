using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Graphics;
using Windows.System;

namespace NeoShell.Snap;

/// <summary>
/// Explorer's Snap layouts flyout, centred under a window's maximize button: shown when the pointer rests on the
/// button (without taking the focus; it goes when the pointer leaves) or for Win+Z (with each layout's number, for the
/// keyboard; Esc or a click elsewhere closes it). A click on a zone, Enter on the chosen one, or a layout's number and
/// then the zone's snaps the window there.
/// </summary>
internal sealed class SnapLayoutsWindow : Window
{
    private static SnapLayoutsWindow? s_open;

    private readonly SnapLayoutPicker _picker;
    private readonly bool _keyboard;
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private int? _pickedLayout;
    private bool _closing;

    /// <summary>The flyout showing now, if any.</summary>
    public static SnapLayoutsWindow? Showing => s_open;

    /// <param name="target">The window to snap, whose maximize button is at <paramref name="button"/> (screen pixels).</param>
    /// <param name="keyboard">Opened by Win+Z: takes the keyboard, and numbers the layouts.</param>
    /// <param name="picked">A zone was chosen: the layout's index and the zone's.</param>
    public static SnapLayoutsWindow Open(nint target, RectInt32 button, DisplayMonitor monitor, IReadOnlyList<SnapChoice> choices,
        IReadOnlyList<ImageSource?> icons, bool keyboard, ElementTheme theme, Action<int, int> picked)
    {
        s_open?.CloseOnce();
        var window = new SnapLayoutsWindow(button, monitor, choices, icons, keyboard, theme, picked);
        s_open = window;
        window.Closed += (_, _) =>
        {
            if (s_open == window)
                s_open = null;
        };
        window.AppWindow.Show(activateWindow: keyboard);
        // WinUI shows the window but leaves the foreground where it was: the keys went to the app in front.
        if (keyboard)
            TopLevelWindows.Activate(window.Handle);
        return window;
    }

    private SnapLayoutsWindow(RectInt32 button, DisplayMonitor monitor, IReadOnlyList<SnapChoice> choices,
        IReadOnlyList<ImageSource?> icons, bool keyboard, ElementTheme theme, Action<int, int> picked)
    {
        _keyboard = keyboard;
        Title = "Snap layouts";
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        SystemBackdrop = new ShellBackdrop(Backdrop.Acrylic) { Theme = theme, Tint = SnapLayoutPicker.BackdropTint(theme), TintOpacities = SnapLayoutPicker.BackdropOpacities };

        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        // The pointer's flyout leaves the focus with the app it's over.
        WindowStyles.AddExtended(Handle, ExtendedWindowStyles.ToolWindow | (keyboard ? ExtendedWindowStyles.None : ExtendedWindowStyles.NoActivate));
        _frameless = new FramelessWindow(Handle, roundedCorners: true);

        _picker = new SnapLayoutPicker(choices, columns: choices.Count > 6 ? 3 : 2, SnapLayoutPicker.FlyoutPadding, theme, icons, numbers: keyboard);
        _picker.Picked += (layout, zone) =>
        {
            CloseOnce();
            picked(layout, zone);
        };
        var root = new ContentControl
        {
            Content = _picker.Root,
            IsTabStop = keyboard,
            UseSystemFocusVisuals = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Opacity = 0,
        };
        root.KeyDown += Root_KeyDown;
        root.Loaded += (_, _) =>
        {
            if (keyboard)
                root.Focus(FocusState.Programmatic);
        };
        // Into place once drawn, fading in over about 100 ms as Explorer's.
        FirstFrame.After(root, () =>
        {
            _placement!.Bounds = ScreenBounds;
            var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(100) };
            Storyboard.SetTarget(fade, root);
            Storyboard.SetTargetProperty(fade, "Opacity");
            new Storyboard { Children = { fade } }.Begin();
        });
        Content = root;

        // Centred under the button, just below it, kept in the work area.
        double scale = monitor.Dpi / 96.0;
        int width = (int)Math.Ceiling(_picker.Size.Width * scale);
        int height = (int)Math.Ceiling(_picker.Size.Height * scale);
        RectInt32 area = monitor.WorkArea;
        // The window's first column is its edge, which Explorer's has a pixel further left.
        int x = Math.Clamp(button.X + button.Width / 2 - width / 2 - 1, area.X, area.X + area.Width - width);
        int y = Math.Clamp(button.Y + button.Height + 1, area.Y, area.Y + area.Height - height);
        ScreenBounds = new RectInt32(x, y, width, height);
        _placement = new PinnedWindow(Handle, ScreenBounds with { X = FirstFrame.OffScreen }, PinnedLayer.Topmost);

        Activated += (_, e) =>
        {
            if (_keyboard && e.WindowActivationState == WindowActivationState.Deactivated)
                DispatcherQueue.TryEnqueue(CloseOnce);
        };
    }

    public nint Handle { get; }

    /// <summary>Where the flyout is, in screen pixels.</summary>
    public RectInt32 ScreenBounds { get; }

    // Deactivation follows every other way of closing.
    public void CloseOnce()
    {
        if (_closing)
            return;
        _closing = true;
        // Let go of the window first: WinUI can crash handling a move of a window it's tearing down.
        _placement.Dispose();
        _frameless.Dispose();
        WindowClosing.IgnoreMoves(Handle);
        Close();
    }

    // The arrows move between zones, Enter picks; a number picks a layout, which then numbers its zones, and the next
    // number picks the zone.
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        IReadOnlyList<SnapChoice> choices = _picker.Choices;
        // Nothing is chosen at first, as in Explorer; the first arrow chooses the first zone.
        if (_picker.Highlighted is null && e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            _picker.Highlight((0, 0));
            return;
        }
        (int layout, int zone) = _picker.Highlighted ?? (0, 0);
        switch (e.Key)
        {
            case VirtualKey.Escape:
                CloseOnce();
                return;
            case VirtualKey.Enter or VirtualKey.Space:
                if (_picker.Highlighted is { } chosen)
                    _picker.Pick(chosen.Choice, chosen.Zone);
                return;
            case VirtualKey.Right:
                _picker.Highlight(zone + 1 < choices[layout].Zones.Count ? (layout, zone + 1) : ((layout + 1) % choices.Count, 0));
                return;
            case VirtualKey.Left:
                int previous = (layout - 1 + choices.Count) % choices.Count;
                _picker.Highlight(zone > 0 ? (layout, zone - 1) : (previous, choices[previous].Zones.Count - 1));
                return;
            case VirtualKey.Down or VirtualKey.Up:
                int next = layout + (e.Key == VirtualKey.Down ? _picker.Columns : -_picker.Columns);
                if (next >= 0 && next < choices.Count)
                    _picker.Highlight((next, Math.Min(zone, choices[next].Zones.Count - 1)));
                return;
        }

        int number = e.Key switch
        {
            >= VirtualKey.Number1 and <= VirtualKey.Number9 => e.Key - VirtualKey.Number0,
            >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => e.Key - VirtualKey.NumberPad0,
            _ => 0,
        };
        if (number == 0)
        {
            e.Handled = false;
            return;
        }
        if (_pickedLayout is not { } picked)
        {
            if (number > choices.Count)
                return;
            _pickedLayout = number - 1;
            _picker.Highlight((number - 1, 0));
        }
        else if (number <= choices[picked].Zones.Count)
        {
            _picker.Pick(picked, number - 1);
        }
    }
}
