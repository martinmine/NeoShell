using System.Diagnostics;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Network;

/// <summary>A VPN connection set up in Windows (Settings → VPN), from the user's or all users' phonebook.</summary>
public sealed record VpnConnection(string Name, string Phonebook, bool IsConnected);

/// <summary>
/// Windows' built-in VPN connections: listed from the remote access phonebooks, connected through Windows' own dial
/// dialog (which asks for what isn't saved) and hung up through the remote access API.
/// </summary>
public static unsafe class VpnConnections
{
    // RASET_Vpn: the phonebooks also hold dial-up and broadband (PPPoE) entries.
    private const string VpnType = "2";

    /// <summary>The VPN connections, the user's first, each as it's named in Settings.</summary>
    public static IReadOnlyList<VpnConnection> List()
    {
        HashSet<string> connected = [.. Connections().Select(connection => connection.Name)];
        var list = new List<VpnConnection>();
        foreach (string phonebook in (string[])
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Network\Connections\Pbk\rasphone.pbk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Network\Connections\Pbk\rasphone.pbk"),
            ])
        {
            if (!File.Exists(phonebook))
                continue;
            foreach (string name in VpnEntries(File.ReadAllText(phonebook)))
                list.Add(new VpnConnection(name, phonebook, connected.Contains(name)));
        }
        return list;
    }

    /// <summary>Opens Windows' dialog that connects it, asking for the user name and password when they aren't saved.</summary>
    public static void Connect(VpnConnection connection) =>
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "rasphone.exe"))
        {
            ArgumentList = { "-f", connection.Phonebook, "-d", connection.Name },
            UseShellExecute = false,
        })?.Dispose();

    /// <summary>Hangs it up; false when it wasn't connected.</summary>
    public static bool Disconnect(VpnConnection connection)
    {
        foreach ((string name, nint handle) in Connections())
        {
            if (name == connection.Name)
                return Rasapi32.RasHangUp(handle) == 0;
        }
        return false;
    }

    /// <summary>The names of a phonebook's VPN entries: its <c>[Name]</c> sections with <c>Type=2</c>.</summary>
    internal static IEnumerable<string> VpnEntries(string phonebook)
    {
        string? entry = null;
        foreach (string raw in phonebook.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
                entry = line[1..^1];
            else if (entry is not null && line.StartsWith("Type=", StringComparison.OrdinalIgnoreCase) && line[5..].Trim() == VpnType)
                yield return entry;
        }
    }

    // The active connections (being dialled or connected) by entry name.
    private static List<(string Name, nint Handle)> Connections()
    {
        uint size = (uint)sizeof(Rasapi32.RASCONN);
        Rasapi32.RASCONN[] buffer = new Rasapi32.RASCONN[1];
        while (true)
        {
            buffer[0].dwSize = (uint)sizeof(Rasapi32.RASCONN);
            uint result;
            uint count;
            fixed (Rasapi32.RASCONN* connections = buffer)
                result = Rasapi32.RasEnumConnections(connections, ref size, out count);
            if (result == Rasapi32.ERROR_BUFFER_TOO_SMALL)
            {
                buffer = new Rasapi32.RASCONN[size / sizeof(Rasapi32.RASCONN) + 1];
                continue;
            }

            var list = new List<(string, nint)>();
            for (int i = 0; result == 0 && i < count; i++)
            {
                fixed (char* name = buffer[i].szEntryName)
                    list.Add((new string(name), buffer[i].hrasconn));
            }
            return list;
        }
    }
}
