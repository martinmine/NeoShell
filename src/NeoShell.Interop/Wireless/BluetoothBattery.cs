using Windows.Devices.Enumeration;

namespace NeoShell.Interop.Wireless;

/// <summary>
/// The battery level Windows itself collects from Bluetooth devices (the LE Battery Service and classic Hands-Free
/// reporting), as Settings → Bluetooth &amp; devices shows it.
/// </summary>
internal static class BluetoothBattery
{
    // DEVPKEY_Bluetooth_Battery. Search queries can't filter on it, so Bluetooth device nodes are filtered in code.
    private const string BatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    // Groups the several device nodes of one physical device.
    private const string ContainerIdProperty = "System.Devices.ContainerId";
    private const string BluetoothNodesFilter =
        "System.Devices.DeviceInstanceId:~<\"BTHENUM\\\" OR System.Devices.DeviceInstanceId:~<\"BTHLE\\\"";

    private const string EndpointContainerIdProperty = "System.Devices.Aep.ContainerId";
    private const string EndpointIsConnectedProperty = "System.Devices.Aep.IsConnected";

    // The platform's selectors per protocol take about 10 ms each; a hand-written query for both protocols takes 30 s
    // (it waits for other endpoint providers to time out).
    private static readonly string[] s_pairedEndpointSelectors =
    [
        Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true),
        Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
    ];

    // Arrays, not collection expressions passed as IEnumerable: CsWinRT can't marshal those under Native AOT.
    private static readonly string[] s_nodeProperties = [BatteryProperty, ContainerIdProperty];
    private static readonly string[] s_endpointProperties = [EndpointContainerIdProperty, EndpointIsConnectedProperty];

    public static async Task<IReadOnlyList<WirelessDevice>> ReadAsync(CancellationToken cancel)
    {
        Task<DeviceInformationCollection> nodesTask = DeviceInformation
            .FindAllAsync(BluetoothNodesFilter, s_nodeProperties, DeviceInformationKind.Device)
            .AsTask(cancel);
        Task<HashSet<Guid>> connectedTask = ConnectedContainersAsync(cancel);
        DeviceInformationCollection nodes = await nodesTask;
        HashSet<Guid> connected = await connectedTask;

        var devices = new List<WirelessDevice>();
        foreach (DeviceInformation node in nodes)
        {
            if (node.Properties.GetValueOrDefault(BatteryProperty) is not byte level)
                continue;

            var containerId = node.Properties.GetValueOrDefault(ContainerIdProperty) as Guid?;
            // Windows keeps the last level after a device disconnects: it only counts while connected.
            bool isConnected = containerId is { } id && connected.Contains(id);
            devices.Add(new WirelessDevice($"bt:{containerId?.ToString() ?? node.Id}", node.Name, WirelessDeviceKind.Bluetooth,
                isConnected, isConnected ? level : null, null));
        }
        // A device may have the level on more than one node.
        return [.. devices.DistinctBy(device => device.Id)];
    }

    /// <summary>The last level Windows knows of each Bluetooth device, by container (also after it disconnected).</summary>
    internal static async Task<Dictionary<Guid, int>> LevelsAsync(CancellationToken cancel)
    {
        var levels = new Dictionary<Guid, int>();
        foreach (DeviceInformation node in await DeviceInformation
            .FindAllAsync(BluetoothNodesFilter, s_nodeProperties, DeviceInformationKind.Device)
            .AsTask(cancel))
        {
            if (node.Properties.GetValueOrDefault(BatteryProperty) is byte level
                && node.Properties.GetValueOrDefault(ContainerIdProperty) is Guid containerId)
            {
                levels[containerId] = level;
            }
        }
        return levels;
    }

    /// <summary>
    /// The containers of the paired Bluetooth devices (classic and LE) that are connected. Connection is a property of
    /// the association endpoint, not of the nodes with the battery level; the container id links them.
    /// </summary>
    private static async Task<HashSet<Guid>> ConnectedContainersAsync(CancellationToken cancel)
    {
        DeviceInformationCollection[] results = await Task.WhenAll(s_pairedEndpointSelectors.Select(selector => DeviceInformation
            .FindAllAsync(selector, s_endpointProperties, DeviceInformationKind.AssociationEndpoint)
            .AsTask(cancel)));

        var connected = new HashSet<Guid>();
        foreach (DeviceInformation endpoint in results.SelectMany(result => result))
        {
            if (endpoint.Properties.GetValueOrDefault(EndpointIsConnectedProperty) is true
                && endpoint.Properties.GetValueOrDefault(EndpointContainerIdProperty) is Guid containerId)
            {
                connected.Add(containerId);
            }
        }
        return connected;
    }
}
