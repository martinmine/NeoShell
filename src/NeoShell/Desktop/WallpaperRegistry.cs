using System.Text;
using Microsoft.Win32;
using NeoShell.Interop.Imaging;
using Windows.Graphics;

namespace NeoShell.Desktop;

/// <summary>
/// The parts of Explorer's wallpaper state beyond the classic <c>Wallpaper</c> value, in Explorer's own formats, so
/// either shell takes over where the other left off.
/// </summary>
/// <remarks>
/// <c>Control Panel\Desktop\TranscodedImageCache</c> describes the image Explorer last drew; with a picture per monitor
/// (or a slideshow) each monitor has its own <c>TranscodedImageCache_000</c>, <c>_001</c>… and <c>Wallpaper</c> names
/// Explorer's transcoded copy instead of the picture. Each value is 800 bytes: a header (magic, the source's size,
/// width and height, its write time), the source path at 24 (260 characters) and, per monitor, the monitor's
/// device path at 544. Explorer draws its transcoded copies (<c>Themes\Transcoded_000</c>… and
/// <c>TranscodedWallpaper</c>) when the header matches the source, and drops a value whose header doesn't. NeoShell draws
/// the source itself but leaves Explorer what it needs: a copy of the picture (any format Explorer can decode) and a
/// matching header.
/// </remarks>
public static class WallpaperRegistry
{
    public const string DesktopKey = @"Control Panel\Desktop";
    public const int ImageCacheSize = 800;
    private const uint ImageCacheMagic = 0x0001C37A;
    private const int PathOffset = 24;
    private const int MonitorOffset = 544;
    private const int PathChars = 260;
    private const int MonitorChars = (ImageCacheSize - MonitorOffset) / 2;

    /// <summary><c>LastUpdated</c> after a change to every monitor; otherwise the monitor's index.</summary>
    private const int AllMonitors = -1;

    /// <summary>Explorer's transcoded copy of the wallpaper, which <c>Wallpaper</c> names in some states.</summary>
    public static string TranscodedWallpaperPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");

    /// <summary>The source image of an image cache value, and its monitor (empty in the value for all monitors).</summary>
    public static (string Path, string MonitorPath)? ParseImageCache(byte[]? value)
    {
        if (value is null || value.Length < ImageCacheSize || BitConverter.ToUInt32(value, 0) != ImageCacheMagic)
            return null;

        string path = ReadString(value, PathOffset, PathChars);
        return path.Length == 0 ? null : (path, ReadString(value, MonitorOffset, MonitorChars));
    }

    /// <summary>An image cache value; an empty monitor path for the value of all monitors.</summary>
    /// <param name="fileSize">The source file's size, which Explorer checks with its write time.</param>
    public static byte[] BuildImageCache(string path, string monitorPath, long fileSize, SizeInt32 size, DateTime writeTime)
    {
        var value = new byte[ImageCacheSize];
        BitConverter.TryWriteBytes(value, ImageCacheMagic);
        BitConverter.TryWriteBytes(value.AsSpan(4), (uint)fileSize);
        BitConverter.TryWriteBytes(value.AsSpan(8), size.Width);
        BitConverter.TryWriteBytes(value.AsSpan(12), size.Height);
        BitConverter.TryWriteBytes(value.AsSpan(16), writeTime.ToFileTimeUtc());
        WriteString(value, PathOffset, PathChars, path);
        WriteString(value, MonitorOffset, MonitorChars, monitorPath);
        return value;
    }

    /// <summary>The pictures per monitor (by device path), when Explorer or NeoShell set them per monitor.</summary>
    public static Dictionary<string, string> ReadMonitorImages(RegistryKey? desktop)
    {
        var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in MonitorValueNames(desktop))
        {
            if (ParseImageCache(desktop!.GetValue(name) as byte[]) is { MonitorPath.Length: > 0 } entry)
                images.TryAdd(entry.MonitorPath, entry.Path);
        }
        return images;
    }

    /// <summary>The source of <c>Wallpaper</c>: itself, or when it names Explorer's transcoded copy, the copy's source.</summary>
    public static string? ReadImagePath(RegistryKey? desktop)
    {
        string? path = desktop?.GetValue("Wallpaper")?.ToString();
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = Environment.ExpandEnvironmentVariables(path);
        if (string.Equals(path, TranscodedWallpaperPath, StringComparison.OrdinalIgnoreCase)
            && ParseImageCache(desktop!.GetValue("TranscodedImageCache") as byte[]) is { } source
            && File.Exists(source.Path))
        {
            return source.Path;
        }
        return path;
    }

    /// <summary>
    /// Sets a picture for each of these monitors as Explorer does: its per-monitor values and transcoded copies, and
    /// the picture of <paramref name="lastIndex"/> as <c>TranscodedWallpaper</c>, which <c>Wallpaper</c> is then to name
    /// (the caller sets it, through Windows). Reads and copies the pictures: call it off the UI thread.
    /// </summary>
    /// <param name="lastIndex">The index of the monitor changed last (Explorer's <c>LastUpdated</c>).</param>
    public static async Task WriteMonitorImagesAsync(IReadOnlyList<(string MonitorPath, string Path)> images, int lastIndex)
    {
        var values = new List<byte[]>();
        for (int i = 0; i < images.Count; i++)
        {
            values.Add(await CopyForExplorerAsync(images[i].Path, images[i].MonitorPath, $"Transcoded_{i:000}"));
            if (i == lastIndex)
                await CopyForExplorerAsync(images[i].Path, "", Path.GetFileName(TranscodedWallpaperPath));
        }

        using RegistryKey desktop = Registry.CurrentUser.CreateSubKey(DesktopKey);
        DeleteMonitorValues(desktop);
        for (int i = 0; i < values.Count; i++)
            desktop.SetValue($"TranscodedImageCache_{i:000}", values[i], RegistryValueKind.Binary);
        if (lastIndex >= 0 && lastIndex < values.Count)
            desktop.SetValue("TranscodedImageCache", WithoutMonitor(values[lastIndex]), RegistryValueKind.Binary);
        desktop.SetValue("LastUpdated", lastIndex, RegistryValueKind.DWord);
    }

    // Copies the picture over Explorer's transcoded file (replaced in place, as Explorer keeps it) and returns its value.
    private static async Task<byte[]> CopyForExplorerAsync(string path, string monitorPath, string transcodedName)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        SizeInt32 size = await Pictures.GetSizeAsync(bytes);
        string target = Path.Combine(Path.GetDirectoryName(TranscodedWallpaperPath)!, transcodedName);
        using (var file = new FileStream(target, FileMode.OpenOrCreate, FileAccess.Write))
        {
            file.SetLength(0);
            await file.WriteAsync(bytes);
        }
        return BuildImageCache(path, monitorPath, bytes.Length, size, File.GetLastWriteTimeUtc(path));
    }

    private static byte[] WithoutMonitor(byte[] value)
    {
        byte[] copy = (byte[])value.Clone();
        Array.Clear(copy, MonitorOffset, ImageCacheSize - MonitorOffset);
        return copy;
    }

    /// <summary>Back to one picture on every monitor.</summary>
    public static void ClearMonitorImages()
    {
        using RegistryKey desktop = Registry.CurrentUser.CreateSubKey(DesktopKey);
        DeleteMonitorValues(desktop);
        desktop.SetValue("LastUpdated", AllMonitors, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Decodes binary data as Explorer's private profiles (<c>slideshow.ini</c>) store it: Base64 characters, each
    /// worth six bits taken lowest first, filling bytes lowest bit first. Null if a character isn't Base64.
    /// </summary>
    public static byte[]? DecodeProfileBinary(string text)
    {
        var bytes = new byte[text.Length * 6 / 8];
        int bit = 0;
        foreach (char c in text)
        {
            int value = Base64Alphabet.IndexOf(c);
            if (value < 0)
                return null;
            for (int i = 0; i < 6; i++, bit++)
            {
                if (bit / 8 < bytes.Length && (value >> i & 1) != 0)
                    bytes[bit / 8] |= (byte)(1 << (bit % 8));
            }
        }
        return bytes;
    }

    /// <summary>Encodes as <see cref="DecodeProfileBinary"/> reads it.</summary>
    public static string EncodeProfileBinary(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder((bytes.Length * 8 + 5) / 6);
        int bits = bytes.Length * 8;
        for (int start = 0; start < bits; start += 6)
        {
            int value = 0;
            for (int i = 0; i < 6 && start + i < bits; i++)
                value |= (bytes[(start + i) / 8] >> ((start + i) % 8) & 1) << i;
            text.Append(Base64Alphabet[value]);
        }
        return text.ToString();
    }

    private const string Base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    private static IEnumerable<string> MonitorValueNames(RegistryKey? desktop) =>
        desktop?.GetValueNames().Where(name => name.StartsWith("TranscodedImageCache_", StringComparison.OrdinalIgnoreCase)) ?? [];

    private static void DeleteMonitorValues(RegistryKey desktop)
    {
        foreach (string name in MonitorValueNames(desktop).ToList())
            desktop.DeleteValue(name, throwOnMissingValue: false);
    }

    private static string ReadString(byte[] value, int offset, int maxChars)
    {
        string text = Encoding.Unicode.GetString(value, offset, maxChars * 2);
        int end = text.IndexOf('\0');
        return end < 0 ? text : text[..end];
    }

    private static void WriteString(byte[] value, int offset, int maxChars, string text)
    {
        if (text.Length >= maxChars)
            throw new ArgumentException($"Longer than {maxChars - 1} characters", nameof(text));
        Encoding.Unicode.GetBytes(text, 0, text.Length, value, offset);
    }
}
