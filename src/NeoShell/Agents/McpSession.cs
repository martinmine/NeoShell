using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using NeoShell.Logging;

namespace NeoShell.Agents;

/// <summary>
/// One agent's MCP session over newline-delimited JSON-RPC: initialize, ping, tools/list and tools/call. Requests
/// are answered as they finish, not in order, so a tool that waits (a notification's buttons) holds up no other.
/// </summary>
/// <param name="runOnUiThread">Starts a tool's handler where shell state may be touched (the UI thread).</param>
/// <param name="timeout">How long a call may take before the agent is told NeoShell is busy.</param>
public sealed class McpSession(
    IReadOnlyList<AgentTool> tools,
    Func<Func<Task<ToolResult>>, Task<ToolResult>> runOnUiThread,
    TimeSpan timeout,
    string serverVersion)
{
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The client's name from its initialize request (e.g. "claude-code"), for the log and its notifications.</summary>
    public string ClientName { get; private set; } = "agent";

    /// <summary>Reads requests from the stream and writes the answers to it until the other end closes it.</summary>
    public async Task RunAsync(Stream stream, CancellationToken cancellation)
    {
        using var reader = new StreamReader(stream, s_utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(stream, s_utf8, leaveOpen: true) { NewLine = "\n" };
        using var writing = new SemaphoreSlim(1);
        var answering = new List<Task>();

        while (await reader.ReadLineAsync(cancellation) is { } line)
        {
            if (line.Length > 0)
                answering.Add(AnswerAsync(line));
            answering.RemoveAll(task => task.IsCompleted);
        }
        await Task.WhenAll(answering);

        async Task AnswerAsync(string line)
        {
            string? answer = await HandleAsync(line);
            if (answer is null)
                return;
            await writing.WaitAsync(CancellationToken.None);
            try
            {
                await writer.WriteLineAsync(answer);
                await writer.FlushAsync(CancellationToken.None);
            }
            catch (IOException)
            {
                // The agent went away before its answer; nothing to tell.
            }
            finally
            {
                writing.Release();
            }
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

    private JsonArray ListTools() => new(tools.Select(tool => (JsonNode)new JsonObject
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
        var json = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Text }),
            ["isError"] = result.IsError,
        };
        if (result.Data is JsonObject data)
            json["structuredContent"] = data.DeepClone();
        return json;
    }
}
