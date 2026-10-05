using System.Diagnostics;
using Microsoft.UI;
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
/// taskbar's top edge, with a window region cutting off what's still below that edge.
/// </remarks>
internal static class TaskbarFlyouts
{
    /// <summary>Effective pixels between the taskbar and a flyout.</summary>
    public const double Gap = 12;

    private static readonly TimeSpan s_duration = TimeSpan.FromMilliseconds(200);
    private static readonly HashSet<FlyoutBase> s_prepared = [];
    private static readonly Dictionary<FlyoutBase, FrameworkElement> s_centredOn = [];
    // Centred flyouts that have been moved into place since they opened.
    private static readonly HashSet<FlyoutBase> s_centred = [];
    // Each flyout's popup window, which WinUI keeps between openings.
    private static readonly Dictionary<FlyoutBase, nint> s_windows = [];
    private static Slide? s_slide;
    private static readonly HashSet<FlyoutBase> s_atPointer = [];
    // The windows of menus at the pointer and their submenus, which no other flyout has.
    private static readonly HashSet<nint> s_pointerWindows = [];
    private static nint s_pointerOwner;
    // How far below where WinUI places the open menu at the pointer its windows go, in pixels.
    private static int s_pointerOffset;

    /// <summary>Opens the flyout centred above <paramref name="target"/>, as Explorer opens a jump list.</summary>
    /// <remarks>
    /// WinUI's Top placement puts the flyout's edge, not its middle, at the point it's given, and the width is only
    /// known once it's open; so it opens hidden at the target's centre and is moved by half its width.
    /// </remarks>
    public static void ShowCentered(FlyoutBase flyout, FrameworkElement target)
    {
        s_centredOn[flyout] = target;
        Show(flyout, target, target.ActualWidth / 2, FlyoutPlacementMode.Top);
    }

    /// <summary>
    /// Opens the menu over the taskbar with its bottom-left corner at <paramref name="position"/> (in
    /// <paramref name="target"/>), as Explorer opens the taskbar's own menu. It doesn't slide, and can't open any other way.
    /// </summary>
    /// <remarks>
    /// WinUI keeps popup windows inside the monitor's work area, which leaves out the taskbar; so the menu opens at the
    /// taskbar's top edge, and its windows (its submenus' too) are moved down by the rest whenever WinUI places them.
    /// </remarks>
    public static void ShowAtPointer(FlyoutBase flyout, FrameworkElement target, Point position)
    {
        if (s_atPointer.Add(flyout))
        {
            flyout.ShouldConstrainToRootBounds = false;
            flyout.Opened += (_, _) =>
            {
                // Opened again while open when it's moved.
                CompositionTarget.Rendering -= OffsetFrame;
                CompositionTarget.Rendering += OffsetFrame;
            };
            flyout.Closed += (_, _) => CompositionTarget.Rendering -= OffsetFrame;
        }

        double edge = -Top(target);
        XamlRoot root = target.XamlRoot;
        s_pointerOwner = Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
        s_pointerOffset = (int)Math.Round(Math.Max(0, position.Y - edge) * root.RasterizationScale);
        flyout.ShowAt(target, new FlyoutShowOptions { Position = new Point(position.X, edge), Placement = FlyoutPlacementMode.TopEdgeAlignedLeft });
    }

    // Each of the menu's windows as it turns up: WinUI creates them as the menu and its submenus first open. While it's
    // open, no other flyout of the taskbar is.
    private static void OffsetFrame(object? sender, object e)
    {
        foreach (nint window in PopupWindows.OwnedBy(s_pointerOwner))
        {
            if (PopupWindows.IsShown(window) && !s_windows.ContainsValue(window) && s_pointerWindows.Add(window))
                PopupWindows.Offset(window, () => s_pointerOffset);
        }
    }

    /// <summary>Opens the flyout above <paramref name="target"/>, left edges aligned, as Explorer opens Start's own menu.</summary>
    public static void ShowAboveLeft(FlyoutBase flyout, FrameworkElement target)
    {
        s_centredOn.Remove(flyout);
        Show(flyout, target, 0, FlyoutPlacementMode.TopEdgeAlignedLeft);
    }

    /// <summary>Opens the flyout at the right of the screen, the gap away from its edge, as Quick Settings and the calendar.</summary>
    public static void ShowAtRight(FlyoutBase flyout, FrameworkElement target)
    {
        s_centredOn.Remove(flyout);
        FrameworkElement taskbar = Taskbar(target);
        double right = target.TransformToVisual(taskbar).TransformPoint(default).X;
        Show(flyout, target, taskbar.ActualWidth - Gap - right, FlyoutPlacementMode.TopEdgeAlignedRight);
    }

    private static void Show(FlyoutBase flyout, FrameworkElement target, double x, FlyoutPlacementMode placement)
    {
        Prepare(flyout);
        // The gap above the taskbar's top edge, whatever the target's height.
        flyout.ShowAt(target, new FlyoutShowOptions { Position = new Point(x, Above(target)), Placement = placement });
    }

    private static void Prepare(FlyoutBase flyout)
    {
        if (!s_prepared.Add(flyout))
            return;

        flyout.ShouldConstrainToRootBounds = false;
        flyout.AreOpenCloseAnimationsEnabled = false;
        flyout.Opened += (_, _) => SlideIn(flyout);
        flyout.Closed += (_, _) => s_centred.Remove(flyout);
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
            var slide = new Slide(flyout, taskbarWindow, anchor, taskbarBounds.Y) { Window = window };
            s_slide = slide;

            // Moved into place, it opens again and slides then; until then its window is only kept hidden.
            if (s_centredOn.TryGetValue(flyout, out FrameworkElement? centreOn) && s_centred.Add(flyout) && Centre(flyout, presenter, centreOn))
                slide.HideOnly = true;
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
    private sealed class Slide(FlyoutBase flyout, nint owner, PointInt32 anchor, int edge)
    {
        public FlyoutBase Flyout { get; } = flyout;
        public nint Owner { get; } = owner;
        public PointInt32 Anchor { get; } = anchor;
        public int Edge { get; } = edge;
        public nint Window { get; set; }
        public long Since { get; set; } = Stopwatch.GetTimestamp();
        public int? To { get; set; }
        /// <summary>Where the window was last seen in place, before it's known to stay there.</summary>
        public int? SeenAt { get; set; }
        /// <summary>Only find and hide the window: the flyout is opening again elsewhere.</summary>
        public bool HideOnly { get; set; }
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
            if (slide.HideOnly)
            {
                if (slide.Window != 0 || Stopwatch.GetElapsedTime(slide.Since) > TimeSpan.FromMilliseconds(500))
                    StopSlide();
                return;
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
                slide.To = bounds.Y;
                slide.Since = Stopwatch.GetTimestamp();
                PopupWindows.Place(slide.Window, slide.Edge, slide.Edge);
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

        double progress = Math.Min(1, Stopwatch.GetElapsedTime(slide.Since) / s_duration);
        double eased = 1 - Math.Pow(1 - progress, 3);
        int y = (int)Math.Round(slide.Edge + (to - slide.Edge) * eased);
        PopupWindows.Place(slide.Window, y, progress < 1 ? slide.Edge : null);
        if (progress >= 1)
            StopSlide();
    }

    private static void StopSlide()
    {
        CompositionTarget.Rendering -= SlideFrame;
        s_slide = null;
    }

    // Opens the flyout again with its left edge where the centred flyout's goes; an open flyout given a new position
    // moves there. False when it's already in place.
    private static bool Centre(FlyoutBase flyout, FrameworkElement presenter, FrameworkElement target)
    {
        FrameworkElement taskbar = Taskbar(target);
        double targetLeft = target.TransformToVisual(taskbar).TransformPoint(default).X;
        double left = presenter.TransformToVisual(taskbar).TransformPoint(default).X;
        double wanted = Math.Clamp(
            targetLeft + target.ActualWidth / 2 - presenter.ActualWidth / 2,
            Gap,
            Math.Max(Gap, taskbar.ActualWidth - Gap - presenter.ActualWidth));
        if (Math.Abs(wanted - left) < 1)
            return false;

        flyout.ShowAt(target, new FlyoutShowOptions { Position = new Point(wanted - targetLeft, Above(target)), Placement = FlyoutPlacementMode.Top });
        return true;
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
