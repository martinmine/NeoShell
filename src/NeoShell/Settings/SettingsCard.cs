using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NeoShell.Settings;

/// <summary>
/// A card of Windows 11's Settings app: a glyph, a header with a description under it, and the setting's control (the
/// card's content) on the right. Its look is in SettingsStyles.xaml.
/// </summary>
internal sealed partial class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata("", (d, _) => ((SettingsCard)d).ShowParts()));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata("", (d, _) => ((SettingsCard)d).ShowParts()));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingsCard), new PropertyMetadata("", (d, _) => ((SettingsCard)d).ShowParts()));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ShowParts();
    }

    // An empty description or glyph takes no room, so a header alone is centred.
    private void ShowParts()
    {
        if (GetTemplateChild("DescriptionText") is UIElement description)
            description.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        if (GetTemplateChild("GlyphIcon") is UIElement glyph)
            glyph.Visibility = string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;
    }
}
