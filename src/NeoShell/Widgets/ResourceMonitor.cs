using System.Net.NetworkInformation;
using Microsoft.UI.Dispatching;
using NeoShell.Interop.Performance;

namespace NeoShell.Widgets;

/// <summary>
/// Samples processor, memory, disk and network use once a second while anyone listens to <see cref="Sampled"/>,
/// keeping the last minute for the graphs. The sidebar shares one, so the resource widget keeps its graphs when it's
/// moved (a new widget is made each time).
/// </summary>
internal sealed class ResourceMonitor : IDisposable
{
    public const int HistoryLength = 60;

    private readonly DispatcherQueueTimer _timer;
    private readonly List<double> _cpu = [];
    private readonly List<double> _memory = [];
    private readonly List<double> _disk = [];
    private readonly List<double> _network = [];
    private SystemUsage? _usage;
    private Action? _sampled;
    private (long Received, long Sent, long Time)? _lastNetwork;
    private long _lastSample;

    public ResourceMonitor()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Sample();
    }

    /// <summary>Raised on the UI thread after each sample; sampling runs only while someone listens.</summary>
    public event Action? Sampled
    {
        add
        {
            _sampled += value;
            if (!_timer.IsRunning)
                Start();
        }
        remove
        {
            _sampled -= value;
            if (_sampled is null)
                _timer.Stop();
        }
    }

    public UsageSample Latest { get; private set; }

    /// <summary>Bytes a second received and sent, over all network adapters.</summary>
    public double DownloadRate { get; private set; }
    public double UploadRate { get; private set; }

    /// <summary>The last minute, oldest first: percentages, and the network's bytes a second both ways.</summary>
    public IReadOnlyList<double> CpuHistory => _cpu;
    public IReadOnlyList<double> MemoryHistory => _memory;
    public IReadOnlyList<double> DiskHistory => _disk;
    public IReadOnlyList<double> NetworkHistory => _network;

    public void Dispose()
    {
        _timer.Stop();
        _usage?.Dispose();
    }

    private void Start()
    {
        // After a long pause the graphs would join two moments far apart.
        if (Environment.TickCount64 - _lastSample > 5000)
        {
            foreach (List<double> history in (List<double>[])[_cpu, _memory, _disk, _network])
                history.Clear();
            _lastNetwork = null;
        }
        _usage ??= new SystemUsage();
        _timer.Start();
        Sample();
    }

    private void Sample()
    {
        _lastSample = Environment.TickCount64;
        Latest = _usage!.Sample();
        Add(_cpu, Latest.CpuPercent);
        Add(_memory, Latest.MemoryTotal == 0 ? 0 : 100.0 * Latest.MemoryUsed / Latest.MemoryTotal);
        Add(_disk, Latest.DiskPercent);

        (long received, long sent) = NetworkTotals();
        long now = Environment.TickCount64;
        if (_lastNetwork is { } last && now > last.Time)
        {
            double seconds = (now - last.Time) / 1000.0;
            // Counters start over when an adapter comes and goes.
            DownloadRate = Math.Max(0, received - last.Received) / seconds;
            UploadRate = Math.Max(0, sent - last.Sent) / seconds;
            Add(_network, DownloadRate + UploadRate);
        }
        _lastNetwork = (received, sent, now);
        _sampled?.Invoke();
    }

    private static void Add(List<double> history, double value)
    {
        if (history.Count == HistoryLength)
            history.RemoveAt(0);
        history.Add(value);
    }

    private static (long Received, long Sent) NetworkTotals()
    {
        long received = 0;
        long sent = 0;
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }
                IPInterfaceStatistics statistics = adapter.GetIPStatistics();
                received += statistics.BytesReceived;
                sent += statistics.BytesSent;
            }
        }
        catch (NetworkInformationException)
        {
            // Adapters changing under us; the next sample counts again.
        }
        return (received, sent);
    }
}
