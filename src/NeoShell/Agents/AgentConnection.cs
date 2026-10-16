using NeoShell.Interop.Shell;

namespace NeoShell.Agents;

/// <summary>
/// An agent connected to NeoShell: its MCP session, and the processes behind it, the bridge first. The agent itself
/// is the bridge's parent (Claude Code, an editor...), which started <c>NeoShell.exe /mcp</c>.
/// </summary>
public sealed class AgentConnection(McpSession session, IReadOnlyList<ProcessEntry> processes, DateTime since)
{
    // Started between an agent and its bridge on the way: not the agent.
    private static readonly HashSet<string> s_launchers = new(StringComparer.OrdinalIgnoreCase)
    {
        "NeoShell.exe", "cmd.exe", "conhost.exe",
    };

    public McpSession Session { get; } = session;

    /// <summary>The connected process and those that started it, nearest first.</summary>
    public IReadOnlyList<ProcessEntry> Processes { get; } = processes;

    public DateTime Since { get; } = since;

    /// <summary>The agent's own process: the first that isn't the bridge or a launcher; null if none is known.</summary>
    public ProcessEntry? Agent => AgentProcess(Processes);

    public static ProcessEntry? AgentProcess(IReadOnlyList<ProcessEntry> processes) =>
        processes.FirstOrDefault(process => !s_launchers.Contains(process.Name));

    /// <summary>The line under the agent's name in the Agents settings.</summary>
    public static string Describe(ProcessEntry? agent, DateTime since, bool revoked)
    {
        string who = agent is null ? "Unknown process" : $"{agent.Name} (process {agent.Id})";
        return revoked ? $"{who}, disconnected" : $"{who}, connected since {since:t}";
    }
}
