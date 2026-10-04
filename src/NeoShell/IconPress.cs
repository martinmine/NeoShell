using System.Numerics;
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

    /// <summary>Scales the icon about its centre, animated from wherever it is now.</summary>
    public static void Scale(UIElement icon, float scale)
    {
        if (icon is FrameworkElement element)
            icon.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        icon.ScaleTransition ??= new Vector3Transition { Duration = TimeSpan.FromMilliseconds(120) };
        icon.Scale = new Vector3(scale, scale, 1);
    }

    /// <summary>Shrinks <paramref name="icon"/> while the left button is down on <paramref name="target"/>.</summary>
    public static void Attach(UIElement target, UIElement icon)
    {
        // Handled events too: a button handles its own presses.
        target.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.GetCurrentPoint(target).Properties.IsLeftButtonPressed)
                Scale(icon, Pressed);
        }), handledEventsToo: true);
        var release = new PointerEventHandler((_, _) => Scale(icon, 1));
        target.AddHandler(UIElement.PointerReleasedEvent, release, handledEventsToo: true);
        target.AddHandler(UIElement.PointerCaptureLostEvent, release, handledEventsToo: true);
        target.AddHandler(UIElement.PointerExitedEvent, release, handledEventsToo: true);
    }
}
