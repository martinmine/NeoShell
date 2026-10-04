using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace NeoShell;

/// <summary>
/// Icons shrink a little while pressed and grow while dragged, as on Windows 11's taskbar and in Start.
/// </summary>
internal static class IconPress
{
    public const float Pressed = 0.8f;
    public const float Dragged = 1.2f;

    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(120);

    // A quick click would turn the shrink round before it shows, so a released icon grows back only once it has
    // shrunk. Kept per icon for as long as the icon lives.
    private static readonly ConditionalWeakTable<UIElement, PressState> States = [];

    /// <summary>Scales the icon about its centre, animated from wherever it is now.</summary>
    public static void Scale(UIElement icon, float scale)
    {
        if (States.TryGetValue(icon, out PressState? state))
            state.GrowTimer.Stop();
        if (icon is FrameworkElement element)
            icon.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        icon.ScaleTransition ??= new Vector3Transition { Duration = Duration };
        icon.Scale = new Vector3(scale, scale, 1);
    }

    /// <summary>Shrinks the icon for a press.</summary>
    public static void Press(UIElement icon)
    {
        Scale(icon, Pressed);
        States.GetValue(icon, CreateState).PressedAt = Environment.TickCount64;
    }

    /// <summary>Grows the icon back after a press, once its shrink has shown.</summary>
    public static void Release(UIElement icon)
    {
        long remaining = States.TryGetValue(icon, out PressState? state)
            ? state.PressedAt + (long)Duration.TotalMilliseconds - Environment.TickCount64
            : 0;
        if (remaining <= 0)
        {
            Scale(icon, 1);
        }
        else if (!state!.GrowTimer.IsRunning)
        {
            state.GrowTimer.Interval = TimeSpan.FromMilliseconds(remaining);
            state.GrowTimer.Start();
        }
    }

    /// <summary>Shrinks <paramref name="icon"/> while the left button is down on <paramref name="target"/>.</summary>
    public static void Attach(UIElement target, UIElement icon)
    {
        // Handled events too: a button handles its own presses.
        target.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.GetCurrentPoint(target).Properties.IsLeftButtonPressed)
                Press(icon);
        }), handledEventsToo: true);
        var release = new PointerEventHandler((_, _) => Release(icon));
        target.AddHandler(UIElement.PointerReleasedEvent, release, handledEventsToo: true);
        target.AddHandler(UIElement.PointerCaptureLostEvent, release, handledEventsToo: true);
        target.AddHandler(UIElement.PointerExitedEvent, release, handledEventsToo: true);
    }

    private static PressState CreateState(UIElement icon)
    {
        DispatcherQueueTimer timer = icon.DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Scale(icon, 1);
        return new PressState(timer);
    }

    private sealed class PressState(DispatcherQueueTimer growTimer)
    {
        public DispatcherQueueTimer GrowTimer { get; } = growTimer;
        public long PressedAt { get; set; }
    }
}
