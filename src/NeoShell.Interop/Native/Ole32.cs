using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Ole32
{
    public const ushort VT_LPWSTR = 31;

    public const uint CLSCTX_INPROC_SERVER = 0x1;
    public const uint CLSCTX_LOCAL_SERVER = 0x4;

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

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    /// <summary>Creates a COM object and returns it as <typeparamref name="T"/>, or throws.</summary>
    public static T Create<T>(Guid clsid, uint context)
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, 0, context, typeof(T).GUID, out nint instance));
        return Com.ComPointer.TakeOwnership<T>(instance);
    }

    [LibraryImport("ole32.dll")]
    public static partial int GetRunningObjectTable(uint reserved, out nint table);

    [LibraryImport("ole32.dll")]
    public static partial int CreateClassMoniker(in Guid clsid, out nint moniker);

    [LibraryImport("ole32.dll")]
    public static partial int OleInitialize(nint reserved);

    [LibraryImport("ole32.dll")]
    public static partial void OleUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int RegisterDragDrop(nint hwnd, nint dropTarget);

    [LibraryImport("ole32.dll")]
    public static partial int RevokeDragDrop(nint hwnd);

    public const uint REGCLS_MULTIPLEUSE = 1;

    [LibraryImport("ole32.dll")]
    public static partial int CoRegisterClassObject(in Guid clsid, nint unknown, uint context, uint flags, out uint cookie);

    [LibraryImport("ole32.dll")]
    public static partial int CoRevokeClassObject(uint cookie);
}
