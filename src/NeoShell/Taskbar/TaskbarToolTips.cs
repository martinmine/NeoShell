using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using Windows.Graphics;

namespace NeoShell.Taskbar;

/// <summary>
/// The taskbar's tooltips, placed as Explorer's: centred on the pointer, their bottom 12 px above the taskbar wherever
/// on it the pointer is, and kept inside the monitor, over the widget sidebar's space too.
/// </summary>
/// <remarks>
/// WinUI keeps a tooltip inside the monitor's work area, which leaves out the sidebar, and puts it by the pointer's
/// height; so each is moved there by its popup's offset once open, which WinUI then doesn't keep inside.
/// </remarks>
internal static class TaskbarToolTips
{
    /// <summary>Effective pixels between a tooltip's bottom and the taskbar's top edge, measured on Explorer's.</summary>
    private const double Gap = 12;

    /// <summary>Gives <paramref name="element"/> a tooltip placed as Explorer's, or changes the text of the one it has.</summary>
    public static void Set(FrameworkElement element, string text)
    {
        if (ToolTipService.GetToolTip(element) is ToolTip tip)
            tip.Content = text;
        else
            ToolTipService.SetToolTip(element, Create(text));
    }

    /// <summary>A tooltip placed as Explorer's, for <see cref="ToolTipService"/>.</summary>
    public static ToolTip Create(object? content = null)
    {
        var tip = new ToolTip { Content = content };
        tip.Opened += (_, _) => Place(tip);
        tip.SizeChanged += (_, _) => Place(tip);
        return tip;
    }

    /// <summary>Moves an open tooltip of the taskbar to where Explorer shows its own.</summary>
    public static void Place(ToolTip tip)
    {
        // A tooltip opening for the first time is laid out only after Opened; it's placed once it has its size, and
        // again when its size changes (the clock's seconds).
        if (!tip.IsOpen || tip.ActualWidth == 0
            || tip.XamlRoot is not { } root
            || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).FirstOrDefault(popup => popup.Child == tip) is not { } popup)
        {
            return;
        }

        WindowId id = root.ContentIslandEnvironment.AppWindowId;
        RectInt32 window = TopLevelWindows.GetBounds(Win32Interop.GetWindowFromWindowId(id));
        RectInt32 monitor = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).OuterBounds;
        double scale = root.RasterizationScale;
        double pointer = (Cursor.Position().X - window.X) / scale;
        double left = (monitor.X - window.X) / scale, right = (monitor.X + monitor.Width - window.X) / scale;

        // The popup's offsets are where the tooltip is in the taskbar's content: WinUI's popup for it has no parent.
        // Half pixels go left, as Explorer's.
        popup.HorizontalOffset = Math.Max(left, Math.Min(Math.Floor(pointer - tip.ActualWidth / 2), right - tip.ActualWidth));
        popup.VerticalOffset = -Gap - tip.ActualHeight;
    }
}
