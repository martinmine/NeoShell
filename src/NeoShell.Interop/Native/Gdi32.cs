using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Gdi32
{
    public const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    public static partial int GetObject(nint gdiObject, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    public static partial int GetDIBits(nint hdc, nint bitmap, uint start, uint lines, void* bits, BITMAPINFOHEADER* info, uint usage);

    public const uint SRCCOPY = 0x00CC0020;
    // Includes layered windows (menus, tooltips, acrylic popups) in the copy.
    public const uint CAPTUREBLT = 0x40000000;

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint hdc, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint gdiObject);
}
