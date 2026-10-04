using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Shlwapi
{
    /// <summary>A memory <c>IStream</c> over a copy of the bytes; 0 on failure.</summary>
    [LibraryImport("shlwapi.dll")]
    public static partial nint SHCreateMemStream(byte* data, uint size);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHLoadIndirectString(string source, char* output, uint length, nint reserved);
}
