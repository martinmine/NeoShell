using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Dwmapi
{
    public const uint DWMWA_EXCLUDED_FROM_PEEK = 12;
    public const uint DWMWA_CLOAK = 13;
    public const uint DWMWA_CLOAKED = 14;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const uint DWMWA_BORDER_COLOR = 34;

    public const int DWMWCP_DONOTROUND = 1;
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    public const uint DWM_BB_ENABLE = 0x01;
    public const uint DWM_BB_BLURREGION = 0x02;

    public const uint DWM_TNP_RECTDESTINATION = 0x01;
    public const uint DWM_TNP_VISIBLE = 0x08;
    public const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public User32.RECT rcDestination;
        public User32.RECT rcSource;
        public byte opacity;
        public int fVisible;
        public int fSourceClientAreaOnly;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        public int fEnable;
        public nint hRgnBlur;
        public int fTransitionOnMaximized;
    }

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmEnableBlurBehindWindow(nint hwnd, DWM_BLURBEHIND* blurBehind);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmUnregisterThumbnail(nint thumbnail);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmUpdateThumbnailProperties(nint thumbnail, DWM_THUMBNAIL_PROPERTIES* properties);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmQueryThumbnailSourceSize(nint thumbnail, out User32.SIZE size);

    /// <summary>Peek's trigger as Explorer's taskbar passes it.</summary>
    public const uint LPT_TASKBAR = 1;

    // Undocumented, exported by ordinal only; it's what Explorer's taskbar calls for peek, and there is no public
    // equivalent. The last parameter was added in Windows 10.
    [LibraryImport("dwmapi.dll", EntryPoint = "#113")]
    public static partial int DwmpActivateLivePreview(int activate, nint peekWindow, nint topmostWindow, uint trigger, nint reserved);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);
}
