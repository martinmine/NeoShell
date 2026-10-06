using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>
/// A readout of the machine and the session: host, user, processor, memory, network address, how long Windows and
/// NeoShell have been up, and how many processes run.
/// </summary>
internal sealed partial class SystemWidget : WidgetView
{
    // Counting processes opens every one of them: not every second.
    private const int ProcessesEveryTicks = 5;

    private static readonly DateTime s_shellStarted = Process.GetCurrentProcess().StartTime;

    private readonly DispatcherQueueTimer _timer;
    private readonly TextBlock _uptime;
    private readonly TextBlock _shell;
    private readonly TextBlock _processes;
    private readonly TextBlock _address;
    private readonly string _runMode;
    private int _ticks;

    public SystemWidget(WidgetSettings settings, RunMode runMode)
    {
        Settings = settings;
        InitializeComponent();
        _runMode = runMode == RunMode.Shell ? "shell" : "beside Explorer";

        AddRow("HOST", Environment.MachineName);
        AddRow("USER", $@"{Environment.UserDomainName}\{Environment.UserName}");
        AddRow("CPU", $"{ProcessorName()} ×{Environment.ProcessorCount}");
        AddRow("MEMORY", $"{GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024):0.#} GB");
        _address = AddRow("ADDRESS", "");
        _uptime = AddRow("UPTIME", "");
        _shell = AddRow("SHELL", "");
        _processes = AddRow("PROC", "");

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        Update();
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        ShowAddress();
    }

    public override void Close()
    {
        _timer.Stop();
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
    }

    private TextBlock AddRow(string label, string value)
    {
        int row = Rows.RowDefinitions.Count;
        Rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelText = new TextBlock { Text = label, Style = (Style)Resources["LabelStyle"] };
        var valueText = new TextBlock { Text = value, Style = (Style)Resources["ValueStyle"] };
        AutomationProperties.SetAutomationId(valueText, "System" + label);
        Grid.SetRow(labelText, row);
        Grid.SetRow(valueText, row);
        Grid.SetColumn(valueText, 1);
        Rows.Children.Add(labelText);
        Rows.Children.Add(valueText);
        return valueText;
    }

    private void Update()
    {
        _uptime.Text = Format(TimeSpan.FromMilliseconds(Environment.TickCount64));
        _shell.Text = $"{Format(DateTime.Now - s_shellStarted)} · {_runMode}";
        if (_ticks++ % ProcessesEveryTicks == 0)
            CountProcessesAsync();
    }

    private async void CountProcessesAsync()
    {
        int count = await Task.Run(() =>
        {
            Process[] processes = Process.GetProcesses();
            foreach (Process process in processes)
                process.Dispose();
            return processes.Length;
        });
        _processes.Text = count.ToString("N0", CultureInfo.CurrentCulture);
    }

    // On a thread of the network stack.
    private void OnAddressChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ShowAddress);

    private void ShowAddress() => _address.Text = Address() ?? "offline";

    /// <summary>The IPv4 address of the first working adapter with a gateway, and the adapter's name.</summary>
    private static string? Address()
    {
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                IPInterfaceProperties properties = adapter.GetIPProperties();
                if (properties.GatewayAddresses.Count == 0)
                    continue;
                UnicastIPAddressInformation? address = properties.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (address is not null)
                    return $"{address.Address} ({adapter.Name})";
            }
        }
        catch (NetworkInformationException)
        {
        }
        return null;
    }

    private static string ProcessorName()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Processor";
    }

    private static string Format(TimeSpan span) =>
        span.Days > 0
            ? $"{span.Days}d {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
}
