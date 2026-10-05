using Microsoft.UI.Dispatching;
using NeoShell.Interop.Wireless;
using NeoShell.Logging;

namespace NeoShell.Widgets;

/// <summary>
/// Reads the wireless devices' batteries while anyone listens to <see cref="Updated"/>: at once, every
/// <see cref="Interval"/>, and shortly after a device is plugged in or removed. The sidebar shares one, so a moved
/// widget shows the devices at once.
/// </summary>
internal sealed class WirelessMonitor
{
    // Device changes come in bursts (a receiver is several devices); one read after the last.
    private static readonly TimeSpan s_settle = TimeSpan.FromSeconds(2);

    private readonly WirelessDevices _devices = new();
    private readonly DispatcherQueueTimer _poll;
    private readonly DispatcherQueueTimer _settle;
    private Action? _updated;
    private bool _reading;

    public WirelessMonitor()
    {
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _poll = dispatcher.CreateTimer();
        _poll.Interval = TimeSpan.FromMinutes(5);
        _poll.Tick += (_, _) => Read();
        _settle = dispatcher.CreateTimer();
        _settle.Interval = s_settle;
        _settle.IsRepeating = false;
        _settle.Tick += (_, _) => Read();
    }

    /// <summary>Raised on the UI thread after each read; reading runs only while someone listens.</summary>
    public event Action? Updated
    {
        add
        {
            bool first = _updated is null;
            _updated += value;
            if (first)
            {
                _poll.Start();
                Read();
            }
        }
        remove
        {
            _updated -= value;
            if (_updated is null)
            {
                _poll.Stop();
                _settle.Stop();
            }
        }
    }

    /// <summary>The devices as last read; null before the first read.</summary>
    public IReadOnlyList<WirelessDevice>? Devices { get; private set; }

    public TimeSpan Interval
    {
        get => _poll.Interval;
        set => _poll.Interval = value;
    }

    /// <summary>A device was plugged in or removed: read again once things settle.</summary>
    public void DevicesChanged()
    {
        if (_updated is null)
            return;
        _settle.Stop();
        _settle.Start();
    }

    private async void Read()
    {
        if (_reading)
            return;

        _reading = true;
        try
        {
            Devices = await _devices.ReadAsync(ex => Log.Warn("Could not read wireless devices", ex));
            _updated?.Invoke();
        }
        finally
        {
            _reading = false;
        }
    }
}
