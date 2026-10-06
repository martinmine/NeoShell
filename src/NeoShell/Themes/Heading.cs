using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NeoShell.Themes;

/// <summary>
/// The heading of a section ("Pinned", "Notifications"), drawn by the theme: plain text in Windows 11, a capitalised
/// label with a rule and its <see cref="Subtitle"/> in Dark Cyber.
/// </summary>
internal sealed partial class Heading : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(Heading), new PropertyMetadata(""));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(Heading), new PropertyMetadata(""));

    public Heading() => IsTabStop = false;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>A second name, which a theme may show (Dark Cyber's Japanese).</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
