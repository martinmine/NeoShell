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
            ShowSearchButton = false,
            TrayMode = TrayMode.ShowAll,
            TaskbarBackdrop = Backdrop.Translucent,
            PinnedTaskbarApps = [new PinnedApp("Notepad", AppUserModelId: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App")],
            StartPins =
            [
                new StartPin(new PinnedApp("Tool", Path: @"C:\Tools\tool.exe", Arguments: "--fast")),
                new StartPin(Folder: new StartFolder("f1", "Games", [new PinnedApp("Solitaire", AppUserModelId: "Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe!App")])),
            ],
            StartAppsOpened = ["P~Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"],
            ShowWidgetSidebar = false,
            WidgetSidebarWidth = 400,
            Widgets =
            [
                new WidgetSettings { Kind = WidgetKind.Notes, FontSize = 18 },
                new WidgetSettings { Kind = WidgetKind.Weather, X = -1200, Y = 40, Latitude = 59.9139, Longitude = 10.7522 },
                new WidgetSettings { Kind = WidgetKind.Resources, CpuColor = "#D13438" },
            ],
        };

        _store.Save(settings);
        ShellSettings loaded = _store.Load();

        Assert.Equal(
            settings with { PinnedTaskbarApps = [], StartPins = [], StartAppsOpened = [], Widgets = [], WindowFrameStyles = [], WindowFrameRules = [] },
            loaded with { PinnedTaskbarApps = [], StartPins = [], StartAppsOpened = [], Widgets = [], WindowFrameStyles = [], WindowFrameRules = [] });
        Assert.Equal(settings.StartAppsOpened, loaded.StartAppsOpened);
        Assert.Equal(settings.PinnedTaskbarApps, loaded.PinnedTaskbarApps);
        Assert.Equal(settings.StartPins[0], loaded.StartPins[0]);
        Assert.Equal(settings.StartPins[1].Folder!.Name, loaded.StartPins[1].Folder!.Name);
        Assert.Equal(settings.StartPins[1].Folder!.Apps, loaded.StartPins[1].Folder!.Apps);
        Assert.Equal(settings.Widgets, loaded.Widgets);
    }

    [Fact]
    public void Window_frame_settings_round_trip()
    {
        var settings = new ShellSettings
        {
            WindowFramesEnabled = true,
            WindowFrameStyle = "Mine",
            WindowFrameStyles =
            [
                new FrameStyle
                {
                    Name = "Mine",
                    Backdrop = FrameBackdrop.Acrylic,
                    Theme = FrameTheme.Dark,
                    CaptionColor = "#0054E3",
                    TextColor = "Contrast",
                    BorderColor = "None",
                    Corners = FrameCorners.Square,
                    BasicFrame = true,
                },
            ],
            WindowFrameRules = [new FrameRule("notepad.exe"), new FrameRule("regedit.exe", "RegEdit_RegEdit", "Luna (XP-like)")],
        };

        _store.Save(settings);
        ShellSettings loaded = _store.Load();

        Assert.True(loaded.WindowFramesEnabled);
        Assert.Equal("Mine", loaded.WindowFrameStyle);
        Assert.Equal(settings.WindowFrameStyles, loaded.WindowFrameStyles);
        Assert.Equal(settings.WindowFrameRules, loaded.WindowFrameRules);
        Assert.Contains("\"Backdrop\": \"Acrylic\"", File.ReadAllText(_store.Path));
    }

    [Fact]
    public void Window_frames_are_off_by_default_with_windows_own_frames()
    {
        WriteFile("""{ "WindowFrameStyles": [ { "Name": "Bare" } ], "WindowFrameRules": [ { "ProcessName": "a.exe" } ] }""");

        ShellSettings loaded = _store.Load();

        Assert.False(loaded.WindowFramesEnabled);
        Assert.Equal("Windows default", loaded.WindowFrameStyle);
        Assert.Equal(new FrameStyle { Name = "Bare" }, loaded.WindowFrameStyles[0]);
        Assert.Equal(new FrameRule("a.exe"), loaded.WindowFrameRules[0]);
        Assert.Empty(new ShellSettings().WindowFrameRules);
    }

    [Fact]
    public void Widget_options_left_unset_are_not_written()
    {
        _store.Save(new ShellSettings { Widgets = [new WidgetSettings { Id = "a", Kind = WidgetKind.Profile }] });

        string json = File.ReadAllText(_store.Path);
        Assert.Contains("\"Kind\": \"Profile\"", json);
        Assert.DoesNotContain("Latitude", json);
        Assert.DoesNotContain("IsFloating", json);
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
