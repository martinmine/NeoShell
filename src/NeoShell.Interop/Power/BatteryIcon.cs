using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Power;

/// <summary>The colour of the battery's fill, as Windows picks it: the text colour, green, yellow or red.</summary>
public enum BatteryFill { Default, Adequate, Warning, Critical }

/// <summary>
/// The battery as Explorer's taskbar draws it: an outline (with a bolt while charging, a plug while plugged in) and a
/// fill over it, both glyphs of SystemTray's own font (SysBatt Fluent Icons), the fill in its own colour.
/// </summary>
public sealed record BatteryGlyphs(string Outline, string Fill, BatteryFill FillColor);

/// <summary>
/// The glyphs of the PC's battery as Windows works them out for its taskbar (the charge, charging, plugged in, nearly
/// empty). Read on thread-pool threads, as <see cref="Privacy.CapabilityUsage"/> is.
/// </summary>
public sealed class BatteryIcon : IDisposable
{
    private readonly Lock _lock = new();
    private IBatteryIcon? _icon;
    private long _token;
    private bool _disposed;

    /// <summary>Starts watching the battery, on the thread pool; <see cref="Changed"/> follows the first read.</summary>
    public BatteryIcon() => ThreadPool.QueueUserWorkItem(_ => Watch());

    /// <summary>The glyphs changed. Raised on a thread-pool thread.</summary>
    public event Action? Changed;

    /// <summary>The battery's glyphs as last read; null without a battery, or before the first read.</summary>
    public BatteryGlyphs? Glyphs { get; private set; }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            if (_icon is not null && _token != 0)
                _icon.RemoveIconDataChanged(_token);
            _icon = null;
        }
    }

    private void Watch()
    {
        try
        {
            var statics = Combase.GetActivationFactory<IBatteryIconStatics>("WindowsUdk.UI.Shell.PowerUX.BatteryIcon");
            Marshal.ThrowExceptionForHR(statics.GetLocalCompositeBatteryIcon(out IBatteryIcon icon));
            lock (_lock)
            {
                if (_disposed)
                    return;
                _icon = icon;
                var handler = new BatteryIconChangedHandler(() => ThreadPool.QueueUserWorkItem(_ => Read()));
                Marshal.ThrowExceptionForHR(icon.AddIconDataChanged(handler, out _token));
            }
            Read();
        }
        catch (Exception ex)
        {
            // Not in this Windows build, for one: the battery shows Segoe Fluent Icons' glyphs instead.
            NativeCallback.Report(new InvalidOperationException("Can't read Windows' battery icon", ex));
        }
    }

    private void Read()
    {
        try
        {
            lock (_lock)
            {
                if (_icon is null)
                    return;
                Marshal.ThrowExceptionForHR(_icon.GetMobileIconData(out IBatteryIconData data));
                Marshal.ThrowExceptionForHR(data.GetIsBatteryPresent(out bool present));
                Marshal.ThrowExceptionForHR(data.GetFillColor(out int color));
                BatteryGlyphs? glyphs = present
                    ? new BatteryGlyphs(TakeString(data.GetOutlineGlyph), TakeString(data.GetFillGlyph), (BatteryFill)color)
                    : null;
                if (glyphs == Glyphs)
                    return;
                Glyphs = glyphs;
            }
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
    }

    private delegate int StringGetter(out nint value);

    private static string TakeString(StringGetter get)
    {
        Marshal.ThrowExceptionForHR(get(out nint value));
        try
        {
            return Combase.GetString(value);
        }
        finally
        {
            Combase.WindowsDeleteString(value);
        }
    }
}

[GeneratedComClass]
internal sealed partial class BatteryIconChangedHandler(Action onChange) : IBatteryIconChangedHandler
{
    public int Invoke(nint sender, nint args)
    {
        try
        {
            onChange();
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 0;
    }
}
