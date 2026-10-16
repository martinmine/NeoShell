using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using NeoShell.Agents;

namespace NeoShell.Tests;

public sealed class McpSessionTests
{
    private static readonly AgentTool s_echo = new("echo", "Says the text back.",
        new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } } },
        arguments => arguments["text"] is JsonValue text && text.TryGetValue(out string? value)
            ? ToolResult.FromData(new JsonObject { ["text"] = value })
            : throw new ToolArgumentException("text is missing"));

    private static readonly AgentTool s_failing = new("fail", "Fails.", AgentTool.NoArguments(),
        ToolResult (_) => throw new InvalidOperationException("broken"));

    private static McpSession Session() =>
        new([s_echo, s_failing], handler => handler(), TimeSpan.FromSeconds(5), "1.2.3");

    private static async Task<JsonObject> Answer(McpSession session, string request) =>
        (JsonObject)JsonNode.Parse((await session.HandleAsync(request))!)!;

    private static string Call(int id, string tool, string arguments) =>
        $$$"""{"jsonrpc":"2.0","id":{{{id}}},"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{arguments}}}}}""";

    [Fact]
    public async Task Initialize_takes_a_version_NeoShell_speaks_and_names_the_client()
    {
        McpSession session = Session();
        JsonObject answer = await Answer(session, """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"claude-code","version":"2"}}}
            """);

        Assert.Equal(1, (int)answer["id"]!);
        Assert.Equal("2025-06-18", (string)answer["result"]!["protocolVersion"]!);
        Assert.Equal("NeoShell", (string)answer["result"]!["serverInfo"]!["name"]!);
        Assert.Equal("1.2.3", (string)answer["result"]!["serverInfo"]!["version"]!);
        Assert.True((bool)answer["result"]!["capabilities"]!["tools"]!["listChanged"]!);
        Assert.Equal("claude-code", session.ClientName);
    }

    [Fact]
    public async Task Initialize_with_an_unknown_version_offers_the_newest()
    {
        JsonObject answer = await Answer(Session(), """{"jsonrpc":"2.0","id":"a","method":"initialize","params":{"protocolVersion":"1999-01-01"}}""");

        Assert.Equal("a", (string)answer["id"]!);
        Assert.Equal(McpProtocol.Versions[0], (string)answer["result"]!["protocolVersion"]!);
    }

    [Fact]
    public async Task Ping_is_answered_and_notifications_are_not()
    {
        McpSession session = Session();

        JsonObject ping = await Answer(session, """{"jsonrpc":"2.0","id":7,"method":"ping"}""");
        Assert.Empty((JsonObject)ping["result"]!);
        Assert.Null(await session.HandleAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public async Task What_isnt_a_message_is_a_parse_error(string line)
    {
        JsonObject answer = await Answer(Session(), line);

        Assert.Equal(McpProtocol.ParseError, (int)answer["error"]!["code"]!);
        Assert.Null(answer["id"]);
    }

    [Fact]
    public async Task Unknown_methods_are_errors()
    {
        JsonObject answer = await Answer(Session(), """{"jsonrpc":"2.0","id":2,"method":"resources/list"}""");

        Assert.Equal(McpProtocol.MethodNotFound, (int)answer["error"]!["code"]!);
    }

    [Fact]
    public async Task Tools_are_listed_with_their_schemas()
    {
        JsonObject answer = await Answer(Session(), """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""");

        JsonArray tools = (JsonArray)answer["result"]!["tools"]!;
        Assert.Equal(["echo", "fail"], tools.Select(tool => (string)tool!["name"]!));
        Assert.Equal("Says the text back.", (string)tools[0]!["description"]!);
        Assert.Equal("string", (string)tools[0]!["inputSchema"]!["properties"]!["text"]!["type"]!);
    }

    [Fact]
    public async Task A_call_returns_text_and_data()
    {
        JsonObject answer = await Answer(Session(), Call(4, "echo", """{"text":"hi"}"""));

        JsonNode result = answer["result"]!;
        Assert.False((bool)result["isError"]!);
        Assert.Equal("text", (string)result["content"]![0]!["type"]!);
        Assert.Equal("""{"text":"hi"}""", (string)result["content"]![0]!["text"]!);
        Assert.Equal("hi", (string)result["structuredContent"]!["text"]!);
    }

    [Theory]
    [InlineData("echo", "{}", "text is missing")]
    [InlineData("echo", "[]", "The arguments must be an object")]
    [InlineData("fail", "{}", "NeoShell failed: broken")]
    public async Task A_call_that_fails_is_a_tool_error(string tool, string arguments, string message)
    {
        JsonObject answer = await Answer(Session(), Call(5, tool, arguments));

        Assert.True((bool)answer["result"]!["isError"]!);
        Assert.Equal(message, (string)answer["result"]!["content"]![0]!["text"]!);
    }

    [Fact]
    public async Task A_call_without_arguments_gets_an_empty_object()
    {
        JsonObject answer = await Answer(Session(), """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"echo"}}""");

        Assert.Equal("text is missing", (string)answer["result"]!["content"]![0]!["text"]!);
    }

    [Fact]
    public async Task An_unknown_tool_is_a_protocol_error()
    {
        JsonObject answer = await Answer(Session(), Call(8, "nope", "{}"));

        Assert.Equal(McpProtocol.InvalidParams, (int)answer["error"]!["code"]!);
    }

    [Fact]
    public async Task An_async_tool_answers_when_it_finishes()
    {
        var later = new AgentTool("later", "Answers after a while.", AgentTool.NoArguments(), async _ =>
        {
            await Task.Delay(20);
            return new ToolResult("later");
        });
        var session = new McpSession([later], handler => handler(), TimeSpan.FromSeconds(5), "1");

        JsonObject answer = await Answer(session, Call(10, "later", "{}"));

        Assert.Equal("later", (string)answer["result"]!["content"]![0]!["text"]!);
    }

    [Fact]
    public async Task A_busy_UI_thread_makes_the_call_an_error_after_the_timeout()
    {
        var session = new McpSession([s_echo], _ => new TaskCompletionSource<ToolResult>().Task, TimeSpan.FromMilliseconds(50), "1");

        JsonObject answer = await Answer(session, Call(9, "echo", """{"text":"hi"}"""));

        Assert.True((bool)answer["result"]!["isError"]!);
        Assert.Contains("busy", (string)answer["result"]!["content"]![0]!["text"]!);
    }

    [Fact]
    public async Task Calls_over_a_stream_are_answered_as_they_finish()
    {
        var release = new TaskCompletionSource();
        var slow = new AgentTool("slow", "Waits.", AgentTool.NoArguments(), _ =>
        {
            release.Task.Wait();
            return new ToolResult("slow");
        });
        var session = new McpSession([slow, s_echo], handler => Task.Run(handler), TimeSpan.FromSeconds(5), "1");
        var (server, client) = Duplex.Create();
        Task running = session.RunAsync(server, CancellationToken.None);
        var lines = new LineEnd(client);

        await lines.Send(Call(1, "slow", "{}"));
        await lines.Send(Call(2, "echo", """{"text":"quick"}"""));

        Assert.Equal(2, (int)(await lines.Receive())["id"]!);
        release.SetResult();
        Assert.Equal(1, (int)(await lines.Receive())["id"]!);

        client.Dispose();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

public sealed class McpBridgeTests
{
    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","clientInfo":{"name":"test"}}}""";

    private static readonly AgentTool s_echo = new("echo", "Says hi.", AgentTool.NoArguments(), _ => new ToolResult("hi"));

    /// <summary>A stand-in for NeoShell: an MCP session behind the bridge's connect, which can be stopped and started.</summary>
    private sealed class FakeNeoShell
    {
        public bool Running { get; set; }
        public Duplex? Connection { get; private set; }
        public TaskCompletionSource Release { get; } = new();
        public int Connections { get; private set; }

        public Task<Stream?> Connect(CancellationToken cancellation)
        {
            if (!Running)
                return Task.FromResult<Stream?>(null);
            Connections++;
            var (server, bridge) = Duplex.Create();
            Connection = server;
            var blocking = new AgentTool("wait", "Waits.", AgentTool.NoArguments(), _ =>
            {
                Release.Task.Wait();
                return new ToolResult("done");
            });
            _ = new McpSession([s_echo, blocking], handler => Task.Run(handler), TimeSpan.FromSeconds(5), "1")
                .RunAsync(server, CancellationToken.None);
            return Task.FromResult<Stream?>(bridge);
        }

        /// <summary>NeoShell exits: its end of the pipe closes.</summary>
        public void Stop()
        {
            Running = false;
            Connection?.Dispose();
        }
    }

    private static (LineEnd Client, Task Bridge, Duplex ClientEnd) Start(FakeNeoShell neoShell)
    {
        var (bridgeEnd, clientEnd) = Duplex.Create();
        var bridge = new McpBridge(bridgeEnd, bridgeEnd, neoShell.Connect, TimeSpan.FromMilliseconds(30));
        return (new LineEnd(clientEnd), bridge.RunAsync(), clientEnd);
    }

    [Fact]
    public async Task Without_NeoShell_the_bridge_answers_for_it()
    {
        var (client, bridge, clientEnd) = Start(new FakeNeoShell());

        await client.Send(Initialize);
        JsonObject initialized = await client.Receive();
        Assert.Equal("2025-06-18", (string)initialized["result"]!["protocolVersion"]!);
        Assert.Equal("NeoShell", (string)initialized["result"]!["serverInfo"]!["name"]!);

        await client.Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        await client.Send("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        Assert.Empty((JsonArray)(await client.Receive())["result"]!["tools"]!);

        await client.Send("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo"}}""");
        JsonObject call = await client.Receive();
        Assert.Equal(3, (int)call["id"]!);
        Assert.Equal(McpProtocol.ServerUnavailable, (int)call["error"]!["code"]!);

        clientEnd.Dispose();
        await bridge.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task With_NeoShell_messages_pass_through()
    {
        var neoShell = new FakeNeoShell { Running = true };
        var (client, bridge, clientEnd) = Start(neoShell);

        await client.Send(Initialize);
        Assert.Equal(1, (int)(await client.Receive())["id"]!);
        await client.Send("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"echo"}}""");
        JsonObject call = await client.Receive();

        Assert.Equal("hi", (string)call["result"]!["content"]![0]!["text"]!);
        Assert.Equal(1, neoShell.Connections);
        clientEnd.Dispose();
        await bridge.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task NeoShell_closing_fails_its_unanswered_requests()
    {
        var neoShell = new FakeNeoShell { Running = true };
        var (client, bridge, clientEnd) = Start(neoShell);
        await client.Send(Initialize);
        await client.Receive();

        await client.Send("""{"jsonrpc":"2.0","id":"w","method":"tools/call","params":{"name":"wait"}}""");
        await Task.Delay(100);
        neoShell.Stop();
        JsonObject failed = await client.Receive();

        Assert.Equal("w", (string)failed["id"]!);
        Assert.Equal(McpProtocol.ServerUnavailable, (int)failed["error"]!["code"]!);
        neoShell.Release.SetResult();
        clientEnd.Dispose();
        await bridge.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task NeoShell_coming_back_gets_the_clients_initialize_and_the_client_hears_the_tools_changed()
    {
        var neoShell = new FakeNeoShell();
        var (client, bridge, clientEnd) = Start(neoShell);
        await client.Send(Initialize);
        await client.Receive(); // answered by the bridge

        neoShell.Running = true;
        JsonObject changed = await client.Receive();
        Assert.Equal("notifications/tools/list_changed", (string)changed["method"]!);

        // The replayed initialize's answer isn't passed on: the next line is the answer to the next request.
        await client.Send("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        JsonObject tools = await client.Receive();
        Assert.Equal(2, (int)tools["id"]!);
        Assert.Equal(2, ((JsonArray)tools["result"]!["tools"]!).Count);

        // And again after a restart.
        neoShell.Stop();
        await Task.Delay(100);
        neoShell.Running = true;
        Assert.Equal("notifications/tools/list_changed", (string)(await client.Receive())["method"]!);
        await client.Send("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo"}}""");
        Assert.Equal("hi", (string)(await client.Receive())["result"]!["content"]![0]!["text"]!);
        Assert.Equal(2, neoShell.Connections);

        clientEnd.Dispose();
        await bridge.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

/// <summary>One end of an in-memory two-way stream, as a pipe connection; disposing it closes the other end's input.</summary>
internal sealed class Duplex(Stream input, Stream output) : Stream
{
    public static (Duplex, Duplex) Create()
    {
        var one = new Pipe();
        var two = new Pipe();
        return (new Duplex(one.Reader.AsStream(), two.Writer.AsStream()), new Duplex(two.Reader.AsStream(), one.Writer.AsStream()));
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        input.ReadAsync(buffer, cancellationToken);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        input.ReadAsync(buffer, offset, count, cancellationToken);
    public override void Write(byte[] buffer, int offset, int count) => output.Write(buffer, offset, count);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        output.WriteAsync(buffer, cancellationToken);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        output.WriteAsync(buffer, offset, count, cancellationToken);
    public override void Flush() => output.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            output.Dispose();
            input.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Sends and receives JSON lines on a stream, as an MCP client does.</summary>
internal sealed class LineEnd(Stream stream)
{
    private readonly StreamReader _reader = new(stream, new UTF8Encoding(false), false, 1024, leaveOpen: true);

    public async Task Send(string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    public async Task<JsonObject> Receive()
    {
        string? line = await _reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(line);
        return (JsonObject)JsonNode.Parse(line)!;
    }
}
