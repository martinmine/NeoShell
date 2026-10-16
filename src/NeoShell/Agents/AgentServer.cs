using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.UI.Dispatching;
using NeoShell.Logging;

namespace NeoShell.Agents;

/// <summary>
/// The named pipe agents reach NeoShell through, via <c>NeoShell.exe /mcp</c> (<see cref="McpBridge"/>): one MCP
/// session per connection. Only the current user's processes can open it, and the pipe's default medium label keeps
/// low-integrity and AppContainer processes (browser sandboxes) out. It exists only while agent access is on.
/// </summary>
public sealed class AgentServer : IDisposable
{
    private const int MaxConnections = 8;
    // Long enough for an index search (which gives up after 4 s), short enough that a hung UI thread is reported.
    private static readonly TimeSpan s_callTimeout = TimeSpan.FromSeconds(10);

    private readonly DispatcherQueue _dispatcher;
    private readonly IReadOnlyList<AgentTool> _tools;
    private readonly string _version = typeof(AgentServer).Assembly.GetName().Version?.ToString() ?? "0";
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private readonly List<NamedPipeServerStream> _pipes = [];

    /// <summary>The pipe's name, one per Windows session as NeoShell is one per session.</summary>
    public static string PipeName { get; } = $"NeoShell.Agents.{Process.GetCurrentProcess().SessionId}";

    public AgentServer(DispatcherQueue dispatcher, IReadOnlyList<AgentTool> tools)
    {
        _dispatcher = dispatcher;
        _tools = tools;
        _ = Task.Run(ListenAsync);
        Log.Info($"Agent access on: pipe {PipeName}");
    }

    public void Dispose()
    {
        _stop.Cancel();
        lock (_lock)
        {
            foreach (NamedPipeServerStream pipe in _pipes)
                pipe.Dispose();
            _pipes.Clear();
        }
        Log.Info("Agent access off");
    }

    private async Task ListenAsync()
    {
        // The first instance must be NeoShell's own: a process that took the name first would hear every agent.
        PipeOptions first = PipeOptions.FirstPipeInstance;
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, MaxConnections, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | first);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (first != PipeOptions.None)
                {
                    Log.Error($"Agent access: the pipe {PipeName} is taken by another process", ex);
                    return;
                }
                // Every instance has an agent on it; listen again once one leaves.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }
            first = PipeOptions.None;
            if (!Track(pipe))
                return;

            try
            {
                await pipe.WaitForConnectionAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
            {
                Untrack(pipe);
                if (_stop.IsCancellationRequested)
                    return;
                continue;
            }
            _ = ServeAsync(pipe);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        var session = new McpSession(_tools, RunOnUiThread, s_callTimeout, _version);
        Log.Info("Agent connected");
        try
        {
            await session.RunAsync(pipe, _stop.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The agent or NeoShell closed the pipe.
        }
        catch (Exception ex)
        {
            Log.Error($"Agent {session.ClientName}'s session failed", ex);
        }
        Log.Info($"Agent {session.ClientName} disconnected");
        Untrack(pipe);
    }

    private Task<ToolResult> RunOnUiThread(Func<Task<ToolResult>> handler)
    {
        var done = new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                done.SetResult(await handler());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        if (!queued)
            done.SetException(new InvalidOperationException("The UI thread has shut down"));
        return done.Task;
    }

    private bool Track(NamedPipeServerStream pipe)
    {
        lock (_lock)
        {
            if (_stop.IsCancellationRequested)
            {
                pipe.Dispose();
                return false;
            }
            _pipes.Add(pipe);
            return true;
        }
    }

    private void Untrack(NamedPipeServerStream pipe)
    {
        lock (_lock)
            _pipes.Remove(pipe);
        pipe.Dispose();
    }
}
