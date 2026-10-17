using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;

namespace NeoShell.Interop.Display;

/// <summary>
/// A monitor whose brightness is set over its cable with DDC/CI (VCP code 0x10, <c>dxva2.dll</c>'s Monitor
/// Configuration API), as Twinkle Tray and the monitor's own buttons do: desktop monitors, which Windows' brightness
/// slider doesn't reach. Every call talks to the monitor and takes tens of milliseconds, so keep them off the UI
/// thread, one at a time per monitor.
/// </summary>
public sealed unsafe class MonitorBrightness : IDisposable
{
    private readonly nint _handle;
    private readonly uint _maximum;

    private MonitorBrightness(nint handle, string name, uint maximum, int percent)
    {
        _handle = handle;
        _maximum = maximum;
        Name = name;
        Percent = percent;
    }

    /// <summary>The monitor's own description (often just "Generic PnP Monitor").</summary>
    public string Name { get; }

    /// <summary>The brightness when it was opened or last set, 0 to 100.</summary>
    public int Percent { get; private set; }

    /// <summary>
    /// The monitors that answer DDC/CI now. A laptop's panel, a virtual machine's screen and a monitor with DDC/CI
    /// turned off in its menu don't, and aren't listed.
    /// </summary>
    public static IReadOnlyList<MonitorBrightness> Open()
    {
        var monitors = new List<MonitorBrightness>();
        foreach (DisplayMonitor display in DisplayMonitor.GetAll())
        {
            if (!Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(display.Handle, out uint count) || count == 0)
                continue;

            var physical = new Dxva2.PHYSICAL_MONITOR[count];
            fixed (Dxva2.PHYSICAL_MONITOR* buffer = physical)
            {
                if (!Dxva2.GetPhysicalMonitorsFromHMONITOR(display.Handle, count, buffer))
                    continue;

                for (int i = 0; i < count; i++)
                {
                    nint handle = buffer[i].hPhysicalMonitor;
                    if (Read(handle) is (uint current, uint maximum))
                        monitors.Add(new MonitorBrightness(handle, new string(buffer[i].szPhysicalMonitorDescription), maximum, ToPercent(current, maximum)));
                    else
                        Dxva2.DestroyPhysicalMonitor(handle);
                }
            }
        }
        return monitors;
    }

    /// <summary>Sets the brightness (0 to 100); false when the monitor didn't take it (unplugged, asleep).</summary>
    public bool SetPercent(int percent)
    {
        if (!Dxva2.SetVCPFeature(_handle, Dxva2.VcpBrightness, FromPercent(percent, _maximum)))
            return false;

        Percent = Math.Clamp(percent, 0, 100);
        return true;
    }

    public void Dispose() => Dxva2.DestroyPhysicalMonitor(_handle);

    internal static int ToPercent(uint current, uint maximum) =>
        (int)Math.Round(Math.Min(current, maximum) * 100.0 / maximum);

    internal static uint FromPercent(int percent, uint maximum) =>
        (uint)Math.Round(Math.Clamp(percent, 0, 100) * maximum / 100.0);

    // Monitors miss a DDC/CI request now and then (more so just after waking), so a failed read is tried again once.
    private static (uint Current, uint Maximum)? Read(nint handle)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (Dxva2.GetVCPFeatureAndVCPFeatureReply(handle, Dxva2.VcpBrightness, out _, out uint current, out uint maximum))
                return maximum > 0 ? (current, maximum) : null;
        }
        return null;
    }
}
