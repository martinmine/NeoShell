using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace NeoShell.Interop.Network;

/// <summary>A Wi-Fi network in range, as the Wi-Fi page lists it: one entry per name, at its strongest.</summary>
/// <param name="SignalBars">0 to 4, as Windows draws them.</param>
public sealed record WifiNetwork(string Ssid, int SignalBars, bool IsSecured, bool IsConnected)
{
    internal WiFiAvailableNetwork? Available { get; init; }
}

public enum WifiConnectResult { Connected, NeedsPassword, Failed }

/// <summary>The PC's Wi-Fi adapter: networks in range, connecting and disconnecting (WinRT <c>WiFiAdapter</c>).</summary>
/// <remarks>Windows only reveals network names to apps allowed to use the location ("Let desktop apps access your location").</remarks>
public sealed class WifiNetworks
{
    private readonly WiFiAdapter _adapter;

    private WifiNetworks(WiFiAdapter adapter) => _adapter = adapter;

    /// <summary>The first Wi-Fi adapter; null when there's none or Windows denies access to it.</summary>
    public static async Task<WifiNetworks?> FindAsync()
    {
        if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed)
            return null;

        IReadOnlyList<WiFiAdapter> adapters = await WiFiAdapter.FindAllAdaptersAsync();
        return adapters.Count > 0 ? new WifiNetworks(adapters[0]) : null;
    }

    /// <summary>Scans (a few seconds) and returns the networks in range, the connected one first.</summary>
    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync()
    {
        await _adapter.ScanAsync();
        string? connected = await ConnectedSsidAsync();
        IEnumerable<WifiNetwork> seen = _adapter.NetworkReport.AvailableNetworks.Select(network => new WifiNetwork(
            network.Ssid,
            Math.Clamp((int)network.SignalBars, 0, 4),
            network.SecuritySettings.NetworkAuthenticationType is not (NetworkAuthenticationType.Open80211 or NetworkAuthenticationType.None),
            IsConnected: false) { Available = network });
        return Arrange(seen, connected);
    }

    /// <summary>
    /// Connects with the saved profile, or <paramref name="password"/> when given. A secured network Windows has no
    /// key for answers <see cref="WifiConnectResult.NeedsPassword"/>, as does a wrong key.
    /// </summary>
    public async Task<WifiConnectResult> ConnectAsync(WifiNetwork network, bool automatically, string? password)
    {
        if (network.Available is not { } available)
            return WifiConnectResult.Failed;

        WiFiReconnectionKind reconnection = automatically ? WiFiReconnectionKind.Automatic : WiFiReconnectionKind.Manual;
        WiFiConnectionResult result = password is null
            ? await _adapter.ConnectAsync(available, reconnection)
            : await _adapter.ConnectAsync(available, reconnection, new PasswordCredential { Password = password });
        return result.ConnectionStatus switch
        {
            WiFiConnectionStatus.Success => WifiConnectResult.Connected,
            WiFiConnectionStatus.InvalidCredential when network.IsSecured => WifiConnectResult.NeedsPassword,
            _ => WifiConnectResult.Failed,
        };
    }

    public void Disconnect() => _adapter.Disconnect();

    /// <summary>
    /// One entry per network name (access points of one network share it), at its strongest signal; the connected
    /// network first, then by signal and name. Hidden networks (no name) are left out.
    /// </summary>
    internal static IReadOnlyList<WifiNetwork> Arrange(IEnumerable<WifiNetwork> seen, string? connectedSsid) =>
    [
        .. seen
            .Where(network => !string.IsNullOrEmpty(network.Ssid))
            .GroupBy(network => network.Ssid)
            .Select(group => group.MaxBy(network => network.SignalBars)! with { IsConnected = group.Key == connectedSsid })
            .OrderByDescending(network => network.IsConnected)
            .ThenByDescending(network => network.SignalBars)
            .ThenBy(network => network.Ssid, StringComparer.CurrentCultureIgnoreCase),
    ];

    private async Task<string?> ConnectedSsidAsync()
    {
        ConnectionProfile? profile = await _adapter.NetworkAdapter.GetConnectedProfileAsync();
        return profile is { IsWlanConnectionProfile: true } ? profile.WlanConnectionProfileDetails.GetConnectedSsid() : null;
    }
}
