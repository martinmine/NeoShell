using System.Text.Json.Nodes;
using NeoShell.Agents;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using NeoShell.Taskbar;

namespace NeoShell.Tests;

public sealed class PresenceToolsTests
{
    private static WindowInfo Window(nint handle, int processId) =>
        new(handle, "title", "class", true, false, true, false, false, 0, processId, null, null);

    private static readonly ProcessEntry[] s_chain =
    [
        new(10, 9, "NeoShell.exe"),
        new(9, 8, "claude.exe"),
        new(8, 7, "pwsh.exe"),
        new(7, 1, "WindowsTerminal.exe"),
    ];

    [Fact]
    public void The_agents_window_is_the_nearest_of_its_processes_with_one()
    {
        WindowInfo[] windows = [Window(0x100, 50), Window(0x200, 7), Window(0x300, 7)];

        Assert.Equal(0x200, PresenceTools.AgentWindow(s_chain, windows)!.Handle);
        Assert.Equal(0x400, PresenceTools.AgentWindow(s_chain, [.. windows, Window(0x400, 9)])!.Handle);
        Assert.Null(PresenceTools.AgentWindow(s_chain, [Window(0x100, 50)]));
        Assert.Null(PresenceTools.AgentWindow([], windows));
    }

    [Fact]
    public void Statuses_show_as_taskbar_progress()
    {
        Assert.Equal(new TaskProgress(TaskbarProgressState.Indeterminate, 0), PresenceTools.ProgressFor(AgentStatus.Working, null));
        Assert.Equal(new TaskProgress(TaskbarProgressState.Normal, 0.42), PresenceTools.ProgressFor(AgentStatus.Progress, 42));
        Assert.Equal(new TaskProgress(TaskbarProgressState.Error, 1), PresenceTools.ProgressFor(AgentStatus.Error, null));
        Assert.Equal(new TaskProgress(TaskbarProgressState.Paused, 0.5), PresenceTools.ProgressFor(AgentStatus.Paused, 50));
        Assert.Null(PresenceTools.ProgressFor(AgentStatus.Clear, null));
        Assert.Null(PresenceTools.ProgressFor(AgentStatus.Attention, 10));
        Assert.Equal(["working", "progress", "paused", "error", "attention", "clear"], PresenceTools.Statuses.Keys);
    }

    [Fact]
    public void Buttons_are_a_short_list_of_short_texts()
    {
        static JsonObject Arguments(string json) => (JsonObject)JsonNode.Parse(json)!;

        Assert.Equal(["Yes", "No"], ToolArguments.Strings(Arguments("""{"buttons":["Yes","No"]}"""), "buttons", 3, 40));
        Assert.Empty(ToolArguments.Strings(Arguments("{}"), "buttons", 3, 40));
        Assert.Throws<ToolArgumentException>(() => ToolArguments.Strings(Arguments("""{"buttons":["a","b","c","d"]}"""), "buttons", 3, 40));
        Assert.Throws<ToolArgumentException>(() => ToolArguments.Strings(Arguments("""{"buttons":[""]}"""), "buttons", 3, 40));
        Assert.Throws<ToolArgumentException>(() => ToolArguments.Strings(Arguments("""{"buttons":"Yes"}"""), "buttons", 3, 40));
        Assert.Throws<ToolArgumentException>(() => ToolArguments.Strings(Arguments($$$"""{"buttons":["{{{new string('x', 41)}}}"]}"""), "buttons", 3, 40));
    }

    [Fact]
    public async Task A_tool_knows_which_session_calls_it()
    {
        McpSession? caller = null;
        var tool = new AgentTool("who", AgentToolGroup.Presence, "Who.", AgentTool.NoArguments(), (_, session) =>
        {
            caller = session;
            return Task.FromResult(new ToolResult(session.Processes[0].Name));
        });
        var session = new McpSession([tool], _ => true, handler => handler(), TimeSpan.FromSeconds(5), "1") { Processes = s_chain };

        string answer = (await session.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"who"}}"""))!;

        Assert.Same(session, caller);
        Assert.Contains("NeoShell.exe", answer);
    }

    [Fact]
    public async Task A_tool_that_waits_for_the_user_has_its_own_timeout()
    {
        var waiting = new AgentTool("wait", AgentToolGroup.Presence, "Waits.", AgentTool.NoArguments(), async _ =>
        {
            await Task.Delay(200);
            return new ToolResult("answered");
        })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        var session = new McpSession([waiting], _ => true, handler => handler(), TimeSpan.FromMilliseconds(50), "1");

        string answer = (await session.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"wait"}}"""))!;

        Assert.Contains("answered", answer);
    }

    [Fact]
    public async Task A_session_ends_when_the_agent_leaves_with_a_call_still_waiting()
    {
        var never = new TaskCompletionSource<ToolResult>();
        var waiting = new AgentTool("wait", AgentToolGroup.Presence, "Waits.", AgentTool.NoArguments(), _ => never.Task) { Timeout = TimeSpan.FromMinutes(5) };
        var session = new McpSession([waiting], _ => true, handler => handler(), TimeSpan.FromSeconds(5), "1");
        var ended = new TaskCompletionSource();
        session.Ended += () => ended.TrySetResult();
        var (server, client) = Duplex.Create();
        Task running = session.RunAsync(server, CancellationToken.None);
        await new LineEnd(client).Send("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"wait"}}""");
        await Task.Delay(50);

        client.Dispose();

        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(running.IsCompleted);
        never.SetResult(new ToolResult("late"));
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_session_ends_once_whether_revoked_or_closed()
    {
        var session = new McpSession([], _ => true, handler => handler(), TimeSpan.FromSeconds(5), "1");
        int ended = 0;
        session.Ended += () => ended++;
        var (server, client) = Duplex.Create();
        Task running = session.RunAsync(server, CancellationToken.None);

        session.Revoke();
        client.Dispose();
        await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, ended);
    }
}
