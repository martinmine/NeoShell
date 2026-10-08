using System.Buffers.Binary;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>
/// The Snipping Tool settings NeoShell's snips follow: the snipping mode last used and "Automatically save
/// screenshots". The app keeps them in its local settings (ApplicationData), a registry hive in its package's
/// settings.dat that a process of the same user can load with <c>RegLoadAppKey</c>, also while Snipping Tool runs.
/// </summary>
/// <remarks>
/// Each value has a type of its own, 0x5F5E100 plus the WinRT <c>PropertyType</c> (0x5F5E104 an Int32, 0x5F5E10B a
/// Boolean), and its data is the value followed by the time it was written (a FILETIME).
/// </remarks>
public static class SnippingToolSettings
{
    public const string AppId = "Microsoft.ScreenSketch_8wekyb3d8bbwe!App";

    private const uint Int32Type = 0x5F5E104;
    private const uint BooleanType = 0x5F5E10B;

    private static string PackageFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Packages\Microsoft.ScreenSketch_8wekyb3d8bbwe");

    private static string Hive => Path.Combine(PackageFolder, @"Settings\settings.dat");

    /// <summary>
    /// Where Snipping Tool keeps snips it doesn't save (and clears them out), or a temporary folder of NeoShell's
    /// without Snipping Tool.
    /// </summary>
    public static string TemporaryFolder() => Directory.Exists(PackageFolder)
        ? Path.Combine(PackageFolder, @"TempState\Snips")
        : Path.Combine(Path.GetTempPath(), "NeoShell Snips");

    /// <summary>
    /// The snipping mode of the last snip (Snipping Tool's own numbers: 1 rectangle, 2 window, 4 freeform; full screen
    /// snips aren't remembered), or null when Snipping Tool has none or isn't installed.
    /// </summary>
    public static int? ReadSnippingMode() => Read(key => ParseInt32(ReadValue(key, "SnippingMode")));

    /// <summary>"Automatically save screenshots", on unless turned off in Snipping Tool's settings.</summary>
    public static bool ReadAutoSave() => Read(key => ParseBoolean(ReadValue(key, "AutoSaveCaptures"))) ?? true;

    /// <summary>Remembers the snipping mode, as Snipping Tool does after each snip; failures are ignored.</summary>
    public static unsafe void SaveSnippingMode(int mode)
    {
        if (!File.Exists(Hive) || Advapi32.RegLoadAppKey(Hive, out nint root, Advapi32.KEY_READ | Advapi32.KEY_SET_VALUE, 0, 0) != 0)
            return;
        using var rootKey = RegistryKey.FromHandle(new SafeRegistryHandle(root, ownsHandle: true));
        using RegistryKey? local = rootKey.OpenSubKey("LocalState", writable: true);
        if (local is null)
            return;
        byte[] data = Int32Value(mode, DateTime.UtcNow);
        fixed (byte* bytes = data)
        {
            foreach (string name in (string[])["SnippingMode", "ProtocolSnippingMode"])
                Advapi32.RegSetValueEx(local.Handle.DangerousGetHandle(), name, 0, Int32Type, bytes, (uint)data.Length);
        }
    }

    internal static int? ParseInt32(byte[]? data) => data is { Length: >= 4 } ? BinaryPrimitives.ReadInt32LittleEndian(data) : null;

    internal static bool? ParseBoolean(byte[]? data) => data is { Length: >= 1 } ? data[0] != 0 : null;

    internal static byte[] Int32Value(int value, DateTime writtenUtc)
    {
        var data = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(data, value);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(4), writtenUtc.ToFileTimeUtc());
        return data;
    }

    // RegistryKey.GetValue gives nothing for the values' own types.
    private static unsafe byte[]? ReadValue(RegistryKey key, string name)
    {
        var data = new byte[64];
        uint size = (uint)data.Length;
        fixed (byte* bytes = data)
        {
            if (Advapi32.RegQueryValueEx(key.Handle.DangerousGetHandle(), name, 0, out _, bytes, ref size) != 0)
                return null;
        }
        return data[..(int)size];
    }

    private static T? Read<T>(Func<RegistryKey, T?> read) where T : struct
    {
        if (!File.Exists(Hive) || Advapi32.RegLoadAppKey(Hive, out nint root, Advapi32.KEY_READ, 0, 0) != 0)
            return null;
        using var rootKey = RegistryKey.FromHandle(new SafeRegistryHandle(root, ownsHandle: true));
        using RegistryKey? local = rootKey.OpenSubKey("LocalState");
        return local is null ? null : read(local);
    }
}
