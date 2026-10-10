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

    /// <summary>
    /// Calls <paramref name="callback"/> on a thread-pool thread each time the state is published after
    /// <paramref name="changeStamp"/>; the state name goes by value. Returns an NTSTATUS.
    /// </summary>
    /// <param name="callback">
    /// <c>int (ulong stateName, uint changeStamp, nint typeId, nint context, void* buffer, uint length)</c>, returning an
    /// NTSTATUS.
    /// </param>
    [LibraryImport("ntdll.dll")]
    public static partial int RtlSubscribeWnfStateChangeNotification(
        out nint subscription, ulong stateName, uint changeStamp,
        delegate* unmanaged<ulong, uint, nint, nint, void*, uint, int> callback, nint context, nint typeId,
        uint serializationGroup, uint flags);

    /// <summary>Ends a subscription. Returns an NTSTATUS.</summary>
    [LibraryImport("ntdll.dll")]
    public static partial int RtlUnsubscribeWnfStateChangeNotification(nint subscription);
}
