using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using Windows.Foundation;
using Windows.Graphics;

namespace NeoShell.Taskbar;

/// <summary>
/// The taskbar's menus and flyouts as Windows 11 shows them: above the taskbar with a gap, sliding up from behind it;
/// the taskbar's own menu at the pointer.
/// </summary>
/// <remarks>
/// Unconstrained, a flyout is a window of its own (<see cref="PopupWindows"/>), owned by the taskbar and so always in
/// front of it, with its acrylic belonging to that window: WinUI's own animation slides it in over the taskbar, and
/// sliding only the content would leave the acrylic standing still. So the window itself slides up from the
/// taskbar's top edge, with a window region cutting off what's still below that edge, and back down when it closes.
/// WinUI places a flyout inside the monitor's work area, which leaves out the taskbar and the widget sidebar; where it
/// goes beyond that, it's moved there by its popup's offset, which WinUI then doesn't keep inside. Not only by moving its
/// window: WinUI wouldn't know, and UI Automation (tests, screen readers) would see the content where WinUI put it.
/// </remarks>
internal static class TaskbarFlyouts
{
    /// <summary>Effective pixels between the taskbar and a flyout.</summary>
    public const double Gap = 12;

    private static readonly TimeSpan s_duration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan s_closeDuration = TimeSpan.FromMilliseconds(150);
    private static readonly HashSet<FlyoutBase> s_prepared = [];
    // Flyouts that slide in and out faster or slower than the rest.
    private static readonly Dictionary<FlyoutBase, (TimeSpan Opening, TimeSpan Closing)> s_durations = [];
    // Where each flyout's left edge goes in the taskbar, given the flyout's width; flyouts WinUI places themselves have none.
    private static readonly Dictionary<FlyoutBase, Func<double, double>> s_lefts = [];
    // Each flyout's popup window, which WinUI keeps between openings.
    private static readonly Dictionary<FlyoutBase, nint> s_windows = [];
    private static Slide? s_slide;
    private static SlideOut? s_slideOut;
    // Flyouts whose slide out is over, closing for real.
    private static readonly HashSet<FlyoutBase> s_slidOut = [];
    private static readonly HashSet<FlyoutBase> s_atPointer = [];
    // The windows of menus at the pointer and their submenus, which no other flyout has.
    private static readonly HashSet<nint> s_pointerWindows = [];
    private static nint s_pointerOwner;
    // How far below where WinUI places them the windows of the open menu at the pointer go, in pixels, until WinUI is
    // told by the menu's popup offset.
    private static int s_pointerOffset;
    // The menu at the pointer, until WinUI is told where it is.
    private static PointerMenu? s_pointerMenu;

    /// <summary>
    /// Where the open flyout's window is on screen, in pixels, as it slides: WinUI doesn't know the window is moving.
    /// </summary>
    public static RectInt32? WindowBounds(FlyoutBase flyout) =>
        flyout.IsOpen && s_windows.TryGetValue(flyout, out nint window) && PopupWindows.IsShown(window) ? PopupWindows.GetBounds(window) : null;

    /// <summary>
    /// Closes the open menus and flyouts of a taskbar window that's about to close, at once and without sliding, and
    /// forgets its flyouts: a slide would go on moving a popup of a closed window and hide its flyout at the end.
    /// </summary>
    public static void CloseAll(XamlRoot root)
    {
        if (s_slide is { } slide && slide.Flyout.XamlRoot == root)
            StopSlide();
        if (s_slideOut is { } slideOut && slideOut.Flyout.XamlRoot == root)
        {
            CompositionTarget.Rendering -= SlideOutFrame;
            s_slideOut = null;
        }
        if (s_pointerOwner == Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId))
        {
            CompositionTarget.Rendering -= PointerFrame;
            s_pointerOwner = 0;
            s_pointerOffset = 0;
            s_pointerMenu = null;
        }
        // Forgotten first: a flyout whose window isn't known closes without sliding out.
        foreach (FlyoutBase flyout in s_prepared.Concat(s_atPointer).Where(flyout => flyout.XamlRoot == root).ToList())
        {
            s_prepared.Remove(flyout);
            s_atPointer.Remove(flyout);
            s_lefts.Remove(flyout);
            s_durations.Remove(flyout);
            s_windows.Remove(flyout);
            s_slidOut.Remove(flyout);
        }
        foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            popup.IsOpen = false;
    }

    /// <summary>
    /// Closes the taskbar's open menus and flyouts, sliding out as ever: the taskbar lost the foreground they took
    /// (see <see cref="TakeForeground"/>), as Explorer's close when another app is clicked.
    /// </summary>
    public static void HideAll(XamlRoot root)
    {
        foreach (FlyoutBase flyout in s_prepared.Concat(s_atPointer).Where(flyout => flyout.IsOpen && flyout.XamlRoot == root).ToList())
            flyout.Hide();
    }

    /// <summary>Opens the flyout centred above <paramref name="target"/>, as Explorer opens a jump list.</summary>
    /// <remarks>
    /// WinUI's Top placement puts the flyout's edge, not its middle, at the point it's given, and the width is only
    /// known once it's open; so it opens hidden at the target's centre and is moved by half its width.
    /// </remarks>
    public static void ShowCentered(FlyoutBase flyout, FrameworkElement target)
    {
        s_lefts[flyout] = width =>
        {
            FrameworkElement taskbar = Taskbar(target);
            double targetLeft = target.TransformToVisual(taskbar).TransformPoint(default).X;
            return Math.Clamp(targetLeft + target.ActualWidth / 2 - width / 2, Gap, Math.Max(Gap, taskbar.ActualWidth - Gap - width));
        };
        Show(flyout, target, target.ActualWidth / 2, FlyoutPlacementMode.Top);
    }

    /// <summary>
    /// Opens the menu over the taskbar with its bottom-left corner at <paramref name="position"/> (in
    /// <paramref name="target"/>), as Explorer opens the taskbar's own menu. It doesn't slide, and can't open any other way.
    /// </summary>
    /// <remarks>
    /// WinUI places a menu inside the monitor's work area, which leaves out the taskbar; so the menu opens at the
    /// taskbar's top edge, and its window is moved down the rest whenever WinUI places it, at once, until its popup's
    /// offset (set once it stays put) has WinUI place it there itself. Its submenus WinUI then places beside it.
    /// </remarks>
    public static void ShowAtPointer(FlyoutBase flyout, FrameworkElement target, Point position)
    {
        if (s_atPointer.Add(flyout))
        {
            flyout.ShouldConstrainToRootBounds = false;
            flyout.Opened += (_, _) =>
            {
                // Opened again while open when it's moved.
                CompositionTarget.Rendering -= PointerFrame;
                CompositionTarget.Rendering += PointerFrame;
            };
            flyout.Closed += (_, _) =>
            {
                CompositionTarget.Rendering -= PointerFrame;
                s_pointerMenu = null;
            };
        }

        double edge = -Top(target);
        XamlRoot root = target.XamlRoot;
        TakeForeground(root);
        s_pointerOwner = Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
        s_pointerOffset = (int)Math.Round(Math.Max(0, position.Y - edge) * root.RasterizationScale);
        s_pointerMenu = new PointerMenu(flyout);
        flyout.ShowAt(target, new FlyoutShowOptions { Position = new Point(position.X, edge), Placement = FlyoutPlacementMode.TopEdgeAlignedLeft });
    }

    /// <summary>A menu at the pointer: its window once found, until WinUI is told where it is.</summary>
    private sealed class PointerMenu(FlyoutBase flyout)
    {
        public FlyoutBase Flyout { get; } = flyout;
        public nint Window { get; set; }
        /// <summary>Where the window was last seen, before it's known to stay there.</summary>
        public int? SeenAt { get; set; }
    }

    // Each of the menu's windows as it turns up (WinUI creates them as the menu and its submenus first open; while it's
    // open, no other flyout of the taskbar is), and the menu's popup offset once it stays put.
    private static void PointerFrame(object? sender, object e)
    {
        foreach (nint window in PopupWindows.OwnedBy(s_pointerOwner))
        {
            if (PopupWindows.IsShown(window) && !s_windows.ContainsValue(window) && s_pointerWindows.Add(window))
                PopupWindows.Offset(window, () => s_pointerOffset);
        }
        if (s_pointerMenu is not { } menu || menu.Flyout.XamlRoot is not { } root)
            return;

        // The menu's is the window that shows first; its submenus open later.
        if (menu.Window == 0)
            menu.Window = PopupWindows.OwnedBy(s_pointerOwner).FirstOrDefault(w => PopupWindows.IsShown(w) && !s_windows.ContainsValue(w));
        if (menu.Window == 0)
            return;
        // Only once it stays put: on a menu's first opening WinUI places the window for a size the menu doesn't keep,
        // then again a frame later.
        int y = PopupWindows.GetBounds(menu.Window).Y;
        if (menu.SeenAt != y)
        {
            menu.SeenAt = y;
            return;
        }
        if (PopupOf(menu.Flyout, root) is { } popup)
        {
            popup.VerticalOffset += s_pointerOffset / root.RasterizationScale;
            s_pointerOffset = 0;
        }
        s_pointerMenu = null;
    }

    private static Popup? PopupOf(FlyoutBase flyout, XamlRoot root) =>
        VisualTreeHelper.GetOpenPopupsForXamlRoot(root).FirstOrDefault(popup => popup.Child is FrameworkElement presenter && IsPresenterOf(presenter, flyout));

    /// <summary>Opens the flyout above <paramref name="target"/>, left edges aligned, as Explorer opens Start's own menu.</summary>
    public static void ShowAboveLeft(FlyoutBase flyout, FrameworkElement target)
    {
        s_lefts.Remove(flyout);
        Show(flyout, target, 0, FlyoutPlacementMode.TopEdgeAlignedLeft);
    }

    /// <summary>Opens the flyout at the right of the screen, the gap away from its edge, as Quick Settings and the calendar.</summary>
    /// <param name="opening">How long it slides in, when not as long as the other flyouts.</param>
    /// <param name="closing">How long it slides out, when not as long as the other flyouts.</param>
    public static void ShowAtRight(FlyoutBase flyout, FrameworkElement target, TimeSpan? opening = null, TimeSpan? closing = null)
    {
        if (opening is not null || closing is not null)
            s_durations[flyout] = (opening ?? s_duration, closing ?? s_closeDuration);
        FrameworkElement taskbar = Taskbar(target);
        s_lefts[flyout] = width => taskbar.ActualWidth - Gap - width;
        double right = target.TransformToVisual(taskbar).TransformPoint(default).X;
        Show(flyout, target, taskbar.ActualWidth - Gap - right, FlyoutPlacementMode.TopEdgeAlignedRight);
    }

    private static void Show(FlyoutBase flyout, FrameworkElement target, double x, FlyoutPlacementMode placement)
    {
        Prepare(flyout);
        // Opened again on its way out: it closes at once and opens afresh.
        if (s_slideOut?.Flyout == flyout)
            FinishSlideOut();
        TakeForeground(target.XamlRoot);
        // The gap above the taskbar's top edge, whatever the target's height.
        flyout.ShowAt(target, new FlyoutShowOptions { Position = new Point(x, Above(target)), Placement = placement });
    }

    // Explorer's flyouts and menus take the foreground, and close when they lose it: a click on another app, even the
    // one in front. The taskbar doesn't take the foreground when clicked, so it takes it for them; it closes them and
    // goes back to not taking it when it loses it (TaskbarWindow's Activated handler). After a click on the taskbar,
    // Windows lets it; after a hotkey (Win+A) the keys went to the app in front, and it takes it as Alt+Tab does.
    private static void TakeForeground(XamlRoot root)
    {
        nint taskbar = Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
        WindowStyles.RemoveExtended(taskbar, ExtendedWindowStyles.NoActivate);
        TopLevelWindows.Activate(taskbar);
        if (TopLevelWindows.GetForeground() != taskbar)
            TopLevelWindows.SwitchTo(taskbar);
    }

    private static void Prepare(FlyoutBase flyout)
    {
        if (!s_prepared.Add(flyout))
            return;

        flyout.ShouldConstrainToRootBounds = false;
        flyout.AreOpenCloseAnimationsEnabled = false;
        flyout.Opened += (_, _) =>
        {
            SlideIn(flyout);
            CloseFocusToolTips(flyout);
        };
        flyout.Closing += (_, e) => StartSlideOut(flyout, e);
    }

    // Opened from the keyboard (Win+A), a flyout puts the keyboard focus on its first control, whose tooltip WinUI then
    // shows; Explorer's show the focus but no tooltip.
    private static void CloseFocusToolTips(FlyoutBase flyout) =>
        flyout.Target?.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (flyout.XamlRoot is not { } root)
                return;
            foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            {
                if (popup.Child is ToolTip tip)
                    tip.IsOpen = false;
            }
        });

    // Cancels the closing, slides the window back behind the taskbar's top edge, and closes it then.
    private static void StartSlideOut(FlyoutBase flyout, FlyoutBaseClosingEventArgs e)
    {
        if (s_slidOut.Remove(flyout) || e.Cancel || s_slideOut?.Flyout == flyout)
            return;
        if (!s_windows.TryGetValue(flyout, out nint window) || !PopupWindows.IsShown(window))
            return;

        int edge;
        if (s_slide is { } slide && slide.Flyout == flyout)
        {
            // Still on its way in, or not even shown yet.
            if (slide.To is null)
                return;
            edge = slide.Edge;
            StopSlide();
        }
        else if (flyout.Target?.XamlRoot is { } root)
        {
            edge = TopLevelWindows.GetBounds(Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId)).Y;
        }
        else
        {
            return;
        }

        e.Cancel = true;
        if (s_slideOut is not null)
            FinishSlideOut();
        s_slideOut = new SlideOut(flyout, window, PopupWindows.GetBounds(window).Y, edge, Stopwatch.GetTimestamp());
        CompositionTarget.Rendering += SlideOutFrame;
    }

    private sealed record SlideOut(FlyoutBase Flyout, nint Window, int From, int Edge, long Since);

    // Accelerating into the taskbar, as Start does.
    private static void SlideOutFrame(object? sender, object e)
    {
        if (s_slideOut is not { } slide)
            return;

        double progress = Math.Min(1, Stopwatch.GetElapsedTime(slide.Since) / Durations(slide.Flyout).Closing);
        int y = (int)Math.Round(slide.From + (slide.Edge - slide.From) * Math.Pow(progress, 3));
        PopupWindows.Place(slide.Window, y, slide.Edge);
        if (progress >= 1)
            FinishSlideOut();
    }

    private static void FinishSlideOut()
    {
        if (s_slideOut is not { } slide)
            return;

        CompositionTarget.Rendering -= SlideOutFrame;
        s_slideOut = null;
        s_slidOut.Add(slide.Flyout);
        slide.Flyout.Hide();
    }

    private static void SlideIn(FlyoutBase flyout)
    {
        if (flyout.Target is not { XamlRoot: { } root } target)
            return;

        foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is not FrameworkElement presenter || !IsPresenterOf(presenter, flyout))
                continue;

            // Where the presenter will be on screen once its window is in place.
            nint taskbarWindow = Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
            RectInt32 taskbarBounds = TopLevelWindows.GetBounds(taskbarWindow);
            double scale = root.RasterizationScale;
            Point centre = presenter.TransformToVisual(Taskbar(target)).TransformPoint(new Point(presenter.ActualWidth / 2, presenter.ActualHeight / 2));
            var anchor = new PointInt32(taskbarBounds.X + (int)(centre.X * scale), taskbarBounds.Y + (int)(centre.Y * scale));

            // Hidden until it slides. Its window is known from an earlier opening, is already there, or turns up in the
            // next frames.
            nint window = s_windows.TryGetValue(flyout, out nint known) && PopupWindows.Exists(known) ? known : NewWindow(taskbarWindow);
            if (window != 0)
            {
                s_windows[flyout] = window;
                Hide(window);
            }
            // Another flyout's slide that's cut short shows its window where it is.
            if (s_slide is { Window: not 0 } previous && previous.Window != window && previous.Flyout != flyout)
                PopupWindows.Place(previous.Window, PopupWindows.GetBounds(previous.Window).Y, null);
            if (s_slide is null)
                CompositionTarget.Rendering += SlideFrame;
            s_slide = new Slide(flyout, popup, presenter, taskbarWindow, anchor, taskbarBounds.Y) { Window = window };
            return;
        }
    }

    // On a flyout's first opening, its window: the taskbar's popup window no other flyout has.
    private static nint NewWindow(nint taskbarWindow) =>
        PopupWindows.OwnedBy(taskbarWindow).FirstOrDefault(w => !s_windows.ContainsValue(w) && !s_pointerWindows.Contains(w));

    // Until it slides: from now on, each time WinUI shows the window too, it's hidden until it's placed.
    private static void Hide(nint window) => PopupWindows.Conceal(window);

    /// <summary>
    /// A flyout's window being found, then waiting to be shown in place (covering <paramref name="anchor"/>), then
    /// sliding up to it from <paramref name="edge"/>.
    /// </summary>
    private sealed class Slide(FlyoutBase flyout, Popup popup, FrameworkElement presenter, nint owner, PointInt32 anchor, int edge)
    {
        public FlyoutBase Flyout { get; } = flyout;
        public Popup Popup { get; } = popup;
        public FrameworkElement Presenter { get; } = presenter;
        public nint Owner { get; } = owner;
        public PointInt32 Anchor { get; } = anchor;
        public int Edge { get; } = edge;
        public nint Window { get; set; }
        public long Since { get; set; } = Stopwatch.GetTimestamp();
        public int? To { get; set; }
        /// <summary>Where the window was last seen in place, before it's known to stay there.</summary>
        public int? SeenAt { get; set; }
    }

    private static void SlideFrame(object? sender, object e)
    {
        if (s_slide is not { } slide)
            return;

        if (slide.To is not { } to)
        {
            if (slide.Window == 0)
            {
                nint window = NewWindow(slide.Owner);
                if (window != 0)
                {
                    slide.Window = s_windows[slide.Flyout] = window;
                    Hide(window);
                }
            }
            RectInt32 bounds = slide.Window != 0 ? PopupWindows.GetBounds(slide.Window) : default;
            if (slide.Window != 0 && PopupWindows.IsShown(slide.Window)
                && slide.Anchor.Y >= bounds.Y && slide.Anchor.Y < bounds.Y + bounds.Height)
            {
                // Only once it stays put: on a menu's first opening WinUI places the window for a size the menu doesn't
                // keep, then again a frame later.
                if (slide.SeenAt != bounds.Y)
                {
                    slide.SeenAt = bounds.Y;
                    return;
                }
                // Across, it goes by its popup's offset (see the remarks above). WinUI moves the window there itself
                // only some frames later, so it starts sliding there at once.
                int shift = Shift(slide);
                if (shift != 0)
                    slide.Popup.HorizontalOffset += shift / slide.Presenter.XamlRoot.RasterizationScale;
                slide.To = bounds.Y;
                slide.Since = Stopwatch.GetTimestamp();
                PopupWindows.Place(slide.Window, slide.Edge, slide.Edge, bounds.X + shift);
            }
            else if (Stopwatch.GetElapsedTime(slide.Since) > TimeSpan.FromMilliseconds(500))
            {
                // Never got there; show it where it is rather than not at all.
                if (slide.Window != 0)
                    PopupWindows.Place(slide.Window, bounds.Y, null);
                StopSlide();
            }
            return;
        }

        double progress = Math.Min(1, Stopwatch.GetElapsedTime(slide.Since) / Durations(slide.Flyout).Opening);
        double eased = 1 - Math.Pow(1 - progress, 3);
        int y = (int)Math.Round(slide.Edge + (to - slide.Edge) * eased);
        PopupWindows.Place(slide.Window, y, progress < 1 ? slide.Edge : null);
        if (progress >= 1)
            StopSlide();
    }

    private static (TimeSpan Opening, TimeSpan Closing) Durations(FlyoutBase flyout) =>
        s_durations.TryGetValue(flyout, out var durations) ? durations : (s_duration, s_closeDuration);

    private static void StopSlide()
    {
        CompositionTarget.Rendering -= SlideFrame;
        s_slide = null;
    }

    // How far right of where WinUI put it the flyout's window goes, in pixels.
    private static int Shift(Slide slide)
    {
        if (!s_lefts.TryGetValue(slide.Flyout, out Func<double, double>? left) || slide.Flyout.Target is not { } target)
            return 0;

        FrameworkElement taskbar = Taskbar(target);
        double actual = slide.Presenter.TransformToVisual(taskbar).TransformPoint(default).X;
        return (int)Math.Round((left(slide.Presenter.ActualWidth) - actual) * taskbar.XamlRoot.RasterizationScale);
    }

    private static bool IsPresenterOf(FrameworkElement presenter, FlyoutBase flyout) => (presenter, flyout) switch
    {
        (FlyoutPresenter p, Flyout f) => ReferenceEquals(p.Content, f.Content),
        (MenuFlyoutPresenter p, MenuFlyout m) => m.Items.Count > 0 && p.Items.Contains(m.Items[0]),
        _ => false,
    };

    // Where a flyout's bottom goes, in the target's coordinates: the gap above the taskbar. WinUI places a flyout by
    // its presenter's size, leaving a margin out, so the gap can't be the presenter's margin.
    private static double Above(FrameworkElement target) => -Top(target) - Gap;

    // How far below the taskbar's top edge the element starts.
    private static double Top(FrameworkElement element) => element.TransformToVisual(Taskbar(element)).TransformPoint(default).Y;

    // The taskbar window's content: flyouts are placed against its edges.
    private static FrameworkElement Taskbar(FrameworkElement element) => (FrameworkElement)element.XamlRoot.Content;
}
