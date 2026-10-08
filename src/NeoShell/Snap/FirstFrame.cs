using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace NeoShell.Snap;

/// <summary>
/// Waits for a new window's content to be drawn: a WinUI window is black until its first frames are composed, which a
/// flyout shown at once would flash. Snap's windows open off the screen and move into place from here.
/// </summary>
internal static class FirstFrame
{
    // Off every monitor.
    public const int OffScreen = -32000;

    /// <summary>Runs <paramref name="action"/> once <paramref name="content"/> has loaded and a few frames have gone by.</summary>
    public static void After(FrameworkElement content, Action action) => content.Loaded += (_, _) => Next(action);

    /// <summary>Runs <paramref name="action"/> once a few frames have gone by: what changed before has been drawn.</summary>
    public static void Next(Action action)
    {
        int frames = 0;
        void OnRendering(object? sender, object e)
        {
            if (++frames < 3)
                return;
            CompositionTarget.Rendering -= OnRendering;
            action();
        }
        CompositionTarget.Rendering += OnRendering;
    }
}
