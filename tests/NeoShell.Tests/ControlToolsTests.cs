using System.Text.Json.Nodes;
using NeoShell.Agents;
using NeoShell.Interop.Shell;
using NeoShell.Snap;

namespace NeoShell.Tests;

public sealed class ControlToolsTests
{
    private static JsonObject Arguments(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static readonly AppCatalogEntry[] s_apps =
    [
        new("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null),
        new("Terminal", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", null),
        new("Terminal", "{6D809377-6AF0-444B-8957-A3773F02200E}\\Tools\\terminal.exe", null),
    ];

    [Fact]
    public void An_app_is_found_by_id_or_its_only_name()
    {
        Assert.Equal("Notepad", ControlTools.FindApp(s_apps, "microsoft.windowsnotepad_8wekyb3d8bbwe!app").Name);
        Assert.Equal("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", ControlTools.FindApp(s_apps, "notepad").Id);
        Assert.Equal("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", ControlTools.FindApp(s_apps, "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App").Id);
    }

    [Fact]
    public void A_name_two_apps_share_or_none_has_is_refused()
    {
        var several = Assert.Throws<ToolArgumentException>(() => ControlTools.FindApp(s_apps, "Terminal"));
        Assert.StartsWith("Several apps are called Terminal; give one's id: Microsoft.WindowsTerminal_8wekyb3d8bbwe!App, ", several.Message);
        var none = Assert.Throws<ToolArgumentException>(() => ControlTools.FindApp(s_apps, "Paint"));
        Assert.Contains("search gives apps and their ids", none.Message);
    }

    [Fact]
    public void Positions_are_Snaps_and_restore_unsnaps()
    {
        Assert.Equal(SnapPosition.None, ControlTools.Arrangements["restore"]);
        Assert.Equal(SnapPosition.BottomRight, ControlTools.Arrangements["bottomRight"]);
        Assert.All(ControlTools.Arrangements.Values.Where(p => p is not (SnapPosition.None or SnapPosition.Minimized or SnapPosition.Maximized)),
            position => Assert.NotNull(WindowSnap.Zone(position)));
        Assert.Equal(["start", "quickSettings", "notificationCenter", "settings"], ControlTools.Surfaces.Keys);
    }

    [Fact]
    public void A_window_must_be_one_with_a_taskbar_button()
    {
        nint[] windows = [0x10, 0x20];

        Assert.Equal(0x20, ToolArguments.Window(Arguments("""{"handle":32}"""), windows, hwnd => hwnd));
        Assert.Contains("list_windows", Assert.Throws<ToolArgumentException>(() => ToolArguments.Window(Arguments("""{"handle":48}"""), windows, hwnd => hwnd)).Message);
        Assert.StartsWith("handle is required", Assert.Throws<ToolArgumentException>(() => ToolArguments.Window(Arguments("""{"handle":"32"}"""), windows, hwnd => hwnd)).Message);
    }

    [Fact]
    public void Choices_and_booleans_are_checked()
    {
        Assert.Equal(SnapPosition.Left, ToolArguments.Choice(Arguments("""{"position":"left"}"""), "position", ControlTools.Arrangements));
        var error = Assert.Throws<ToolArgumentException>(() => ToolArguments.Choice(Arguments("""{"position":"middle"}"""), "position", ControlTools.Arrangements));
        Assert.StartsWith("position must be one of minimize, maximize, restore, left, ", error.Message);
        Assert.True(ToolArguments.RequiredBoolean(Arguments("""{"on":true}"""), "on"));
        Assert.Null(ToolArguments.OptionalBoolean(Arguments("{}"), "on"));
        Assert.Equal("on must be true or false", Assert.Throws<ToolArgumentException>(() => ToolArguments.OptionalBoolean(Arguments("""{"on":"yes"}"""), "on")).Message);
    }

    [Fact]
    public void A_schema_lists_its_required_arguments_and_choices()
    {
        JsonObject schema = ToolArguments.Schema(new JsonObject
        {
            ["position"] = ToolArguments.ChoiceSchema("Where", ["left", "right"]),
        }, "position");

        Assert.Equal("object", (string)schema["type"]!);
        Assert.Equal(["position"], ((JsonArray)schema["required"]!).Select(name => (string)name!));
        Assert.Equal(["left", "right"], ((JsonArray)schema["properties"]!["position"]!["enum"]!).Select(choice => (string)choice!));
        Assert.Null(ToolArguments.Schema(new JsonObject())["required"]);
    }
}
