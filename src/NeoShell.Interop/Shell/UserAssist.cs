using Microsoft.Win32;

namespace NeoShell.Interop.Shell;

/// <summary>When an app was last started.</summary>
/// <param name="Id">An AppUserModelID, or an executable's path that may start with a known folder, as in the AppsFolder.</param>
public sealed record AppUsage(string Id, DateTime LastRun);

/// <summary>
/// The launch history Windows keeps for Start: every app started through the shell with usage logging (Explorer's
/// Start, taskbar and desktop, and NeoShell's <see cref="ShellLaunch"/>) has a value here.
/// </summary>
public static class UserAssist
{
    // The key for executables and AppUserModelIDs; {F4E57C4B-…} beside it counts the shortcuts that started them.
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count";

    // The value is 72 bytes: session, run count, focus count, focus time, ..., last run as a FILETIME at 60.
    private const int LastRunOffset = 60;

    /// <summary>Every app with a recorded start, most recent first.</summary>
    public static IReadOnlyList<AppUsage> Load()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key is null)
            return [];

        return [.. key.GetValueNames()
            .Select(name => Parse(name, key.GetValue(name) as byte[]))
            .OfType<AppUsage>()
            .OrderByDescending(usage => usage.LastRun)];
    }

    /// <summary>A value as an app's last start; null for bookkeeping values and apps never started.</summary>
    internal static AppUsage? Parse(string valueName, byte[]? data)
    {
        if (data is null || data.Length < LastRunOffset + sizeof(long))
            return null;

        long fileTime = BitConverter.ToInt64(data, LastRunOffset);
        // Explorer adds entries it only ever showed, with no start.
        return fileTime > 0 ? new AppUsage(Rot13(valueName), DateTime.FromFileTimeUtc(fileTime)) : null;
    }

    /// <summary>Value names are ROT13-encoded.</summary>
    internal static string Rot13(string text) => string.Create(text.Length, text, (chars, source) =>
    {
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            chars[i] = c switch
            {
                >= 'a' and <= 'z' => (char)('a' + (c - 'a' + 13) % 26),
                >= 'A' and <= 'Z' => (char)('A' + (c - 'A' + 13) % 26),
                _ => c,
            };
        }
    });
}
