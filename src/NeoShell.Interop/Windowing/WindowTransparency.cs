using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

public static unsafe class WindowTransparency
{
    /// <summary>
    /// Whether DWM shows what's behind the window through its see-through pixels; otherwise they're composed onto
    /// black. Blur-behind with an empty region turns on the window's alpha without any blur.
    /// </summary>
    public static void SetSeeThrough(nint hwnd, bool enable)
    {
        nint region = enable ? Gdi32.CreateRectRgn(-2, -2, -1, -1) : 0;
        var blurBehind = new Dwmapi.DWM_BLURBEHIND
        {
            dwFlags = Dwmapi.DWM_BB_ENABLE | (enable ? Dwmapi.DWM_BB_BLURREGION : 0),
            fEnable = enable ? 1 : 0,
            hRgnBlur = region,
        };
        Dwmapi.DwmEnableBlurBehindWindow(hwnd, &blurBehind);
        if (region != 0)
            Gdi32.DeleteObject(region);
    }
}
