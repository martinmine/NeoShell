using System.Xml.Linq;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.Themes;

namespace NeoShell.Tests;

public sealed class ThemeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static TheoryData<ThemeKind> Kinds => [.. Enum.GetValues<ThemeKind>()];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_is_a_theme_with_its_own_resources(ThemeKind kind)
    {
        Assert.Equal(kind, ShellTheme.For(kind).Kind);
        Assert.True(File.Exists(ThemeFile(kind)), $"Missing {ThemeFile(kind)}");
    }

    // Views look these keys up as they load; a theme that misses one crashes NeoShell at start-up.
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_theme_defines_what_the_views_look_up(ThemeKind kind)
    {
        HashSet<string> defined = Keys(ThemeFile(kind));
        IEnumerable<string> missing = Keys(ThemeFile(ThemeKind.Windows11)).Where(key => !defined.Contains(key));

        Assert.Empty(missing);
    }

    [Fact]
    public void Dark_cyber_is_always_dark_and_ignores_the_accent_colour()
    {
        Assert.Equal(Microsoft.UI.Xaml.ElementTheme.Dark, ShellTheme.DarkCyber.ReadTheme());
        Assert.Null(ShellTheme.DarkCyber.ReadAccent());
        Assert.False(ShellTheme.DarkCyber.RoundedCorners);
    }

    [Fact]
    public void Settings_start_with_the_windows_11_theme()
    {
        Assert.Equal(ThemeKind.Windows11, new ShellSettings().Theme);
    }

    [Fact]
    public void Log_lines_are_kept_and_announced_for_the_event_feed()
    {
        string message = "Theme test " + Guid.NewGuid();
        LogEntry? announced = null;
        void OnWritten(LogEntry entry)
        {
            if (entry.Message == message)
                announced = entry;
        }
        Log.Written += OnWritten;
        try
        {
            Log.Warn(message);
        }
        finally
        {
            Log.Written -= OnWritten;
        }

        Assert.Equal("WARN", announced?.Level);
        Assert.Contains(Log.Recent(), entry => entry.Message == message);
    }

    /// <summary>
    /// The keys a resource dictionary defines at its top level, with those of the dictionaries it merges; a style
    /// without a key counts as its target type.
    /// </summary>
    private static HashSet<string> Keys(string file)
    {
        XElement root = XDocument.Load(file).Root!;
        var keys = new HashSet<string>();
        foreach (XElement element in root.Elements())
        {
            if (element.Attribute(Xaml + "Key") is { } key)
                keys.Add(key.Value);
            else if (element.Name.LocalName == "Style" && element.Attribute("TargetType") is { } target)
                keys.Add("TargetType:" + target.Value);
        }
        foreach (XAttribute source in root.Descendants().Where(e => e.Name.LocalName == "ResourceDictionary").Attributes("Source"))
            keys.UnionWith(Keys(Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileName(source.Value))));
        return keys;
    }

    private static string ThemeFile(ThemeKind kind) => Path.Combine(RepositoryRoot(), "src", "NeoShell", "Themes", $"{kind}.xaml");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "NeoShell.slnx")))
            directory = directory.Parent ?? throw new DirectoryNotFoundException("NeoShell.slnx not found above the test output");
        return directory.FullName;
    }
}
