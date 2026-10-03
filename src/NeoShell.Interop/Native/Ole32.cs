using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Ole32
{
    public const ushort VT_LPWSTR = 31;

    /// <summary>PROPVARIANT, laid out for 64-bit: a type tag and a 16-byte union of which only a pointer is read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PROPVARIANT
    {
        public ushort vt;
        public ushort reserved1;
        public ushort reserved2;
        public ushort reserved3;
        public nint pointer;
        public nint reserved4;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(PROPVARIANT* value);
}
