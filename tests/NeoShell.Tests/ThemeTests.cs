using NeoShell.Settings;
using NeoShell.Themes;

namespace NeoShell.Tests;

public sealed class ThemeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NeoShell.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Windows_11_is_the_default_and_listed_first()
    {
        Assert.Equal(ShellTheme.Windows11, new ShellSettings().Theme);
        Assert.Equal(ShellTheme.Windows11, ShellThemes.All[0]);
        Assert.Equal("Windows 11", ShellThemes.Name(ShellTheme.Windows11));
    }

    [Fact]
    public void Every_theme_is_listed_once_with_its_own_name()
    {
        ShellTheme[] themes = Enum.GetValues<ShellTheme>();

        Assert.Equal(themes, ShellThemes.All);
        Assert.Equal(themes.Length, themes.Select(ShellThemes.Name).Distinct().Count());
    }

    [Fact]
    public void Settings_from_before_themes_get_windows_11()
    {
        string path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """{ "TaskbarAlignment": "Left" }""");

        ShellSettings settings = new SettingsStore(path).Load();

        Assert.Equal(ShellTheme.Windows11, settings.Theme);
        Assert.Equal(TaskbarAlignment.Left, settings.TaskbarAlignment);
    }

    [Fact]
    public void Theme_is_saved_by_name()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));

        store.Save(new ShellSettings { Theme = ShellTheme.Windows11 });

        Assert.Contains("\"Theme\": \"Windows11\"", File.ReadAllText(store.Path));
        Assert.Equal(ShellTheme.Windows11, store.Load().Theme);
    }
}
