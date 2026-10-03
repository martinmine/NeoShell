using System.Runtime.InteropServices;
using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

/// <summary>A live, DWM-drawn preview of one window, shown inside another window.</summary>
public sealed unsafe class DwmThumbnail : IDisposable
{
    private nint _thumbnail;

    public DwmThumbnail(nint destination, nint source)
    {
        Marshal.ThrowExceptionForHR(Dwmapi.DwmRegisterThumbnail(destination, source, out _thumbnail));
    }

    /// <summary>Size of the source window in pixels.</summary>
    public SizeInt32 SourceSize =>
        Dwmapi.DwmQueryThumbnailSourceSize(_thumbnail, out User32.SIZE size) == 0 ? new SizeInt32(size.cx, size.cy) : default;

    /// <summary>Shows the preview at <paramref name="bounds"/>, in pixels relative to the destination window.</summary>
    public void Show(RectInt32 bounds)
    {
        var properties = new Dwmapi.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = Dwmapi.DWM_TNP_RECTDESTINATION | Dwmapi.DWM_TNP_VISIBLE | Dwmapi.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = User32.RECT.From(bounds),
            fVisible = 1,
            fSourceClientAreaOnly = 0,
        };
        Dwmapi.DwmUpdateThumbnailProperties(_thumbnail, &properties);
    }

    public void Dispose()
    {
        if (_thumbnail != 0)
            Dwmapi.DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = 0;
    }
}
