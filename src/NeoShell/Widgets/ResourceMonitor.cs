using System.Net.NetworkInformation;
using Microsoft.UI.Dispatching;
using NeoShell.Interop.Performance;
using NeoShell.Interop.Power;

namespace NeoShell.Widgets;

/// <summary>A fixed drive's space, and how busy it was in the last second.</summary>
/// <param name="Name">"C:".</param>
public sealed record DriveUsage(string Name, ulong Free, ulong Total, double BusyPercent);

/// <summary>A network adapter that's up, and its bytes a second each way.</summary>
public sealed record AdapterUsage(string Id, string Name, double DownloadRate, double UploadRate);

/// <summary>
/// Samples processor, GPU, memory, disk and network use once a second while anyone listens to <see cref="Sampled"/>
/// and the display is on, keeping the last minute of each for the graphs. The sidebar shares one, so the resource widget keeps its graphs
/// when it's moved (a new widget is made each time).
/// </summary>
internal sealed class ResourceMonitor : IDisposable
{
    public const int HistoryLength = 60;

    // The keys of History.
    public const string Cpu = "cpu";
    public const string Gpu = "gpu";
    public const string Memory = "memory";
    public const string Disk = "disk";
    public const string Network = "network";
    public static string DriveKey(string name) => "drive:" + name;
    public static string AdapterKey(string id) => "adapter:" + id;

    private readonly DispatcherQueueTimer _timer;
    private readonly DisplayPower _display = new();
    private readonly Dictionary<string, List<double>> _history = [];
    private readonly Dictionary<string, (long Received, long Sent)> _lastBytes = [];
    private SystemUsage? _usage;
    private Action? _sampled;
    private long _lastSample;
    // Listing the adapters is most of a sample's cost (and a wake of other services), so they're listed again only
    // when an adapter comes, goes or changes its addresses; each sample reads just their byte counters. Null to list.
    private volatile NetworkInterface[]? _adapters;
    // A sample being read off the UI thread.
    private Task? _reading;

    public ResourceMonitor()
    {
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Sample();
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        _display.Changed += () => dispatcher.TryEnqueue(StartOrStop);
    }

    /// <summary>
    /// Raised on the UI thread after each sample; sampling runs only while someone listens, and the display is on.
    /// </summary>
    public event Action? Sampled
    {
        add
        {
            _sampled += value;
            StartOrStop();
        }
        remove
        {
            _sampled -= value;
            StartOrStop();
        }
    }

    public UsageSample? Latest { get; private set; }

    public IReadOnlyList<DriveUsage> Drives { get; private set; } = [];

    public IReadOnlyList<AdapterUsage> Adapters { get; private set; } = [];

    /// <summary>Bytes a second received and sent, over all adapters.</summary>
    public double DownloadRate => Adapters.Sum(adapter => adapter.DownloadRate);
    public double UploadRate => Adapters.Sum(adapter => adapter.UploadRate);

    /// <summary>
    /// The last minute of one resource, oldest first: percentages, and for the network bytes a second both ways.
    /// </summary>
    public IReadOnlyList<double> History(string key) => _history.TryGetValue(key, out List<double>? history) ? history : [];

    public void Dispose()
    {
        _timer.Stop();
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _display.Dispose();
        // The counters can't be closed while a read is using them.
        _reading?.Wait(TimeSpan.FromSeconds(1));
        _usage?.Dispose();
    }

    // Nobody sees the widgets while the display is off (a locked PC's goes off within a minute or so), and a wake each
    // second keeps a laptop from idling down.
    private void StartOrStop()
    {
        if (_sampled is null || !_display.IsOn)
            _timer.Stop();
        else if (!_timer.IsRunning)
            Start();
    }

    private void Start()
    {
        // After a long pause the graphs would join two moments far apart.
        if (Environment.TickCount64 - _lastSample > 5000 && _reading is null)
        {
            _history.Clear();
            _lastBytes.Clear();
        }
        _timer.Start();
        Sample();
    }

    private async void Sample()
    {
        if (_reading is not null)
            return;

        long now = Environment.TickCount64;
        double seconds = _lastSample == 0 ? 1 : Math.Max(0.1, (now - _lastSample) / 1000.0);
        _lastSample = now;

        // Off the UI thread: reading every network adapter and GPU engine takes about 10 ms, a hitch in a drag, and
        // setting the counters up the first time over 100 ms. Dispose waits for this read before it closes them.
        Task<(UsageSample, List<DriveUsage>, List<AdapterUsage>)> reading = Task.Run(() =>
        {
            _usage ??= new SystemUsage();
            UsageSample usage = _usage.Sample();
            return (usage, ReadDrives(usage), ReadAdapters(seconds));
        });
        _reading = reading;
        (UsageSample usage, List<DriveUsage> drives, List<AdapterUsage> adapters) = await reading;
        _reading = null;

        Latest = usage;
        var sampled = new Dictionary<string, double>
        {
            [Cpu] = usage.CpuPercent,
            [Memory] = usage.MemoryTotal == 0 ? 0 : 100.0 * usage.MemoryUsed / usage.MemoryTotal,
            [Disk] = usage.DiskPercent,
        };
        if (usage.GpuPercent is { } gpu)
            sampled[Gpu] = gpu;

        Drives = drives;
        foreach (DriveUsage drive in Drives)
            sampled[DriveKey(drive.Name)] = drive.BusyPercent;

        Adapters = adapters;
        foreach (AdapterUsage adapter in Adapters)
            sampled[AdapterKey(adapter.Id)] = adapter.DownloadRate + adapter.UploadRate;
        sampled[Network] = DownloadRate + UploadRate;

        // Drives and adapters that are gone take their graphs with them.
        foreach (string key in _history.Keys.Where(key => !sampled.ContainsKey(key)).ToList())
            _history.Remove(key);
        foreach ((string key, double value) in sampled)
        {
            if (!_history.TryGetValue(key, out List<double>? history))
                _history[key] = history = [];
            if (history.Count == HistoryLength)
                history.RemoveAt(0);
            history.Add(value);
        }
        _sampled?.Invoke();
    }

    private static List<DriveUsage> ReadDrives(UsageSample usage)
    {
        var drives = new List<DriveUsage>();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    continue;
                string name = drive.Name.TrimEnd('\\');
                drives.Add(new DriveUsage(name, (ulong)drive.TotalFreeSpace, (ulong)drive.TotalSize,
                    usage.DriveBusyPercent.GetValueOrDefault(name)));
            }
            catch (IOException)
            {
                // Became unavailable meanwhile.
            }
        }
        return drives;
    }

    // Rates from the byte counters' growth since the last sample; a new adapter has none until the next one.
    private List<AdapterUsage> ReadAdapters(double seconds)
    {
        var adapters = new List<AdapterUsage>();
        var bytes = new Dictionary<string, (long Received, long Sent)>();
        try
        {
            foreach (NetworkInterface adapter in _adapters ??= ListAdapters())
            {
                IPInterfaceStatistics statistics = adapter.GetIPStatistics();
                (long received, long sent) = bytes[adapter.Id] = (statistics.BytesReceived, statistics.BytesSent);
                if (_lastBytes.TryGetValue(adapter.Id, out var last))
                {
                    // Counters start over when an adapter is reset.
                    adapters.Add(new AdapterUsage(adapter.Id, adapter.Name,
                        Math.Max(0, received - last.Received) / seconds, Math.Max(0, sent - last.Sent) / seconds));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Adapters changing under us; the next sample lists them and counts again.
            _adapters = null;
        }
        _lastBytes.Clear();
        foreach ((string id, var counts) in bytes)
            _lastBytes[id] = counts;
        return adapters;
    }

    private static NetworkInterface[] ListAdapters() =>
        // Each adapter's filter drivers (QoS, WFP) are listed as adapters of their own with the same byte counts, but
        // without IP.
        [.. NetworkInterface.GetAllNetworkInterfaces().Where(adapter =>
            adapter.OperationalStatus == OperationalStatus.Up
            && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            && (adapter.Supports(NetworkInterfaceComponent.IPv4) || adapter.Supports(NetworkInterfaceComponent.IPv6)))];

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => _adapters = null;
}
