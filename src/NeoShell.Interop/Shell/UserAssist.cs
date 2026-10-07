using System.Runtime.InteropServices;
using Microsoft.Win32;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>When an app was last started, how often, and how long it has had the focus.</summary>
/// <param name="Id">An AppUserModelID, or an executable's path that may start with a known folder, as in the AppsFolder.</param>
public sealed record AppUsage(string Id, DateTime LastRun, int Runs = 0, TimeSpan FocusTime = default);

/// <summary>
/// The launch history Windows keeps for Start: every app started through the shell with usage logging (Explorer's
/// Start, taskbar and desktop, and NeoShell's <see cref="ShellLaunch"/> and <see cref="RecordLaunch"/>) has a value here.
/// </summary>
public static class UserAssist
{
    // The key for executables and AppUserModelIDs; {F4E57C4B-…} beside it counts the shortcuts that started them.
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count";

    // The value is 72 bytes: session, run count, focus count, focus time in ms, ..., last run as a FILETIME at 60.
    private const int RunsOffset = 4;
    private const int FocusTimeOffset = 12;
    private const int LastRunOffset = 60;

    private static readonly Guid CLSID_UserAssist = new("dd313e04-feff-11d1-8ecd-0000f87a470c");
    // The group of the key above.
    private static readonly Guid UAIID_Apps = new("cebff5cd-ace2-4f4f-9178-9926f41749ea");
    private const int UAE_LAUNCH = 0;

    /// <summary>
    /// Records a start of the packaged app, as Explorer does when its Start or taskbar starts one: the activation
    /// manager records nothing itself. Shell32 writes the value (counts, last run, the session's totals), and
    /// leaves it alone when the user turned off "Let Windows track app launches". Throws on failure.
    /// </summary>
    public static void RecordLaunch(string appUserModelId)
    {
        var userAssist = Ole32.Create<IUserAssist>(CLSID_UserAssist, Ole32.CLSCTX_INPROC_SERVER);
        Marshal.ThrowExceptionForHR(userAssist.FireEvent(UAIID_Apps, UAE_LAUNCH, appUserModelId, 0));
    }

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
        return fileTime > 0
            ? new AppUsage(Rot13(valueName), DateTime.FromFileTimeUtc(fileTime), BitConverter.ToInt32(data, RunsOffset),
                TimeSpan.FromMilliseconds(BitConverter.ToUInt32(data, FocusTimeOffset)))
            : null;
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
