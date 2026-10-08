using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Rasapi32
{
    public const uint ERROR_BUFFER_TOO_SMALL = 603;

    /// <summary>RASCONNW as of Windows 7 (1392 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RASCONN
    {
        public uint dwSize;
        public nint hrasconn;
        public fixed char szEntryName[257];
        public fixed char szDeviceType[17];
        public fixed char szDeviceName[129];
        public fixed char szPhonebook[260];
        public uint dwSubEntry;
        public Guid guidEntry;
        public uint dwFlags;
        public long luid;
        public Guid guidCorrelationId;
    }

    [LibraryImport("rasapi32.dll", EntryPoint = "RasEnumConnectionsW")]
    public static partial uint RasEnumConnections(RASCONN* connections, ref uint bytes, out uint count);

    [LibraryImport("rasapi32.dll", EntryPoint = "RasHangUpW")]
    public static partial uint RasHangUp(nint connection);
}
