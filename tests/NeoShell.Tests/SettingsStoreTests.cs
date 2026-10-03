using NeoShell.Settings;

namespace NeoShell.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NeoShell.Tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _store;

    public SettingsStoreTests()
    {
        _store = new SettingsStore(Path.Combine(_directory, "settings.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        Assert.Equal(new ShellSettings(), _store.Load());
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var settings = new ShellSettings
        {
            TaskbarAlignment = TaskbarAlignment.Left,
            CombineButtons = CombineButtons.Never,
            AutoHide = true,
            ShowOnAllDisplays = false,
            TrayMode = TrayMode.ShowAll,
            PinnedTaskbarApps = [new PinnedApp("Notepad", AppUserModelId: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App")],
            PinnedStartApps = [new PinnedApp("Tool", Path: @"C:\Tools\tool.exe", Arguments: "--fast")],
        };

        _store.Save(settings);
        ShellSettings loaded = _store.Load();

        Assert.Equal(settings with { PinnedTaskbarApps = [], PinnedStartApps = [] },
            loaded with { PinnedTaskbarApps = [], PinnedStartApps = [] });
        Assert.Equal(settings.PinnedTaskbarApps, loaded.PinnedTaskbarApps);
        Assert.Equal(settings.PinnedStartApps, loaded.PinnedStartApps);
    }

    [Fact]
    public void Enums_are_saved_as_names()
    {
        _store.Save(new ShellSettings { TaskbarAlignment = TaskbarAlignment.Left });

        Assert.Contains("\"TaskbarAlignment\": \"Left\"", File.ReadAllText(_store.Path));
    }

    [Fact]
    public void Missing_values_get_defaults_and_unknown_values_are_ignored()
    {
        WriteFile("""
            {
              // Comments and trailing commas are fine in a hand-edited file.
              "AutoHide": true,
              "SomethingFromANewerVersion": 42,
            }
            """);

        Assert.Equal(new ShellSettings { AutoHide = true }, _store.Load());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("""{ "TaskbarAlignment": "Diagonal" }""")]
    [InlineData("""{ "PinnedTaskbarApps": null }""")]
    [InlineData("""{ "PinnedTaskbarApps": [ { "Path": "C:\\a.exe" } ] }""")]
    public void Invalid_file_is_moved_to_bak_and_defaults_are_used(string json)
    {
        WriteFile(json);

        Assert.Equal(new ShellSettings(), _store.Load());
        Assert.False(File.Exists(_store.Path));
        Assert.Equal(json, File.ReadAllText(_store.Path + ".bak"));
    }

    [Fact]
    public void Save_replaces_the_existing_file()
    {
        _store.Save(new ShellSettings { AutoHide = true });
        _store.Save(new ShellSettings { AutoHide = false });

        Assert.False(_store.Load().AutoHide);
        Assert.False(File.Exists(_store.Path + ".tmp"));
    }

    [Fact]
    public void Load_makes_the_saved_settings_current()
    {
        _store.Save(new ShellSettings { AutoHide = true });

        _store.Load();

        Assert.True(_store.Current.AutoHide);
    }

    [Fact]
    public void Update_saves_and_raises_changed()
    {
        int changes = 0;
        _store.Changed += () => changes++;

        _store.Update(_store.Current with { TaskbarAlignment = TaskbarAlignment.Left });

        Assert.Equal(1, changes);
        Assert.Equal(TaskbarAlignment.Left, _store.Current.TaskbarAlignment);
        Assert.Equal(TaskbarAlignment.Left, new SettingsStore(_store.Path).Load().TaskbarAlignment);
    }

    [Fact]
    public void Update_with_equal_settings_does_nothing()
    {
        int changes = 0;
        _store.Changed += () => changes++;

        _store.Update(_store.Current with { });

        Assert.Equal(0, changes);
        Assert.False(File.Exists(_store.Path));
    }

    private void WriteFile(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_store.Path, json);
    }
}
