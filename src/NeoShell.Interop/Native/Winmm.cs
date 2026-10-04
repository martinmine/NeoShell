using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static partial class Winmm
{
    public const uint SND_ASYNC = 0x0001;
    public const uint SND_NODEFAULT = 0x0002;
    public const uint SND_ALIAS = 0x0001_0000;
    public const uint SND_SYSTEM = 0x0020_0000;

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PlaySound(string sound, nint module, uint flags);
}
