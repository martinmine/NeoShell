using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Secur32
{
    public const int NameDisplay = 3;

    [LibraryImport("secur32.dll", EntryPoint = "GetUserNameExW")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GetUserNameEx(int format, char* name, ref uint size);
}
