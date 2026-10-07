using System.Buffers.Binary;
using Microsoft.Win32;
using NeoShell.Interop.Shell;

namespace NeoShell.Interop.Tray;

/// <summary>A tray icon's key in <see cref="NotifyIconSettings"/>.</summary>
/// <param name="ExecutablePath">
/// As stored: starting with a known folder's GUID where it can (see <see cref="NotifyIconSettings.StoredPath"/>).
/// </param>
public sealed record NotifyIconKey(ulong Id, string ExecutablePath, uint? Uid, Guid? IconGuid, bool IsPromoted);

/// <summary>
/// Explorer's record of every tray icon it has seen: <c>HKCU\Control Panel\NotifyIconSettings\&lt;ID&gt;</c>, one key
/// per icon named by a random 64-bit number, holding the icon's executable (<c>ExecutablePath</c>) and its <c>UID</c>
/// or <c>IconGuid</c>, and whether it shows on the taskbar (<c>IsPromoted</c>; otherwise it's behind the chevron).
/// The root's <c>UIOrderList</c> orders all of them, those on the taskbar and those in the overflow alike. Explorer
/// sends an icon's balloons as toasts of the app <c>NotifyIconGeneratedAumid_&lt;ID&gt;</c>, and Settings lists the
/// keys under "Other system tray icons". As the shell, NeoShell adds and maintains them as Explorer does
/// (Taskbar.dll's <c>NotifyIconSettingsDatabase</c>).
/// </summary>
public static class NotifyIconSettings
{
    public const string KeyPath = @"Control Panel\NotifyIconSettings";

    /// <summary>Where the "Hidden icon menu" setting lives (see <see cref="ChevronVisible"/>).</summary>
    public const string TrayNotifyPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotify";

    // The layout version Explorer writes, and below which it throws the whole key away.
    private const int Version = 3;

    // The folders Explorer's ExecutablePath starts with a GUID for; a program elsewhere (under AppData, say) keeps its
    // full path.
    private static readonly Guid[] s_pathFolders =
    [
        new("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7"), // System
        new("D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27"), // SystemX86
        new("F38BF404-1D43-42F2-9305-67DE0B28FC23"), // Windows
        new("6D809377-6AF0-444B-8957-A3773F02200E"), // ProgramFilesX64
        new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E"), // ProgramFilesX86
    ];

    /// <summary>
    /// Whether the chevron and the hidden icons behind it are there at all: Settings' "Hidden icon menu"
    /// (<c>SystemTrayChevronVisibility</c>, on unless set to 0). Off, icons that aren't promoted don't show anywhere.
    /// </summary>
    public static bool ChevronVisible
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(TrayNotifyPath);
            return key?.GetValue("SystemTrayChevronVisibility") is not int value || value != 0;
        }
        set
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(TrayNotifyPath);
            key.SetValue("SystemTrayChevronVisibility", value ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    /// <summary>
    /// Creates the root key, as Explorer leaves it, and the chevron setting's key, if missing, so they can be watched.
    /// </summary>
    public static void CreateIfMissing()
    {
        using RegistryKey root = OpenRoot();
        using RegistryKey trayNotify = Registry.CurrentUser.CreateSubKey(TrayNotifyPath);
    }

    /// <summary>Every icon's key.</summary>
    public static IReadOnlyList<NotifyIconKey> ReadAll()
    {
        using RegistryKey? root = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (root is null)
            return [];

        var keys = new List<NotifyIconKey>();
        foreach (string name in root.GetSubKeyNames())
        {
            using RegistryKey? key = root.OpenSubKey(name);
            if (!ulong.TryParse(name, out ulong id) || key?.GetValue("ExecutablePath") is not string path)
                continue;
            Guid? iconGuid = key.GetValue("IconGuid") is string text && Guid.TryParse(text, out Guid parsed) ? parsed : null;
            uint? uid = key.GetValue("UID") is int value ? (uint)value : null;
            keys.Add(new NotifyIconKey(id, path, uid, iconGuid, key.GetValue("IsPromoted") is int promoted && promoted != 0));
        }
        return keys;
    }

    /// <summary>The icons' order, left to right on the taskbar and in the overflow.</summary>
    public static IReadOnlyList<ulong> ReadOrder()
    {
        using RegistryKey? root = Registry.CurrentUser.OpenSubKey(KeyPath);
        return root?.GetValue("UIOrderList") is byte[] bytes ? ParseOrder(bytes) : [];
    }

    public static void WriteOrder(IReadOnlyList<ulong> order)
    {
        using RegistryKey root = OpenRoot();
        root.SetValue("UIOrderList", FormatOrder(order), RegistryValueKind.Binary);
    }

    /// <summary>
    /// Adds a key for an icon seen for the first time, as Explorer does: a new random ID, the executable, and the
    /// GUID, or the ID and the tooltip it first had. Not promoted: a new icon starts in the overflow. Put first in the
    /// order. Returns the new key's ID.
    /// </summary>
    public static ulong Add(string executablePath, uint id, Guid? guid, string tip, IReadOnlyList<ulong> order)
    {
        using RegistryKey root = OpenRoot();
        Span<byte> random = stackalloc byte[8];
        Random.Shared.NextBytes(random);
        ulong newId = BinaryPrimitives.ReadUInt64LittleEndian(random);

        using (RegistryKey key = root.CreateSubKey(newId.ToString()))
        {
            if (guid is { } g)
            {
                key.SetValue("IconGuid", g.ToString("B").ToUpperInvariant());
                key.SetValue("ExecutablePath", StoredPath(executablePath));
            }
            else
            {
                key.SetValue("UID", unchecked((int)id), RegistryValueKind.DWord);
                key.SetValue("ExecutablePath", StoredPath(executablePath));
                key.SetValue("InitialTooltip", tip);
            }
        }
        root.SetValue("UIOrderList", FormatOrder([newId, .. order.Where(other => other != newId)]), RegistryValueKind.Binary);
        return newId;
    }

    /// <summary>Shows an icon on the taskbar, or puts it behind the chevron.</summary>
    public static void SetPromoted(ulong id, bool promoted)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{KeyPath}\{id}", writable: true);
        key?.SetValue("IsPromoted", promoted ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>The key of the icon, among <paramref name="keys"/>, or null when it has none yet.</summary>
    public static NotifyIconKey? Find(IReadOnlyList<NotifyIconKey> keys, string executablePath, uint id, Guid? guid) =>
        keys.FirstOrDefault(key => Matches(key.ExecutablePath, key.Uid, key.IconGuid, executablePath, id, guid, JumpLists.KnownFolderPath));

    /// <summary>
    /// Whether a key is the icon's: the same executable, and the same GUID for an icon that has one, else the same ID.
    /// </summary>
    internal static bool Matches(
        string storedPath, uint? storedId, Guid? storedGuid, string executablePath, uint id, Guid? guid, Func<Guid, string?> knownFolderPath)
    {
        if (guid is { } g ? storedGuid != g : storedGuid is not null || storedId != id)
            return false;
        return string.Equals(ExpandKnownFolder(storedPath, knownFolderPath), executablePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An executable's path as Explorer stores it: "{6D809377-…}\VideoLAN\VLC\vlc.exe" under Program Files.</summary>
    public static string StoredPath(string path) => StoredPath(path, s_pathFolders.Select(folder => (folder, JumpLists.KnownFolderPath(folder))));

    // The longest folder that holds the file wins: System (C:\Windows\System32) before Windows.
    internal static string StoredPath(string path, IEnumerable<(Guid Folder, string? Path)> folders) => JumpLists.ImplicitAppId(path, folders);

    internal static IReadOnlyList<ulong> ParseOrder(byte[] bytes)
    {
        var order = new ulong[bytes.Length / 8];
        for (int i = 0; i < order.Length; i++)
            order[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(i * 8));
        return order;
    }

    internal static byte[] FormatOrder(IReadOnlyList<ulong> order)
    {
        byte[] bytes = new byte[order.Count * 8];
        for (int i = 0; i < order.Count; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i * 8), order[i]);
        return bytes;
    }

    // Created as Explorer's ValidateRegistry leaves it, so Explorer keeps what NeoShell wrote.
    private static RegistryKey OpenRoot()
    {
        RegistryKey root = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (root.GetValue("Version") is not int version || version < Version)
            root.SetValue("Version", Version, RegistryValueKind.DWord);
        return root;
    }

    // "{6D809377-…}\VideoLAN\VLC\vlc.exe" is under Program Files.
    private static string ExpandKnownFolder(string path, Func<Guid, string?> knownFolderPath)
    {
        int end = path.IndexOf("}\\", StringComparison.Ordinal);
        if (!path.StartsWith('{') || end < 0 || !Guid.TryParse(path[..(end + 1)], out Guid folder) || knownFolderPath(folder) is not { } folderPath)
            return path;
        return Path.Combine(folderPath, path[(end + 2)..]);
    }
}
