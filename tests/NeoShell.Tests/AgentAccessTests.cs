using System.Text.Json.Nodes;
using NeoShell.Agents;
using NeoShell.Interop.Shell;
using NeoShell.Settings;
using NeoShell.Tray;

namespace NeoShell.Tests;

public sealed class AgentAccessTests
{
    private static readonly AgentTool s_look = new("look", AgentToolGroup.Read, "Looks.", AgentTool.NoArguments(), _ => new ToolResult("seen"));
    private static readonly AgentTool s_act = new("act", AgentToolGroup.Control, "Acts.", AgentTool.NoArguments(), _ => new ToolResult("done"));

    private static McpSession Session(params AgentToolGroup[] allowed) =>
        new([s_look, s_act], allowed.Contains, handler => handler(), TimeSpan.FromSeconds(5), "1");

    private static async Task<JsonObject> Answer(McpSession session, string request) =>
        (JsonObject)JsonNode.Parse((await session.HandleAsync(request))!)!;

    private static async Task<IEnumerable<string>> ToolNames(McpSession session) =>
        ((JsonArray)(await Answer(session, """{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""))["result"]!["tools"]!)
            .Select(tool => (string)tool!["name"]!);

    private static async Task<JsonNode> Call(McpSession session, string tool) =>
        (await Answer(session, $$$"""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"{{{tool}}}"}}"""))["result"]!;

    [Fact]
    public async Task Only_allowed_groups_are_listed()
    {
        Assert.Equal(["look"], await ToolNames(Session(AgentToolGroup.Read)));
        Assert.Equal(["look", "act"], await ToolNames(Session(AgentToolGroup.Read, AgentToolGroup.Control)));
        Assert.Empty(await ToolNames(Session()));
    }

    [Fact]
    public async Task A_tool_of_a_group_turned_off_says_so()
    {
        JsonNode result = await Call(Session(AgentToolGroup.Read), "act");

        Assert.True((bool)result["isError"]!);
        Assert.Equal("NeoShell's settings don't let agents use control tools (Settings > Agents)", (string)result["content"]![0]!["text"]!);
    }

    [Fact]
    public async Task A_revoked_session_has_no_tools_and_refuses_calls()
    {
        McpSession session = Session(AgentToolGroup.Read);
        session.Revoke();

        Assert.True(session.IsRevoked);
        Assert.Empty(await ToolNames(session));
        JsonNode result = await Call(session, "look");
        Assert.True((bool)result["isError"]!);
        Assert.Contains("disconnected", (string)result["content"]![0]!["text"]!);
    }

    [Fact]
    public async Task Revoking_tells_the_client_the_tools_changed()
    {
        McpSession session = Session(AgentToolGroup.Read);
        var (server, client) = Duplex.Create();
        Task running = session.RunAsync(server, CancellationToken.None);
        var lines = new LineEnd(client);
        await lines.Send("""{"jsonrpc":"2.0","id":1,"method":"ping"}""");
        await lines.Receive();

        session.Revoke();

        Assert.Equal("notifications/tools/list_changed", (string)(await lines.Receive())["method"]!);
        client.Dispose();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Initialize_raises_Initialized_with_the_name()
    {
        McpSession session = Session(AgentToolGroup.Read);
        string? named = null;
        session.Initialized += () => named = session.ClientName;

        await session.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"claude-code"}}}""");

        Assert.Equal("claude-code", named);
    }

    [Fact]
    public void The_agent_is_the_first_process_that_isnt_the_bridge_or_a_launcher()
    {
        ProcessEntry[] chain =
        [
            new(10, 9, "NeoShell.exe"),
            new(9, 8, "cmd.exe"),
            new(8, 7, "claude.exe"),
            new(7, 1, "WindowsTerminal.exe"),
        ];

        Assert.Equal("claude.exe", AgentConnection.AgentProcess(chain)!.Name);
        Assert.Null(AgentConnection.AgentProcess([new(10, 9, "NeoShell.exe")]));
    }

    [Fact]
    public void A_connection_is_described_by_its_process_and_time()
    {
        var since = new DateTime(2026, 10, 16, 14, 5, 0);

        Assert.StartsWith("claude.exe (process 8), connected since ", AgentConnection.Describe(new(8, 7, "claude.exe"), since, revoked: false));
        Assert.Equal("Unknown process, disconnected", AgentConnection.Describe(null, since, revoked: true));
    }

    [Fact]
    public void This_process_heads_its_own_ancestry()
    {
        IReadOnlyList<ProcessEntry> chain = Processes.Ancestry(Environment.ProcessId);

        Assert.Equal(Environment.ProcessId, chain[0].Id);
        Assert.Equal(chain[0].ParentId, chain.Count > 1 ? chain[1].Id : chain[0].ParentId);
    }

    [Fact]
    public void Agents_may_read_and_show_their_state_unless_told_otherwise()
    {
        Assert.Equal([AgentToolGroup.Read, AgentToolGroup.Presence], new ShellSettings().AllowedAgentTools.Order());
        var settings = new ShellSettings { AgentReadAllowed = false, AgentControlAllowed = true, AgentScreenAllowed = true, AgentPresenceAllowed = false };
        Assert.Equal([AgentToolGroup.Control, AgentToolGroup.Screen], settings.AllowedAgentTools.Order());
        Assert.False(new ShellSettings().AgentAccessEnabled);
    }

    [Fact]
    public void The_Claude_Code_command_quotes_the_path()
    {
        Assert.Equal(@"claude mcp add --scope user neoshell -- ""C:\Program Files\NeoShell\NeoShell.exe"" /mcp",
            SettingsPages.ClaudeCodeCommand(@"C:\Program Files\NeoShell\NeoShell.exe"));
        Assert.Equal("Agents", SettingsPages.Title(SettingsPage.Agents));
    }

    [Fact]
    public void The_agent_indicator_names_connected_agents_only_while_they_can_act_or_see()
    {
        (string, bool)[] connections = [("claude-code", false), ("old", true)];

        Assert.Equal(["claude-code"], IndicatorDisplay.AgentsToShow(connections, canActOrSee: true));
        Assert.Empty(IndicatorDisplay.AgentsToShow(connections, canActOrSee: false));
        Assert.Equal("Connected agents that can act in the shell:\nclaude-code", IndicatorDisplay.AgentToolTip(["claude-code"]));
    }
}
