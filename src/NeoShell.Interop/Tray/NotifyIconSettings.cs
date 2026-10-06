using Microsoft.Win32;
using NeoShell.Interop.Shell;

namespace NeoShell.Interop.Tray;

/// <summary>
/// Explorer's record of every tray icon it has seen: <c>HKCU\Control Panel\NotifyIconSettings\&lt;ID&gt;</c>, one key
/// per icon named by a random 64-bit number, holding the icon's executable (<c>ExecutablePath</c>, starting with a
/// known folder's GUID where it can) and its <c>UID</c> or <c>IconGuid</c>. Explorer sends an icon's balloons as
/// toasts of the app <c>NotifyIconGeneratedAumid_&lt;ID&gt;</c>, so that is where the user's notification settings
/// for them live. Read only: Explorer adds and maintains the keys.
/// </summary>
public static class NotifyIconSettings
{
    private const string KeyPath = @"Control Panel\NotifyIconSettings";

    /// <summary>The ID of the icon's key, or null when Explorer has never seen the icon.</summary>
    public static string? FindId(string executablePath, uint id, Guid? guid)
    {
        using RegistryKey? root = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (root is null)
            return null;

        foreach (string name in root.GetSubKeyNames())
        {
            using RegistryKey? key = root.OpenSubKey(name);
            if (key?.GetValue("ExecutablePath") is not string path)
                continue;
            Guid? iconGuid = key.GetValue("IconGuid") is string text && Guid.TryParse(text, out Guid parsed) ? parsed : null;
            uint? uid = key.GetValue("UID") is int value ? (uint)value : null;
            if (Matches(path, uid, iconGuid, executablePath, id, guid, JumpLists.KnownFolderPath))
                return name;
        }
        return null;
    }

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

    // "{6D809377-…}\VideoLAN\VLC\vlc.exe" is under Program Files.
    private static string ExpandKnownFolder(string path, Func<Guid, string?> knownFolderPath)
    {
        int end = path.IndexOf("}\\", StringComparison.Ordinal);
        if (!path.StartsWith('{') || end < 0 || !Guid.TryParse(path[..(end + 1)], out Guid folder) || knownFolderPath(folder) is not { } folderPath)
            return path;
        return Path.Combine(folderPath, path[(end + 2)..]);
    }
}
