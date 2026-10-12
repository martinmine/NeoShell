using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace NeoShell.Taskbar;

/// <summary>
/// A task button's own animations, as Explorer's (Taskbar.View.dll, <c>SharedAnimations</c>; numbers in
/// docs/design/taskbar.md): the pill changing length, the icon dipping when its window is minimized and rising when
/// it's restored, and the icon shrinking while pressed.
/// </summary>
public static class TaskButtonMotion
{
    public const double IconSize = 24;

    /// <summary>The active pill is two thirds of the icon long, a running one a quarter (GetRunningIndicatorExtent).</summary>
    public const double ActivePillWidth = IconSize * 2 / 3, RunningPillWidth = IconSize / 4;

    public static readonly TimeSpan PillDuration = TimeSpan.FromMilliseconds(333);
    public static readonly TimeSpan BounceDelay = TimeSpan.FromMilliseconds(83);
    public static readonly TimeSpan BounceDuration = TimeSpan.FromMilliseconds(667);
    public static readonly TimeSpan PressDuration = TimeSpan.FromMilliseconds(167);
    public static readonly TimeSpan ReleaseDuration = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DragScaleDuration = TimeSpan.FromMilliseconds(333);

    public const float PressedScale = 0.8f, ReleaseOvershoot = 1.02f, ReleaseOvershootAt = 0.33f;

    // When each icon was pressed, and its release waiting for the shrink to finish; kept for as long as the icon lives.
    private static readonly ConditionalWeakTable<UIElement, StrongBox<long>> s_pressedAt = [];
    private static readonly ConditionalWeakTable<UIElement, DispatcherQueueTimer> s_pendingReleases = [];

    /// <summary>
    /// The pill takes its new length at once and is scaled from the old one about its centre: the scale it starts at.
    /// </summary>
    public static float PillStartScale(double fromWidth, double toWidth) => (float)(fromWidth / toWidth);

    /// <summary>
    /// The icon's vertical offset at each key frame: minimized, it dips by an eighth of its size, comes back up past
    /// its place by a 24th and settles; restored, the other way round.
    /// </summary>
    public static (float Progress, float Offset)[] Bounce(bool minimized)
    {
        float direction = minimized ? 1 : -1;
        return [(0.25f, direction * (float)IconSize / 8), (0.5f, -direction * (float)IconSize / 24), (1, 0)];
    }

    /// <summary>Starts the pill that just took over (16 for the active one, 6 for running) from the other's length.</summary>
    public static void MorphPill(UIElement pill, double fromWidth, double toWidth)
    {
        Compositor compositor = CurrentCompositor();
        pill.CenterPoint = new Vector3((float)toWidth / 2, 0, 0);
        Vector3KeyFrameAnimation scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new Vector3(PillStartScale(fromWidth, toWidth), 1, 1));
        scale.InsertKeyFrame(1, Vector3.One, Decelerate(compositor));
        scale.Duration = PillDuration;
        scale.Target = nameof(UIElement.Scale);
        pill.StartAnimation(scale);
    }

    /// <summary>The icon's dip (minimized) or rise (restored) when its window is minimized or restored.</summary>
    public static void BounceIcon(UIElement icon, bool minimized)
    {
        Compositor compositor = CurrentCompositor();
        CompositionEasingFunction[] easings =
        [
            Decelerate(compositor),
            compositor.CreateCubicBezierEasingFunction(new(0.85f, 0), new(0.75f, 1)),
            compositor.CreateCubicBezierEasingFunction(new(0.35f, 0), new(0, 1)),
        ];
        Vector3KeyFrameAnimation bounce = compositor.CreateVector3KeyFrameAnimation();
        (float Progress, float Offset)[] frames = Bounce(minimized);
        for (int i = 0; i < frames.Length; i++)
            bounce.InsertKeyFrame(frames[i].Progress, new Vector3(0, frames[i].Offset, 0), easings[i]);
        bounce.DelayTime = BounceDelay;
        bounce.Duration = BounceDuration;
        bounce.Target = nameof(UIElement.Translation);
        icon.StartAnimation(bounce);
    }

    /// <summary>Shrinks the icon for a press, from wherever it is now.</summary>
    public static void Press(UIElement icon)
    {
        if (s_pendingReleases.TryGetValue(icon, out DispatcherQueueTimer? pending))
            pending.Stop();
        s_pressedAt.AddOrUpdate(icon, new StrongBox<long>(Environment.TickCount64));
        Scale(icon, PressedScale, PressDuration);
    }

    /// <summary>
    /// Grows the icon back after a press, a little past its size first. As in Explorer, only once the shrink has run
    /// its course: a quick click still shows the icon shrinking (recorded: shrunk until ~170 ms after a 40 ms click).
    /// </summary>
    public static void Release(UIElement icon)
    {
        // Not pressed, or taken over by a drag (Scale).
        if (!s_pressedAt.TryGetValue(icon, out StrongBox<long>? pressedAt))
            return;

        long remaining = pressedAt.Value + (long)PressDuration.TotalMilliseconds - Environment.TickCount64;
        if (remaining <= 0)
        {
            Grow(icon);
            return;
        }

        DispatcherQueueTimer timer = s_pendingReleases.GetValue(icon, _ =>
        {
            DispatcherQueueTimer created = icon.DispatcherQueue.CreateTimer();
            created.IsRepeating = false;
            created.Tick += (_, _) => Grow(icon);
            return created;
        });
        timer.Interval = TimeSpan.FromMilliseconds(remaining);
        timer.Start();
    }

    private static void Grow(UIElement icon)
    {
        s_pressedAt.Remove(icon);
        Compositor compositor = CurrentCompositor();
        SetCentre(icon);
        Vector3KeyFrameAnimation grow = compositor.CreateVector3KeyFrameAnimation();
        // The shrink has run its course, so this starts from the pressed size.
        grow.InsertKeyFrame(0, new Vector3(PressedScale, PressedScale, 1));
        grow.InsertKeyFrame(ReleaseOvershootAt, new Vector3(ReleaseOvershoot, ReleaseOvershoot, 1),
            compositor.CreateCubicBezierEasingFunction(new(0.85f, 0), new(0.75f, 1)));
        grow.InsertKeyFrame(1, Vector3.One, compositor.CreateCubicBezierEasingFunction(new(0.35f, 0), new(0, 1)));
        grow.Duration = ReleaseDuration;
        grow.Target = nameof(UIElement.Scale);
        icon.StartAnimation(grow);
    }

    /// <summary>Scales the icon about its centre (1.2 while dragged, 1 after), from wherever it is now.</summary>
    public static void Scale(UIElement icon, float scale)
    {
        s_pressedAt.Remove(icon);
        if (s_pendingReleases.TryGetValue(icon, out DispatcherQueueTimer? pending))
            pending.Stop();
        Scale(icon, scale, DragScaleDuration);
    }

    private static void Scale(UIElement icon, float scale, TimeSpan duration)
    {
        Compositor compositor = CurrentCompositor();
        SetCentre(icon);
        Vector3KeyFrameAnimation animation = compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1, new Vector3(scale, scale, 1), Decelerate(compositor));
        animation.Duration = duration;
        animation.Target = nameof(UIElement.Scale);
        icon.StartAnimation(animation);
    }

    private static void SetCentre(UIElement icon)
    {
        if (icon is FrameworkElement element)
            icon.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
    }

    // Not the element's own visual: once that's asked for, its Scale, Translation and CenterPoint can't be set.
    private static Compositor CurrentCompositor() => CompositionTarget.GetCompositorForCurrentThread();

    // Windows' "fast out, slow in" curve, which most of the taskbar's animations use.
    private static CompositionEasingFunction Decelerate(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(new(0, 0), new(0, 1));
}
