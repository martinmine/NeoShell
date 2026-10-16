using System.Runtime.CompilerServices;
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
            // The settings' controls: their accent parts red, their corners square.
            ["ToggleSwitchFillOn"] = Hud.Solid(Hud.Red),
            ["ToggleSwitchFillOnPointerOver"] = Hud.Solid(Hud.RedHot),
            ["ToggleSwitchFillOnPressed"] = Hud.Solid(Hud.RedHot),
            ["ToggleSwitchStrokeOn"] = Hud.Solid(Hud.Red),
            ["ToggleSwitchKnobFillOn"] = Hud.Solid(Hud.Void),
            ["ToggleSwitchKnobFillOnPointerOver"] = Hud.Solid(Hud.Void),
            ["ToggleSwitchKnobFillOnPressed"] = Hud.Solid(Hud.Void),
            ["TextControlElevationBorderFocusedBrush"] = Hud.Solid(Hud.Red),
            ["TextControlBackground"] = Hud.Solid(Hud.Void),
            ["TextControlBackgroundPointerOver"] = Hud.Solid(Hud.Raised),
            ["TextControlBackgroundFocused"] = Hud.Solid(Hud.Void),
            ["TextControlBorderBrush"] = Hud.Solid(Hud.LineBright),
            ["ComboBoxBackground"] = Hud.Solid(Hud.Void),
            ["ComboBoxBackgroundPointerOver"] = Hud.Solid(Hud.Raised),
            ["ComboBoxBorderBrush"] = Hud.Solid(Hud.LineBright),
            ["ComboBoxBorderBrushPointerOver"] = Hud.Solid(Hud.Red),
            ["ComboBoxItemPillFillBrush"] = Hud.Solid(Hud.Red),
            ["ComboBoxDropDownBackground"] = Hud.Solid(Hud.Panel),
            ["ComboBoxDropDownBorderBrush"] = Hud.Solid(Hud.Red),
            ["ButtonBackground"] = Hud.Solid(Hud.Void),
            ["ButtonBackgroundPointerOver"] = Hud.Solid(Hud.Raised),
            ["ButtonBorderBrush"] = Hud.Solid(Hud.LineBright),
            ["ButtonBorderBrushPointerOver"] = Hud.Solid(Hud.Red),
            ["ControlCornerRadius"] = new CornerRadius(0),
            ["OverlayCornerRadius"] = new CornerRadius(0),
        };
        return palette;
    }

    /// <summary>The settings flyout's plate: Panel, a red outline, square.</summary>
    public static Style FlyoutStyle() => new(typeof(FlyoutPresenter))
    {
        Setters =
        {
            new Setter(Control.BackgroundProperty, Hud.Solid(Hud.Panel)),
            new Setter(Control.BorderBrushProperty, Hud.Solid(Hud.Red)),
            new Setter(Control.BorderThicknessProperty, new Thickness(1)),
            new Setter(Control.CornerRadiusProperty, new CornerRadius(0)),
            new Setter(Control.PaddingProperty, new Thickness(16, 14, 16, 16)),
        },
    };

    /// <summary>
    /// The sidebar's menus (Add widget, its right-click menu): a solid Panel plate with a red outline, square, in place
    /// of the acrylic the theme's menus have.
    /// </summary>
    public static Style MenuStyle() => new(typeof(MenuFlyoutPresenter))
    {
        BasedOn = (Style)Application.Current.Resources["DefaultMenuFlyoutPresenterStyle"],
        Setters =
        {
            new Setter(Control.BackgroundProperty, Hud.Solid(Hud.Panel)),
            new Setter(Control.BorderBrushProperty, Hud.Solid(Hud.Red)),
            new Setter(Control.BorderThicknessProperty, new Thickness(1)),
            new Setter(Control.CornerRadiusProperty, new CornerRadius(0)),
        },
    };

    /// <summary>A tooltip on the concept's plate, in its type: a button's name and its Japanese.</summary>
    public static ToolTip ToolTip(string text) => new()
    {
        Content = text,
        Background = Hud.Solid(Hud.Panel),
        BorderBrush = Hud.Solid(Hud.Red),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(0),
        Foreground = Hud.Solid(Hud.Text),
        FontFamily = Hud.Display,
        CharacterSpacing = 80,
    };

    /// <summary>The settings flyout's heading: the widget's name, then what the flyout is.</summary>
    public static StackPanel SettingsHeading(WidgetKind kind)
    {
        StackPanel heading = Heading(kind);
        heading.Margin = new Thickness(0);
        heading.Children.Add(Hud.Label("· SETTINGS 設定", 10, Hud.Dim, Hud.Japanese, spacing: 100));
        return heading;
    }

    /// <summary>
    /// Sets the concept's type on the text under <paramref name="root"/> that has the theme's (Segoe UI): a text
    /// style sets its font itself, so the text doesn't take the card's. What it changes goes in
    /// <paramref name="changed"/> with the font it had, for <see cref="RestoreFont"/>; icon fonts are left alone.
    /// </summary>
    public static void ApplyFont(DependencyObject root, ConditionalWeakTable<DependencyObject, FontFamily> changed)
    {
        foreach (DependencyObject element in Descendants(root))
        {
            switch (element)
            {
                case TextBlock text when IsThemeFont(text.FontFamily):
                    changed.AddOrUpdate(text, text.FontFamily);
                    text.FontFamily = Hud.Display;
                    break;
                case TextBox box when IsThemeFont(box.FontFamily):
                    changed.AddOrUpdate(box, box.FontFamily);
                    box.FontFamily = Hud.Display;
                    break;
            }
        }
    }

    /// <summary>Gives the text <see cref="ApplyFont"/> changed its font back.</summary>
    public static void RestoreFont(ConditionalWeakTable<DependencyObject, FontFamily> changed)
    {
        foreach ((DependencyObject element, FontFamily font) in changed)
        {
            if (element is TextBlock text)
                text.FontFamily = font;
            else if (element is Control control)
                control.FontFamily = font;
        }
        changed.Clear();
    }

    private static bool IsThemeFont(FontFamily? font) =>
        font?.Source is "XamlAutoFontFamily" or "Segoe UI" or "Segoe UI Variable" or "Segoe UI Variable Text"
            or "Segoe UI Variable Display" or "Segoe UI Variable Small";

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            DependencyObject element = pending.Pop();
            yield return element;
            for (int i = VisualTreeHelper.GetChildrenCount(element) - 1; i >= 0; i--)
                pending.Push(VisualTreeHelper.GetChild(element, i));
        }
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
