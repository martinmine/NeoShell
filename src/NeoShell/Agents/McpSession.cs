using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using NeoShell.Logging;

namespace NeoShell.Agents;

/// <summary>
/// One agent's MCP session over newline-delimited JSON-RPC: initialize, ping, tools/list and tools/call. Requests
/// are answered as they finish, not in order, so a tool that waits (a notification's buttons) holds up no other.
/// </summary>
/// <param name="isAllowed">Whether the user lets agents use a group's tools now; the others aren't listed.</param>
/// <param name="runOnUiThread">Starts a tool's handler where shell state may be touched (the UI thread).</param>
/// <param name="timeout">How long a call may take before the agent is told NeoShell is busy.</param>
public sealed class McpSession(
    IReadOnlyList<AgentTool> tools,
    Func<AgentToolGroup, bool> isAllowed,
    Func<Func<Task<ToolResult>>, Task<ToolResult>> runOnUiThread,
    TimeSpan timeout,
    string serverVersion)
{
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _writing = new(1);
    private StreamWriter? _writer;
    private volatile bool _revoked;

    /// <summary>The client's name from its initialize request (e.g. "claude-code"), for the log and its notifications.</summary>
    public string ClientName { get; private set; } = "agent";

    /// <summary>The client said who it is (<see cref="ClientName"/>).</summary>
    public event Action? Initialized;

    /// <summary>Disconnected by the user: the session stays open but offers no tools and refuses every call.</summary>
    public bool IsRevoked => _revoked;

    /// <summary>
    /// Takes the agent's tools away. The pipe stays open on purpose: closed, the agent's bridge would connect again
    /// two seconds later.
    /// </summary>
    public void Revoke()
    {
        _revoked = true;
        NotifyToolsChanged();
    }

    /// <summary>Tells the client to ask for the tools again: the groups the user allows have changed.</summary>
    public void NotifyToolsChanged() => _ = SendAsync(McpProtocol.Notification("notifications/tools/list_changed"));

    /// <summary>Reads requests from the stream and writes the answers to it until the other end closes it.</summary>
    public async Task RunAsync(Stream stream, CancellationToken cancellation)
    {
        using var reader = new StreamReader(stream, s_utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await using var writer = new StreamWriter(stream, s_utf8, leaveOpen: true) { NewLine = "\n" };
        _writer = writer;
        var answering = new List<Task>();
        try
        {
            while (await reader.ReadLineAsync(cancellation) is { } line)
            {
                if (line.Length > 0)
                    answering.Add(AnswerAsync(line));
                answering.RemoveAll(task => task.IsCompleted);
            }
            await Task.WhenAll(answering);
        }
        finally
        {
            await _writing.WaitAsync(CancellationToken.None);
            _writer = null;
            _writing.Release();
        }

        async Task AnswerAsync(string line)
        {
            if (await HandleAsync(line) is { } answer)
                await SendAsync(answer);
        }
    }

    private async Task SendAsync(string line)
    {
        await _writing.WaitAsync(CancellationToken.None);
        try
        {
            if (_writer is null)
                return;
            await _writer.WriteLineAsync(line);
            await _writer.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The agent went away; nothing to tell.
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>Answers one message: the response line, or null for a notification (which gets none).</summary>
    public async Task<string?> HandleAsync(string line)
    {
        McpProtocol.Message message = McpProtocol.Parse(line);
        if (message.Json is null)
            return McpProtocol.Error(null, McpProtocol.ParseError, "Not a JSON-RPC message");
        if (!message.IsRequest)
            return null; // notifications (initialized, cancelled) and responses need nothing from NeoShell

        JsonObject? parameters = message.Json["params"] as JsonObject;
        switch (message.Method)
        {
            case "initialize":
                if (parameters?["clientInfo"]?["name"] is JsonValue name && name.TryGetValue(out string? clientName))
                    ClientName = clientName;
                Initialized?.Invoke();
                string? version = parameters?["protocolVersion"] is JsonValue value && value.TryGetValue(out string? v) ? v : null;
                return McpProtocol.Result(message.Id, McpProtocol.InitializeResult(version, serverVersion));
            case "ping":
                return McpProtocol.Result(message.Id, new JsonObject());
            case "tools/list":
                return McpProtocol.Result(message.Id, new JsonObject { ["tools"] = ListTools() });
            case "tools/call":
                return await CallToolAsync(message.Id, parameters);
            default:
                return McpProtocol.Error(message.Id, McpProtocol.MethodNotFound, $"NeoShell has no method {message.Method}");
        }
    }

    private JsonArray ListTools() => new(tools.Where(tool => !_revoked && isAllowed(tool.Group)).Select(tool => (JsonNode)new JsonObject
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["inputSchema"] = tool.InputSchema.DeepClone(),
    }).ToArray());

    private async Task<string> CallToolAsync(JsonNode? id, JsonObject? parameters)
    {
        string? name = parameters?["name"] is JsonValue value && value.TryGetValue(out string? n) ? n : null;
        AgentTool? tool = tools.FirstOrDefault(tool => tool.Name == name);
        if (tool is null)
            return McpProtocol.Error(id, McpProtocol.InvalidParams, $"NeoShell has no tool {name}");
        if (_revoked)
            return McpProtocol.Result(id, ToJson(ToolResult.Error("The user disconnected this agent in NeoShell's settings")));
        if (!isAllowed(tool.Group))
        {
            return McpProtocol.Result(id, ToJson(ToolResult.Error(
                $"NeoShell's settings don't let agents use {AgentData.Name(tool.Group)} tools (Settings > Agents)")));
        }

        JsonNode? argumentsNode = parameters!["arguments"];
        ToolResult result;
        if (argumentsNode is not (null or JsonObject))
        {
            result = ToolResult.Error("The arguments must be an object");
        }
        else
        {
            var arguments = (JsonObject?)argumentsNode?.DeepClone() ?? [];
            var stopwatch = Stopwatch.StartNew();
            result = await RunAsync(tool, arguments);
            Log.Info($"Agent {ClientName} called {tool.Name}: {(result.IsError ? "error, " + result.Text : "done")} in {stopwatch.ElapsedMilliseconds} ms");
        }
        return McpProtocol.Result(id, ToJson(result));
    }

    private async Task<ToolResult> RunAsync(AgentTool tool, JsonObject arguments)
    {
        try
        {
            return await runOnUiThread(() => Invoke(tool, arguments)).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            return ToolResult.Error("NeoShell is busy; try again in a moment");
        }
        catch (Exception ex)
        {
            Log.Error($"Agent tool {tool.Name} could not run", ex);
            return ToolResult.Error("NeoShell could not run the tool");
        }
    }

    private static async Task<ToolResult> Invoke(AgentTool tool, JsonObject arguments)
    {
        try
        {
            return await tool.Handler(arguments);
        }
        catch (ToolArgumentException ex)
        {
            return ToolResult.Error(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error($"Agent tool {tool.Name} failed", ex);
            return ToolResult.Error($"NeoShell failed: {ex.Message}");
        }
    }

    private static JsonObject ToJson(ToolResult result)
    {
        var content = new JsonArray();
        if (result.Png is { } png)
            content.Add((JsonNode)new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(png), ["mimeType"] = "image/png" });
        content.Add((JsonNode)new JsonObject { ["type"] = "text", ["text"] = result.Text });
        var json = new JsonObject
        {
            ["content"] = content,
            ["isError"] = result.IsError,
        };
        if (result.Data is JsonObject data)
            json["structuredContent"] = data.DeepClone();
        return json;
    }
}
