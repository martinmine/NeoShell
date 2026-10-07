using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static partial class Cabinet
{
    public const uint COMPRESS_ALGORITHM_LZMS = 5;

    [LibraryImport("cabinet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateDecompressor(uint algorithm, nint allocationRoutines, out nint decompressor);

    [LibraryImport("cabinet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool Decompress(
        nint decompressor, byte* compressedData, nuint compressedDataSize, byte* uncompressedBuffer, nuint uncompressedBufferSize, out nuint uncompressedDataSize);

    [LibraryImport("cabinet.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseDecompressor(nint decompressor);
}
