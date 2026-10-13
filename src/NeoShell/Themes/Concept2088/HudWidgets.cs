using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NeoShell.Settings;

namespace NeoShell.Themes.Concept2088;

/// <summary>
/// The widget sidebar and floating widgets in the 2088 concept (see docs/design/theme-2088.md): each card a cut-corner
/// panel with a bilingual heading, and the widgets' own text, fills and accent in the concept's palette.
/// </summary>
internal static class HudWidgets
{
    /// <summary>The Japanese beside a widget's name in its heading.</summary>
    public static string Japanese(WidgetKind kind) => kind switch
    {
        WidgetKind.Profile => "操作者",
        WidgetKind.Resources => "資源監視",
        WidgetKind.Pictures => "映像",
        WidgetKind.Media => "再生",
        WidgetKind.Weather => "気象",
        WidgetKind.Notes => "記録",
        WidgetKind.Wireless => "無線機器",
        WidgetKind.Windows => "系統情報",
        _ => "部品",
    };

    /// <summary>A card's heading: a red tick, the name in capitals, its Japanese.</summary>
    public static StackPanel Heading(WidgetKind kind)
    {
        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Margin = new Thickness(14, 11, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        heading.Children.Add(new Rectangle { Width = 3, Height = 10, Fill = Hud.Solid(Hud.Red) });
        heading.Children.Add(Hud.Label(Widgets.WidgetView.Title(kind).ToUpperInvariant(), 10, Hud.Text, spacing: 180, bold: true));
        heading.Children.Add(Hud.Label("//", 10, Hud.Faint, Hud.Mono));
        heading.Children.Add(Hud.Label(Japanese(kind), 10, Hud.Red, Hud.Japanese));
        return heading;
    }

    /// <summary>
    /// What the widgets draw with, in the concept's colours: their text, card and graph fills, the accent (Windows'
    /// accent becomes the signal red) and the font. Put over a card, the widget's ThemeResources resolve to these.
    /// </summary>
    public static ResourceDictionary Palette()
    {
        var palette = new ResourceDictionary
        {
            ["TextFillColorPrimaryBrush"] = Hud.Solid(Hud.Text),
            ["TextFillColorSecondaryBrush"] = Hud.Solid(Hud.Dim),
            ["TextFillColorTertiaryBrush"] = Hud.Solid(Hud.Faint),
            // The card's own fill and outline: the concept's plate is drawn behind it instead.
            ["CardBackgroundFillColorDefaultBrush"] = Hud.Solid(Microsoft.UI.Colors.Transparent),
            ["CardStrokeColorDefaultBrush"] = Hud.Solid(Microsoft.UI.Colors.Transparent),
            ["SubtleFillColorSecondaryBrush"] = Hud.Solid(Hud.Raised),
            ["SolidBackgroundFillColorSecondaryBrush"] = Hud.Solid(Hud.Raised),
            ["ProgressBarBackground"] = Hud.Solid(Hud.Line),
            ["AccentFillColorDefaultBrush"] = Hud.Solid(Hud.Red),
            ["AccentFillColorSecondaryBrush"] = Hud.Solid(Hud.RedHot),
            ["AccentTextFillColorPrimaryBrush"] = Hud.Solid(Hud.Red),
            ["SystemAccentColor"] = Hud.Red,
            ["ContentControlThemeFontFamily"] = Hud.Display,
        };
        return palette;
    }

    /// <summary>The sidebar's left edge: a red rule with a ruler's ticks down it.</summary>
    public static Canvas Edge()
    {
        var edge = new Canvas { Width = 8, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
        edge.SizeChanged += (_, e) =>
        {
            edge.Children.Clear();
            edge.Children.Add(new Rectangle { Width = 1, Height = e.NewSize.Height, Fill = Hud.Solid(Hud.Red, 0.8) });
            for (double y = 0; y < e.NewSize.Height; y += 8)
            {
                bool major = y % 64 == 0;
                edge.Children.Add(new Rectangle
                {
                    Width = major ? 6 : 3,
                    Height = 1,
                    Fill = Hud.Solid(major ? Hud.Red : Hud.Faint, major ? 0.9 : 0.6),
                    RenderTransform = new TranslateTransform { X = 1, Y = y },
                });
            }
        };
        return edge;
    }

    /// <summary>Over the sidebar's add button: what the strip is.</summary>
    public static StackPanel Title()
    {
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        title.Children.Add(Hud.Label("SIDE RAIL", 10, Hud.Dim, Hud.Mono, spacing: 250));
        title.Children.Add(Hud.Label("側面", 10, Hud.Red, Hud.Japanese));
        return title;
    }
}
