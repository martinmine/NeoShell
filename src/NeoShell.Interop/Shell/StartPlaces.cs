using Microsoft.Win32;

namespace NeoShell.Interop.Shell;

/// <summary>A folder Start can show beside the power button (Settings → Personalization → Start → Folders).</summary>
public enum StartPlace { Documents, Downloads, Music, Pictures, Videos, Network, PersonalFolder, FileExplorer, Settings }

/// <summary>The folders chosen for Start, as Explorer keeps them.</summary>
public static class StartPlaces
{
    private const string StartKey = @"Software\Microsoft\Windows\CurrentVersion\Start";
    private const string PolicyKey = @"SOFTWARE\Microsoft\PolicyManager\current\device\Start";

    // Settings writes the chosen folders' ids to VisiblePlaces, one 16-byte GUID after another, in the order they
    // were switched on. Start shows them in its own order whatever the order here.
    private static readonly Dictionary<Guid, StartPlace> s_ids = new()
    {
        [new Guid("2D34D5CE-FA5A-4543-82F2-22E6EAF7773C")] = StartPlace.Documents,
        [new Guid("E367B32F-89DE-4355-BFCE-61F37B18A937")] = StartPlace.Downloads,
        [new Guid("B00B0620-7F51-4C32-AA1E-34CC547F7315")] = StartPlace.Music,
        [new Guid("383F07A0-E80A-4C80-B05A-86DB845DBC4D")] = StartPlace.Pictures,
        [new Guid("42B3A5C5-7D86-42F4-80A4-93FACA7A88B5")] = StartPlace.Videos,
        [new Guid("FE758144-080D-42AE-8BDA-34ED97B66394")] = StartPlace.Network,
        [new Guid("74BDB04A-F94A-4F68-8BD6-4398071DA8BC")] = StartPlace.PersonalFolder,
        [new Guid("148A24BC-D60C-4289-A080-6ED9BBA24882")] = StartPlace.FileExplorer,
        [new Guid("52730886-51AA-4243-9F7B-2776584659D4")] = StartPlace.Settings,
    };

    /// <summary>
    /// The folders to show, in Start's order: those chosen in Settings, unless the <c>AllowPinnedFolder…</c> policy of
    /// a folder forces it on or off.
    /// </summary>
    public static IReadOnlyList<StartPlace> Load()
    {
        using RegistryKey? start = Registry.CurrentUser.OpenSubKey(StartKey);
        using RegistryKey? policy = Registry.LocalMachine.OpenSubKey(PolicyKey);
        IReadOnlySet<StartPlace> chosen = Parse(start?.GetValue("VisiblePlaces") as byte[] ?? []);
        return Visible(chosen, place => policy?.GetValue($"AllowPinnedFolder{place}") as int?);
    }

    internal static IReadOnlySet<StartPlace> Parse(byte[] value)
    {
        var places = new HashSet<StartPlace>();
        for (int i = 0; i + 16 <= value.Length; i += 16)
        {
            if (s_ids.TryGetValue(new Guid(value.AsSpan(i, 16)), out StartPlace place))
                places.Add(place);
        }
        return places;
    }

    /// <summary>Chosen folders in Start's order, with each policy (0 hidden, 1 shown, anything else not set) applied.</summary>
    internal static IReadOnlyList<StartPlace> Visible(IReadOnlySet<StartPlace> chosen, Func<StartPlace, int?> policy) =>
        [.. Enum.GetValues<StartPlace>().Where(place => policy(place) switch
        {
            0 => false,
            1 => true,
            _ => chosen.Contains(place),
        })];
}
