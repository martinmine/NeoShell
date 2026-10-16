using System.Text.Json.Nodes;
using NeoShell.Agents;
using NeoShell.Interop.Network;
using NeoShell.Interop.Notifications;
using NeoShell.Interop.Power;
using NeoShell.Interop.Search;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using NeoShell.Taskbar;
using Windows.Graphics;

namespace NeoShell.Tests;

public sealed class AgentDataTests
{
    private static readonly WindowInfo s_window = new(0x1234, "Report.docx - Word", "OpusApp", true, false, true, false, false,
        0, 42, "Microsoft.Office.WINWORD.EXE.15", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE");

    [Theory]
    [InlineData(RunMode.AlongsideExplorer, "alongsideExplorer")]
    [InlineData(RunMode.Shell, "shell")]
    public void Enum_values_are_camel_case(RunMode mode, string name) => Assert.Equal(name, AgentData.Name(mode));

    [Fact]
    public void Monitors_are_numbered_with_their_scale()
    {
        DisplayMonitor[] monitors =
        [
            new(10, new RectInt32(0, 0, 1920, 1080), new RectInt32(0, 0, 1920, 1032), true, 96),
            new(20, new RectInt32(1920, 0, 2560, 1440), new RectInt32(1920, 0, 2560, 1440), false, 144),
        ];

        JsonArray json = AgentData.Monitors(monitors);

        Assert.Equal(1, (int)json[1]!["index"]!);
        Assert.True((bool)json[0]!["primary"]!);
        Assert.Equal(1032, (int)json[0]!["workArea"]!["height"]!);
        Assert.Equal(150, (int)json[1]!["scalePercent"]!);
        Assert.Equal(1, AgentData.MonitorIndex(monitors, 20));
        Assert.Null(AgentData.MonitorIndex(monitors, 30));
    }

    [Fact]
    public void A_window_has_its_app_state_and_place()
    {
        JsonObject json = AgentData.Window(s_window, "Word", foreground: true, minimized: false, maximized: true,
            new RectInt32(0, 0, 1920, 1032), monitor: 0, flashing: false, new TaskProgress(TaskbarProgressState.Normal, 0.456),
            new AppBadge(3, BadgeGlyph.None));

        Assert.Equal(0x1234, (long)json["handle"]!);
        Assert.Equal("Report.docx - Word", (string)json["title"]!);
        Assert.Equal("Word", (string)json["app"]!);
        Assert.Equal("WINWORD.EXE", (string)json["process"]!);
        Assert.Equal(42, (int)json["processId"]!);
        Assert.True((bool)json["foreground"]!);
        Assert.Equal("maximized", (string)json["state"]!);
        Assert.Equal(1920, (int)json["bounds"]!["width"]!);
        Assert.Equal("normal", (string)json["progress"]!["state"]!);
        Assert.Equal(46, (int)json["progress"]!["percent"]!);
        Assert.Equal("3", (string)json["badge"]!);
    }

    [Fact]
    public void A_minimized_window_without_progress_or_badge()
    {
        JsonObject json = AgentData.Window(s_window with { ProcessPath = null }, "Word", foreground: false, minimized: true,
            maximized: true, default, monitor: null, flashing: true, new TaskProgress(TaskbarProgressState.None, 0), null);

        Assert.Equal("minimized", (string)json["state"]!);
        Assert.True((bool)json["flashing"]!);
        Assert.Null(json["process"]);
        Assert.Null(json["monitor"]);
        Assert.Null(json["progress"]);
        Assert.Null(json["badge"]);
    }

    [Fact]
    public void A_glyph_badge_is_named()
    {
        JsonObject json = AgentData.Window(s_window, "Word", false, false, false, default, 0, false, null, new AppBadge(0, BadgeGlyph.NewMessage));

        Assert.Equal("newMessage", (string)json["badge"]!);
    }

    [Fact]
    public void Network_gives_signal_bars_only_for_WiFi()
    {
        Assert.Equal(3, (int)AgentData.Network(new NetworkState(NetworkKind.WiFi, "Home", true, 3))["signalBars"]!);
        JsonObject ethernet = AgentData.Network(new NetworkState(NetworkKind.Ethernet, "Network 2", false, 0));
        Assert.Equal("ethernet", (string)ethernet["kind"]!);
        Assert.False((bool)ethernet["internet"]!);
        Assert.Null(ethernet["signalBars"]);
    }

    [Fact]
    public void Battery_is_null_without_one()
    {
        Assert.Null(AgentData.Battery(null));
        Assert.True((bool)AgentData.Battery(new BatteryState(80, true))!["charging"]!);
    }

    [Fact]
    public void Notifications_come_newest_first_up_to_the_limit()
    {
        var day = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        ToastInfo[] toasts =
        [
            new(1, "mail", "Mail", day, "Old", "first"),
            new(2, "mail", "Mail", day.AddHours(2), "Newest", "third"),
            new(3, "chat", "Chat", day.AddHours(1), "Middle", "second"),
        ];

        JsonArray json = AgentData.Notifications(toasts, 2);

        Assert.Equal(["Newest", "Middle"], json.Select(toast => (string)toast!["title"]!));
        Assert.Equal("Chat", (string)json[1]!["app"]!);
        Assert.Equal("2026-10-16T13:00:00.0000000+00:00", (string)json[1]!["time"]!);
    }

    [Fact]
    public void Apps_are_ranked_as_Start_ranks_them()
    {
        AppCatalogEntry[] apps =
        [
            new("Visual Studio Code", "Microsoft.VisualStudioCode", null),
            new("Code Writer", "CodeWriter!App", null),
            new("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null),
        ];

        JsonArray json = AgentData.Apps(apps, "code", 5);

        Assert.Equal(["Code Writer", "Visual Studio Code"], json.Select(app => (string)app!["name"]!));
        Assert.Equal("CodeWriter!App", (string)json[0]!["id"]!);
        Assert.Single(AgentData.Apps(apps, "code", 1));
    }

    [Fact]
    public void Files_get_Starts_groups()
    {
        JsonArray json = AgentData.Files(
        [
            new IndexResult("Report.docx", @"C:\Users\me\Documents\Report.docx", ["document"]),
            new IndexResult("Documents", @"C:\Users\me\Documents", ["folder"]),
            new IndexResult("setup.exe", @"C:\Users\me\Downloads\setup.exe", ["program"]),
        ]);

        Assert.Equal(["documents", "folders", "other"], json.Select(file => (string)file!["kind"]!));
        Assert.Equal(@"C:\Users\me\Documents\Report.docx", (string)json[0]!["path"]!);
    }
}

public sealed class ToolArgumentsTests
{
    private static JsonObject Arguments(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Strings_are_read_and_a_missing_required_one_is_named()
    {
        Assert.Equal("calc", ToolArguments.RequiredString(Arguments("""{"query":"calc"}"""), "query"));
        Assert.Null(ToolArguments.OptionalString(Arguments("{}"), "query"));
        Assert.Equal("query is required", Assert.Throws<ToolArgumentException>(() => ToolArguments.RequiredString(Arguments("""{"query":""}"""), "query")).Message);
        Assert.Equal("query must be a string", Assert.Throws<ToolArgumentException>(() => ToolArguments.OptionalString(Arguments("""{"query":5}"""), "query")).Message);
    }

    [Theory]
    [InlineData("{}", 20)]
    [InlineData("""{"limit":5}""", 5)]
    [InlineData("""{"limit":5.0}""", 5)]
    [InlineData("""{"limit":100}""", 100)]
    public void Integers_take_the_default_when_missing(string json, int expected) =>
        Assert.Equal(expected, ToolArguments.Integer(Arguments(json), "limit", 20, 1, 100));

    [Theory]
    [InlineData("""{"limit":0}""")]
    [InlineData("""{"limit":101}""")]
    [InlineData("""{"limit":2.5}""")]
    [InlineData("""{"limit":"5"}""")]
    public void Integers_out_of_range_or_not_whole_are_refused(string json)
    {
        var error = Assert.Throws<ToolArgumentException>(() => ToolArguments.Integer(Arguments(json), "limit", 20, 1, 100));
        Assert.Equal("limit must be a whole number from 1 to 100", error.Message);
    }
}
