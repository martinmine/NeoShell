using Windows.Networking.Connectivity;

namespace NeoShell.Interop.Network;

public enum NetworkKind { Disconnected, Ethernet, WiFi, Cellular, Other }

/// <summary>The connection the computer reaches the internet through, as the taskbar shows it.</summary>
/// <param name="Name">Wi-Fi network name (SSID) or connection profile name.</param>
/// <param name="SignalBars">Wi-Fi or cellular signal, 0 to 5; 0 for wired connections.</param>
public sealed record NetworkState(NetworkKind Kind, string? Name, bool HasInternet, int SignalBars)
{
    public static readonly NetworkState Disconnected = new(NetworkKind.Disconnected, null, false, 0);
}

/// <summary>Watches the internet connection. <see cref="Changed"/> comes from a thread-pool thread.</summary>
public sealed class NetworkStatus : IDisposable
{
    private const uint IanaEthernet = 6;

    public NetworkStatus()
    {
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
    }

    public event Action? Changed;

    public static NetworkState Read()
    {
        try
        {
            ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
                return NetworkState.Disconnected;

            bool hasInternet = profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            int bars = profile.GetSignalBars() ?? 0;
            if (profile.IsWlanConnectionProfile)
                return new NetworkState(NetworkKind.WiFi, profile.WlanConnectionProfileDetails.GetConnectedSsid(), hasInternet, bars);
            if (profile.IsWwanConnectionProfile)
                return new NetworkState(NetworkKind.Cellular, profile.ProfileName, hasInternet, bars);

            NetworkKind kind = profile.NetworkAdapter?.IanaInterfaceType == IanaEthernet ? NetworkKind.Ethernet : NetworkKind.Other;
            return new NetworkState(kind, profile.ProfileName, hasInternet, 0);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The network stack is changing under us; the next change event reads again.
            return NetworkState.Disconnected;
        }
    }

    public void Dispose() => NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;

    private void OnNetworkStatusChanged(object sender) => Changed?.Invoke();
}
