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

    private RectInt32 _bounds;
    private int _visibleBottom = int.MaxValue;

    /// <summary>Shows the preview at <paramref name="bounds"/>, in pixels relative to the destination window.</summary>
    public void Show(RectInt32 bounds)
    {
        _bounds = bounds;
        Update();
    }

    /// <summary>
    /// Shows only the part of the preview above <paramref name="visibleBottom"/>, in pixels from the destination
    /// window's top. DWM draws previews over the window, ignoring its region.
    /// </summary>
    public void Clip(int visibleBottom)
    {
        _visibleBottom = visibleBottom;
        if (_bounds.Height > 0)
            Update();
    }

    private void Update()
    {
        int height = Math.Clamp(_visibleBottom - _bounds.Y, 0, _bounds.Height);
        SizeInt32 source = SourceSize;
        var properties = new Dwmapi.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = Dwmapi.DWM_TNP_RECTDESTINATION | Dwmapi.DWM_TNP_RECTSOURCE | Dwmapi.DWM_TNP_VISIBLE
                | Dwmapi.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = User32.RECT.From(_bounds with { Height = height }),
            // The same part of the window, so what shows isn't squashed.
            rcSource = new User32.RECT { right = source.Width, bottom = _bounds.Height > 0 ? source.Height * height / _bounds.Height : 0 },
            fVisible = height > 0 ? 1 : 0,
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
