using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>
/// A widget's content. One is made when the widget is shown and goes with it, as it is, when it moves between the
/// sidebar and the desktop (from one window's <see cref="WidgetFrame"/> to the other's), so it shows the same thing
/// without loading again. Its options live in its <see cref="Settings"/>, and what must outlast a session in a file of
/// its own (a note's text).
/// </summary>
internal partial class WidgetView : UserControl
{
    public WidgetSettings Settings { get; protected set; } = new();

    /// <summary>The widget changed its own settings (from its settings flyout), for the sidebar to save them.</summary>
    public event Action<WidgetSettings>? SettingsChanged;

    /// <summary>
    /// The name in the add menu, in the settings flyout and for UI Automation. Not shown on the widget: the ones that
    /// show a heading have it in their own XAML.
    /// </summary>
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
        _ => kind.ToString(),
    };

    /// <summary>Segoe Fluent Icons glyph for the add menu.</summary>
    public static string Glyph(WidgetKind kind) => kind switch
    {
        WidgetKind.Profile => "",
        WidgetKind.Resources => "",
        WidgetKind.Pictures => "",
        WidgetKind.Media => "",
        WidgetKind.Weather => "",
        WidgetKind.Notes => "",
        WidgetKind.Wireless => "",
        WidgetKind.Windows => "",
        _ => "",
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

    /// <summary>
    /// Content reaches the top right, under the settings and close buttons (a list's values, a note's text): they show
    /// only while the pointer is near them, not anywhere over the widget, so they don't hide it while it's used.
    /// </summary>
    public virtual bool ContentUnderButtons => false;

    /// <summary>The controls of the settings flyout, built each time it opens.</summary>
    public virtual FrameworkElement CreateSettings() => new StackPanel();

    /// <summary>The widget is going away (closed, or the sidebar hidden): stop timers and let go of events.</summary>
    public virtual void Close()
    {
    }

    protected void SaveSettings(WidgetSettings settings)
    {
        Settings = settings;
        SettingsChanged?.Invoke(settings);
    }
}
