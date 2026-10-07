using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Ntdll
{
    /// <summary>Publishes a Windows Notification Facility state; the state name goes by value. Returns an NTSTATUS.</summary>
    [LibraryImport("ntdll.dll")]
    public static partial int RtlPublishWnfStateData(ulong stateName, nint typeId, void* buffer, uint length, nint explicitScope);

    /// <summary>Reads a Windows Notification Facility state; the state name goes by reference. Returns an NTSTATUS.</summary>
    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryWnfStateData(
        in ulong stateName, nint typeId, nint explicitScope, out uint changeStamp, void* buffer, ref uint length);
}
