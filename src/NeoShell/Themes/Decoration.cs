using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NeoShell.Themes;

/// <summary>
/// What a theme draws over a surface (a card, a panel, the taskbar): nothing in Windows 11, corner markers and a
/// labelled frame in Dark Cyber. Lies over the surface's content and takes no input. Its look is a style from the
/// theme (<c>CardDecorationStyle</c>, <c>PanelDecorationStyle</c>…).
/// </summary>
/// <remarks>
/// A template may have the visual states "Hidden" and "Shown"; <see cref="PlayIntro"/> goes through both, so a
/// surface that opens again (Start) can animate in each time.
/// </remarks>
internal sealed partial class Decoration : Control
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Decoration), new PropertyMetadata(""));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(Decoration), new PropertyMetadata(""));

    public Decoration()
    {
        IsHitTestVisible = false;
        IsTabStop = false;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>A second line of the title, which a theme may show (Dark Cyber's Japanese).</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public void PlayIntro()
    {
        VisualStateManager.GoToState(this, "Hidden", false);
        VisualStateManager.GoToState(this, "Shown", true);
    }

    /// <summary>Plays the intro of every decoration in <paramref name="root"/>'s tree, for a surface that opens.</summary>
    public static void PlayIntros(DependencyObject root)
    {
        if (root is Decoration decoration)
            decoration.PlayIntro();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            PlayIntros(VisualTreeHelper.GetChild(root, i));
    }
}
