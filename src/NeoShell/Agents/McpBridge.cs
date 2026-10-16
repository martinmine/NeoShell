using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace NeoShell.Agents;

/// <summary>
/// <c>NeoShell.exe /mcp</c>: the MCP server process an agent's client starts, passing messages between its stdio
/// and NeoShell's pipe (<see cref="AgentServer"/>). It outlives NeoShell: while NeoShell isn't there it answers for
/// it (initialize, ping, no tools, an error for anything else), and when NeoShell comes back it starts the session
/// there again with the client's own initialize and tells the client the tools changed. So the client can be started
/// before NeoShell and keeps working across NeoShell's restarts.
/// </summary>
/// <param name="connect">Opens a stream to NeoShell, or null if it isn't there.</param>
/// <param name="retryInterval">How often NeoShell is looked for while it isn't there.</param>
public sealed class McpBridge(Stream clientIn, Stream clientOut, Func<CancellationToken, Task<Stream?>> connect, TimeSpan retryInterval)
{
    private const string ReplayId = "neoshell-bridge-initialize";
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly StreamWriter _toClient = new(clientOut, s_utf8) { NewLine = "\n" };
    private readonly SemaphoreSlim _clientWriting = new(1);
    // Held while the connection to NeoShell changes or a message goes to it, so messages keep their order.
    private readonly SemaphoreSlim _gate = new(1);
    private readonly string _version = typeof(McpBridge).Assembly.GetName().Version?.ToString() ?? "0";
    // Requests NeoShell has and hasn't answered, by id: if it goes, the client gets an error for each.
    private readonly Dictionary<string, JsonNode?> _pending = [];
    private Stream? _neoShell;
    private StreamWriter? _toNeoShell;
    private string? _initialize;

    /// <summary>Runs the bridge on this process's standard input and output.</summary>
    public static int Run()
    {
        using Stream input = Console.OpenStandardInput();
        using Stream output = Console.OpenStandardOutput();
        new McpBridge(input, output, ConnectAsync, TimeSpan.FromSeconds(2)).RunAsync().GetAwaiter().GetResult();
        return 0;
    }

    private static async Task<Stream?> ConnectAsync(CancellationToken cancellation)
    {
        var pipe = new NamedPipeClientStream(".", AgentServer.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(500, cancellation);
            return pipe;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            await pipe.DisposeAsync();
            return null;
        }
    }

    /// <summary>Passes messages until the client closes its end.</summary>
    public async Task RunAsync()
    {
        using var stop = new CancellationTokenSource();
        await TryConnectAsync(stop.Token);
        Task reconnecting = ReconnectAsync(stop.Token);

        using var reader = new StreamReader(clientIn, s_utf8, detectEncodingFromByteOrderMarks: false);
        while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
        {
            if (line.Length > 0)
                await FromClientAsync(line);
        }

        stop.Cancel();
        await reconnecting;
        await _gate.WaitAsync(CancellationToken.None);
        _neoShell?.Dispose();
        _neoShell = null;
        _gate.Release();
    }

    private async Task ReconnectAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(retryInterval, cancellation);
                if (_neoShell is null)
                    await TryConnectAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task TryConnectAsync(CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            if (_neoShell is not null || await connect(cancellation) is not { } stream)
                return;

            var writer = new StreamWriter(stream, s_utf8, leaveOpen: true) { NewLine = "\n" };
            try
            {
                if (_initialize is not null)
                {
                    // The client initialized with a NeoShell that has gone, or with the bridge: start NeoShell's
                    // session with the same request; its answer is the bridge's to drop.
                    var replay = (JsonObject)JsonNode.Parse(_initialize)!;
                    replay["id"] = ReplayId;
                    await writer.WriteLineAsync(replay.ToJsonString());
                    await writer.WriteLineAsync(McpProtocol.Notification("notifications/initialized"));
                    await writer.FlushAsync(cancellation);
                }
            }
            catch (IOException)
            {
                await writer.DisposeAsync();
                await stream.DisposeAsync();
                return;
            }

            _neoShell = stream;
            _toNeoShell = writer;
            _ = FromNeoShellAsync(stream);
            if (_initialize is not null)
                await ToClientAsync(McpProtocol.Notification("notifications/tools/list_changed"));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task FromClientAsync(string line)
    {
        McpProtocol.Message message = McpProtocol.Parse(line);
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (message.IsRequest && message.Method == "initialize")
                _initialize = line;

            if (_toNeoShell is not null)
            {
                try
                {
                    if (message.IsRequest)
                    {
                        lock (_pending)
                            _pending[McpProtocol.IdKey(message.Id)] = message.Id;
                    }
                    await _toNeoShell.WriteLineAsync(line);
                    await _toNeoShell.FlushAsync(CancellationToken.None);
                    return;
                }
                catch (IOException)
                {
                    await DisconnectAsync();
                }
            }

            if (message.IsRequest)
                await ToClientAsync(AnswerForNeoShell(message));
            else if (message.Json is null)
                await ToClientAsync(McpProtocol.Error(null, McpProtocol.ParseError, "Not a JSON-RPC message"));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task FromNeoShellAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, s_utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        try
        {
            while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
            {
                McpProtocol.Message message = McpProtocol.Parse(line);
                if (message.IsResponse)
                {
                    string key = McpProtocol.IdKey(message.Id);
                    if (key == McpProtocol.IdKey(ReplayId))
                        continue;
                    lock (_pending)
                        _pending.Remove(key);
                }
                await ToClientAsync(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // NeoShell went away, or the bridge closed the pipe.
        }

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (_neoShell == stream)
                await DisconnectAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops the connection to NeoShell and fails what it hadn't answered. Called holding the gate.</summary>
    private async Task DisconnectAsync()
    {
        _neoShell?.Dispose();
        _neoShell = null;
        _toNeoShell = null;
        List<JsonNode?> unanswered;
        lock (_pending)
        {
            unanswered = [.. _pending.Values];
            _pending.Clear();
        }
        foreach (JsonNode? id in unanswered)
            await ToClientAsync(McpProtocol.Error(id, McpProtocol.ServerUnavailable, "NeoShell closed before answering"));
    }

    private string AnswerForNeoShell(McpProtocol.Message request)
    {
        switch (request.Method)
        {
            case "initialize":
                string? version = request.Json!["params"]?["protocolVersion"] is JsonValue value && value.TryGetValue(out string? v) ? v : null;
                return McpProtocol.Result(request.Id, McpProtocol.InitializeResult(version, _version));
            case "ping":
                return McpProtocol.Result(request.Id, new JsonObject());
            case "tools/list":
                return McpProtocol.Result(request.Id, new JsonObject { ["tools"] = new JsonArray() });
            default:
                return McpProtocol.Error(request.Id, McpProtocol.ServerUnavailable,
                    "NeoShell isn't running, or agent access is off in its settings");
        }
    }

    private async Task ToClientAsync(string line)
    {
        await _clientWriting.WaitAsync(CancellationToken.None);
        try
        {
            await _toClient.WriteLineAsync(line);
            await _toClient.FlushAsync(CancellationToken.None);
        }
        catch (IOException)
        {
            // The client has gone; its stdin closing ends the bridge.
        }
        finally
        {
            _clientWriting.Release();
        }
    }
}
