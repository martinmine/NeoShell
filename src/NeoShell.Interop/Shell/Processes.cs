using Microsoft.Win32.SafeHandles;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>A running process: its id, the id of the process that started it, and its executable's file name.</summary>
public sealed record ProcessEntry(int Id, int ParentId, string Name);

/// <summary>Who is on the other end of a pipe, and which processes started it.</summary>
public static unsafe class Processes
{
    private const int MaxAncestors = 16;

    /// <summary>The id of the process that connected to a pipe NeoShell serves, or null if Windows doesn't say.</summary>
    public static int? PipeClient(SafePipeHandle pipe) =>
        Kernel32.GetNamedPipeClientProcessId(pipe, out uint processId) ? (int)processId : null;

    /// <summary>
    /// The process and those that started it, nearest first, as far as they still run. A parent that has exited may
    /// have its id taken by a newer process, so the chain is a guide (the process that started an agent's bridge is
    /// normally still there: it's the agent).
    /// </summary>
    public static IReadOnlyList<ProcessEntry> Ancestry(int processId)
    {
        var running = new Dictionary<int, ProcessEntry>();
        using (SafeFileHandle snapshot = Kernel32.CreateToolhelp32Snapshot(Kernel32.TH32CS_SNAPPROCESS, 0))
        {
            if (snapshot.IsInvalid)
                return [];
            Kernel32.PROCESSENTRY32W entry = new() { dwSize = (uint)sizeof(Kernel32.PROCESSENTRY32W) };
            for (bool more = Kernel32.Process32First(snapshot, &entry); more; more = Kernel32.Process32Next(snapshot, &entry))
                running[(int)entry.th32ProcessID] = new ProcessEntry((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, new string(entry.szExeFile));
        }

        var chain = new List<ProcessEntry>();
        while (chain.Count < MaxAncestors && running.TryGetValue(processId, out ProcessEntry? process)
            && !chain.Any(known => known.Id == process.Id))
        {
            chain.Add(process);
            processId = process.ParentId;
        }
        return chain;
    }
}
