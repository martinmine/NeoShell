using NeoShell.Interop.Windowing;
using Windows.Graphics;

namespace NeoShell.Snap;

/// <summary>
/// A snap group's preview, as Explorer's taskbar and Alt+Tab draw it: its monitor's work area in small, the wallpaper
/// with the group's windows where they are, each a live DWM preview, drawn into another window.
/// </summary>
internal sealed class SnapGroupPreview : IDisposable
{
    // Each window's preview keeps this share of the preview inside its place, so the wallpaper shows round them as in
    // Explorer's (measured: 4 of 200 across, 10 of 107 down).
    private const double GapAcross = 0.02;
    private const double GapDown = 0.09;

    private readonly RectInt32 _workArea;
    private readonly DwmThumbnail? _wallpaper;
    private readonly List<(DwmThumbnail Thumbnail, RectInt32 Bounds)> _windows = [];

    /// <param name="destination">The window the preview is drawn in.</param>
    /// <param name="windows">The group's windows.</param>
    public SnapGroupPreview(nint destination, IReadOnlyList<nint> windows, WindowSnapping snapping)
    {
        nint monitorHandle = TopLevelWindows.NearestMonitorOf(windows[0]);
        IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
        DisplayMonitor monitor = monitors.FirstOrDefault(m => m.Handle == monitorHandle) ?? monitors.First(m => m.IsPrimary);
        _workArea = monitor.WorkArea;

        nint wallpaper = snapping.WallpaperOn(monitor.Handle);
        if (wallpaper != 0)
        {
            _wallpaper = TryRegister(destination, wallpaper);
            // The wallpaper's window covers the monitor; the preview is of the work area.
            if (_wallpaper is not null)
                _wallpaper.SourceArea = new RectInt32(_workArea.X - monitor.Bounds.X, _workArea.Y - monitor.Bounds.Y, _workArea.Width, _workArea.Height);
        }

        // Bottom first, so the top window's preview is drawn over the others.
        List<nint> zOrder = [.. TopLevelWindows.GetAll()];
        foreach (nint window in windows.OrderByDescending(zOrder.IndexOf))
        {
            if (TopLevelWindows.IsMinimized(window) || TryRegister(destination, window) is not { } thumbnail)
                continue;
            // What's drawn of the window, without the invisible borders round it.
            RectInt32 outer = TopLevelWindows.GetBounds(window);
            RectInt32 visible = TopLevelWindows.GetVisibleBounds(window);
            thumbnail.SourceArea = new RectInt32(visible.X - outer.X, visible.Y - outer.Y, visible.Width, visible.Height);
            _windows.Add((thumbnail, visible));
        }
    }

    /// <summary>Width over height: the work area's.</summary>
    public double Aspect => _workArea.Height > 0 ? (double)_workArea.Width / _workArea.Height : 16.0 / 9;

    /// <summary>Draws the preview at <paramref name="bounds"/>, in pixels of the window it's drawn in.</summary>
    public void Show(RectInt32 bounds)
    {
        _wallpaper?.Show(bounds);
        double factor = (double)bounds.Width / _workArea.Width;
        int across = (int)Math.Round(GapAcross * bounds.Width);
        int down = (int)Math.Round(GapDown * bounds.Height);
        foreach ((DwmThumbnail thumbnail, RectInt32 window) in _windows)
        {
            thumbnail.Show(new RectInt32(
                bounds.X + (int)Math.Round((window.X - _workArea.X) * factor) + across,
                bounds.Y + (int)Math.Round((window.Y - _workArea.Y) * factor) + down,
                Math.Max(1, (int)Math.Round(window.Width * factor) - 2 * across),
                Math.Max(1, (int)Math.Round(window.Height * factor) - 2 * down)));
        }
    }

    /// <summary>Shows only what's above <paramref name="visibleBottom"/> (see <see cref="DwmThumbnail.Clip"/>).</summary>
    public void Clip(int visibleBottom)
    {
        _wallpaper?.Clip(visibleBottom);
        foreach ((DwmThumbnail thumbnail, _) in _windows)
            thumbnail.Clip(visibleBottom);
    }

    public void Dispose()
    {
        _wallpaper?.Dispose();
        foreach ((DwmThumbnail thumbnail, _) in _windows)
            thumbnail.Dispose();
    }

    private static DwmThumbnail? TryRegister(nint destination, nint source)
    {
        try
        {
            return new DwmThumbnail(destination, source);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
