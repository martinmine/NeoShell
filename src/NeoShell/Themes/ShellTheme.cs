using Microsoft.UI.Xaml;
using NeoShell.Settings;
using Windows.UI;

namespace NeoShell.Themes;

/// <summary>
/// How all of NeoShell looks. A theme is mostly its resource dictionary, <c>Themes/&lt;Kind&gt;.xaml</c>: the views
/// take their logos, headings, decorations and control styles from it by key (Windows11.xaml has every key a theme
/// must define), and it may restyle WinUI's own controls. What code needs to know is here.
/// </summary>
/// <param name="Theme">Light or dark whatever Windows uses; null follows Windows' mode.</param>
/// <param name="ShowsAccentColor">Surfaces take Windows' accent colour when it's shown on Start and taskbar.</param>
/// <param name="BackdropTint">The colour of every acrylic surface; null for the theme's grey.</param>
/// <param name="RoundedCorners">Popup windows get Windows 11's rounded corners.</param>
public sealed record ShellTheme(
    ThemeKind Kind,
    string Name,
    ElementTheme? Theme,
    bool ShowsAccentColor,
    Color? BackdropTint,
    bool RoundedCorners)
{
    public static readonly ShellTheme Windows11 = new(ThemeKind.Windows11, "Windows 11", null, true, null, true);

    public static readonly ShellTheme DarkCyber = new(
        ThemeKind.DarkCyber, "Dark Cyber", ElementTheme.Dark, false, Color.FromArgb(0xFF, 0x05, 0x05, 0x07), false);

    public static IReadOnlyList<ShellTheme> All { get; } = [Windows11, DarkCyber];

    /// <summary>The theme NeoShell started with; it changes only with a restart.</summary>
    public static ShellTheme Current { get; private set; } = Windows11;

    public static ShellTheme For(ThemeKind kind) => All.FirstOrDefault(theme => theme.Kind == kind) ?? Windows11;

    /// <summary>
    /// Makes <paramref name="kind"/> current and adds its resources to the app's. Before any window is created: views
    /// look the theme's resources up as they load.
    /// </summary>
    public static void Apply(ThemeKind kind, Application app)
    {
        Current = For(kind);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"ms-appx:///Themes/{Current.Kind}.xaml") });
    }

    /// <summary>The surfaces' light or dark mode. Re-read it on <c>WM_SETTINGCHANGE</c>.</summary>
    public ElementTheme ReadTheme() => Theme ?? SystemTheme.Read();

    /// <summary>The surfaces' colour, when the theme shows Windows' accent colour on them; otherwise null.</summary>
    public Color? ReadAccent() => ShowsAccentColor ? SystemTheme.ReadAccent() : null;
}
