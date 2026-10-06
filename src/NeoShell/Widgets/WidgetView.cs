using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>
/// A widget's content. A new one is made each time the widget moves between the sidebar and the desktop, so what it
/// keeps lives in its <see cref="Settings"/>, a file of its own or a service the sidebar shares.
/// </summary>
internal partial class WidgetView : UserControl
{
    public WidgetView()
    {
        Loaded += (_, _) =>
        {
            if (!LoadsContent)
                MarkReady();
        };
    }

    public WidgetSettings Settings { get; protected set; } = new();

    /// <summary>
    /// The widget shows its content: once it's laid out, or for one that loads its content (a picture, the weather)
    /// once that's shown. A widget moving between the sidebar and the desktop is covered with a picture of how it
    /// looked until then (<see cref="WidgetFrame.Cover"/>), so it doesn't show empty and fill in.
    /// </summary>
    public bool IsReady { get; private set; }

    public event Action? Ready;

    /// <summary>The widget changed its own settings (from its settings flyout), for the sidebar to save them.</summary>
    public event Action<WidgetSettings>? SettingsChanged;

    /// <summary>The name in the add menu and in the settings flyout.</summary>
    public static string Title(WidgetKind kind) => kind switch
    {
        WidgetKind.Profile => "Profile",
        WidgetKind.Resources => "Resource usage",
        WidgetKind.Pictures => "Pictures",
        WidgetKind.Media => "Now playing",
        WidgetKind.Weather => "Weather",
        WidgetKind.Notes => "Notes",
        WidgetKind.Wireless => "Wireless devices",
        WidgetKind.Windows => "About Windows",
        WidgetKind.Log => "Event log",
        WidgetKind.System => "System",
        _ => kind.ToString(),
    };

    /// <summary>A second name, which a theme may show beside the title (Dark Cyber's Japanese).</summary>
    public static string Subtitle(WidgetKind kind) => kind switch
    {
        WidgetKind.Profile => "プロフィール",
        WidgetKind.Resources => "リソース",
        WidgetKind.Pictures => "画像",
        WidgetKind.Media => "再生中",
        WidgetKind.Weather => "天気",
        WidgetKind.Notes => "メモ",
        WidgetKind.Wireless => "無線機器",
        WidgetKind.Windows => "ウィンドウズ",
        WidgetKind.Log => "イベントログ",
        WidgetKind.System => "システム",
        _ => "",
    };

    /// <summary>Segoe Fluent Icons glyph for the add menu.</summary>
    public static string Glyph(WidgetKind kind) => kind switch
    {
        WidgetKind.Profile => "\uE77B",
        WidgetKind.Resources => "\uE9D9",
        WidgetKind.Pictures => "\uE91B",
        WidgetKind.Media => "\uE8D6",
        WidgetKind.Weather => "\uE706",
        WidgetKind.Notes => "\uE70B",
        WidgetKind.Wireless => "\uE957",
        WidgetKind.Windows => "\uE770",
        _ => "\uE74C",
    };

    /// <summary>Whether the sidebar and desktop may show more than one of this kind.</summary>
    public static bool AllowsSeveral(WidgetKind kind) => kind is WidgetKind.Pictures or WidgetKind.Notes;

    /// <summary>
    /// The widget can be resized while it floats: its width, and the height of <see cref="ContentHeight"/>'s part.
    /// </summary>
    public virtual bool CanResize => false;

    /// <summary>The height of the part that grows when the widget is resized (a note's text), in effective pixels.</summary>
    public virtual double ContentHeight { get; set; }

    /// <summary>Content that fills the card to its edges (a picture), without the card's padding and border.</summary>
    public virtual bool FillsCard => false;

    /// <summary>The controls of the settings flyout, built each time it opens.</summary>
    public virtual FrameworkElement CreateSettings() => new StackPanel();

    /// <summary>The widget loads what it shows, and says so with <see cref="MarkReady"/>; otherwise it's ready once laid out.</summary>
    protected virtual bool LoadsContent => false;

    /// <summary>The widget's content is shown, or couldn't be loaded.</summary>
    protected void MarkReady()
    {
        if (IsReady)
            return;
        IsReady = true;
        Ready?.Invoke();
    }

    /// <summary>The widget is going away: stop timers and let go of events.</summary>
    public virtual void Close()
    {
    }

    protected void SaveSettings(WidgetSettings settings)
    {
        Settings = settings;
        SettingsChanged?.Invoke(settings);
    }
}
