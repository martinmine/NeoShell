using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Graphics;

namespace NeoShell.Frames;

/// <summary>
/// A window of NeoShell's own wearing the style being edited, with Windows' standard title bar as most apps have it.
/// <see cref="WindowFrames"/> leaves NeoShell's windows alone, so this one is styled directly.
/// </summary>
internal sealed class FramePreviewWindow : Window
{
    private readonly nint _hwnd;
    private readonly bool _darkModeBefore;
    private FrameAttributes _applied = new();

    /// <param name="bounds">Where it shows, in screen pixels.</param>
    public FramePreviewWindow(RectInt32 bounds)
    {
        Title = "Frame preview";
        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        _darkModeBefore = WindowFrame.ReadDarkMode(_hwnd) ?? false;
        Content = new Grid
        {
            Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
            Padding = new Thickness(24),
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "This window wears the style being edited. Click it to see the active look: Windows draws Mica "
                        + "and Acrylic only on the window in front.",
                },
            },
        };
        AppWindow.MoveAndResize(bounds);
    }

    public void ShowStyle(FrameStyle style)
    {
        FrameAttributes attributes = FrameColors.ToAttributes(style, ImmersiveColors.Get("ImmersiveSystemAccent") ?? 0xFF0078D4);
        WindowFrame.Reset(_hwnd, FrameColors.PartsToReset(_applied, attributes), _darkModeBefore);
        WindowFrame.Apply(_hwnd, attributes);
        _applied = attributes;
    }
}
