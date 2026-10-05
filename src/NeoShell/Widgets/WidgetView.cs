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
    public WidgetSettings Settings { get; protected set; } = new();

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
        _ => kind.ToString(),
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
        _ => "\uE74C",
    };

    /// <summary>Whether the sidebar and desktop may show more than one of this kind.</summary>
    public static bool AllowsSeveral(WidgetKind kind) => kind is WidgetKind.Pictures or WidgetKind.Notes;

    /// <summary>The controls of the settings flyout, built each time it opens.</summary>
    public virtual FrameworkElement CreateSettings() => new StackPanel();

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
