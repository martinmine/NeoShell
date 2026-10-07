using System.Text;
using Microsoft.Win32;
using NeoShell.Interop.Shell;

namespace NeoShell.Desktop;

/// <summary>
/// The desktop slideshow as Explorer stores it: the pictures in <c>%APPDATA%\Microsoft\Windows\Themes\slideshow.ini</c>
/// (a folder, or files in one folder, as ID lists; an empty file means no slideshow) and the options in
/// <c>HKCU\Control Panel\Personalization\Desktop Slideshow</c>.
/// </summary>
/// <param name="Sources">The folder, or the files; empty when there is no slideshow.</param>
/// <param name="Interval">Milliseconds between pictures.</param>
/// <param name="LastTick">When the picture last changed, kept for intervals of 5 minutes and more.</param>
/// <param name="AnimationDuration">The crossfade between pictures, in milliseconds.</param>
/// <param name="AlignToMidnight">Changes fall on whole intervals since midnight (UTC) unless <c>Flags</c> bit 2 is set.</param>
public sealed record SlideshowSettings(
    IReadOnlyList<string> Sources, uint Interval, bool Shuffle, DateTime? LastTick, int AnimationDuration, bool AlignToMidnight)
{
    public const string OptionsKey = @"Control Panel\Personalization\Desktop Slideshow";
    public const uint DefaultInterval = 1_800_000;
    public const int DefaultAnimationDuration = 1000;
    public const int MinAnimationDuration = 250;

    /// <summary>Explorer keeps the time of the last change only from this interval up (and ignores it below).</summary>
    public const uint PersistedTickInterval = 300_000;

    private const string Section = "[Slideshow]";
    private const string RootKey = "ImagesRootPIDL";
    private const string RootPathKey = "ImagesRoot";
    private const string ItemKey = "Item";

    public static string IniPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\slideshow.ini");

    public bool IsRunning => Sources.Count > 0;

    public static SlideshowSettings Read()
    {
        using RegistryKey? options = Registry.CurrentUser.OpenSubKey(OptionsKey);
        long? tick = options?.GetValue("LastTickHigh") is int high && options.GetValue("LastTickLow") is int low
            ? ((long)(uint)high << 32) | (uint)low
            : null;
        return new SlideshowSettings(
            ReadSources(),
            options?.GetValue("Interval") is int interval ? (uint)interval : DefaultInterval,
            options?.GetValue("Shuffle") is int shuffle && shuffle != 0,
            tick is > 0 ? DateTime.FromFileTimeUtc(tick.Value) : null,
            ClampAnimationDuration(options?.GetValue("AnimationDuration") as int?),
            options?.GetValue("Flags") is not int flags || (flags & 4) == 0);
    }

    /// <summary>Explorer's rule: unset means 1000 ms, anything shorter than 250 ms means 250.</summary>
    public static int ClampAnimationDuration(int? value) => value switch
    {
        null => DefaultAnimationDuration,
        < MinAnimationDuration => MinAnimationDuration,
        _ => value.Value,
    };

    /// <summary>Starts a slideshow of these folders or files; throws if they aren't one folder or files in one folder.</summary>
    public static void WriteSources(IReadOnlyList<string> paths)
    {
        var ini = new StringBuilder(Section).Append("\r\n");
        if (paths.Count == 1 && Directory.Exists(paths[0]))
        {
            ini.Append(RootKey).Append('=').Append(Encode(IDList(paths[0]))).Append("\r\n");
        }
        else
        {
            string folder = Path.GetDirectoryName(paths[0]) ?? throw new ArgumentException("Not a file", nameof(paths));
            byte[] root = IDList(folder);
            ini.Append(RootKey).Append('=').Append(Encode(root)).Append("\r\n");
            for (int i = 0; i < paths.Count; i++)
            {
                if (!string.Equals(Path.GetDirectoryName(paths[i]), folder, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Not all in one folder", nameof(paths));
                // The item relative to the folder: its ID list after the folder's own items.
                ini.Append(ItemKey).Append(i).Append('=').Append(Encode(IDList(paths[i])[(root.Length - 2)..])).Append("\r\n");
            }
        }
        WriteIni(Encoding.ASCII.GetBytes(ini.ToString()));
    }

    /// <summary>Ends the slideshow, as Explorer does: the file is emptied.</summary>
    public static void ClearSources() => WriteIni([]);

    public static void WriteOptions(bool shuffle, uint interval)
    {
        using RegistryKey options = Registry.CurrentUser.CreateSubKey(OptionsKey);
        options.SetValue("Interval", unchecked((int)interval), RegistryValueKind.DWord);
        options.SetValue("Shuffle", shuffle ? 1 : 0, RegistryValueKind.DWord);
    }

    public static void WriteLastTick(DateTime time)
    {
        long fileTime = time.ToFileTimeUtc();
        using RegistryKey options = Registry.CurrentUser.CreateSubKey(OptionsKey);
        options.SetValue("LastTickHigh", unchecked((int)(fileTime >> 32)), RegistryValueKind.DWord);
        options.SetValue("LastTickLow", unchecked((int)fileTime), RegistryValueKind.DWord);
    }

    /// <summary>
    /// Milliseconds until the next picture, as Explorer schedules it: on a whole interval since midnight (UTC) unless
    /// that's under 4 seconds away (then one interval later); from 5-minute intervals up, an interval after the last
    /// change, or straight away if that has passed (or never happened). <paramref name="now"/> is UTC.
    /// </summary>
    public static uint NextChange(uint interval, DateTime now, DateTime? lastTick, bool alignToMidnight)
    {
        interval = Math.Clamp(interval, 10_000, int.MaxValue);
        uint delay = interval;
        if (interval >= PersistedTickInterval && lastTick < now)
        {
            double elapsed = (now - lastTick.Value).TotalMilliseconds;
            if (elapsed >= interval)
                return 10;
            delay = interval - (uint)elapsed;
        }
        else if (interval >= PersistedTickInterval && lastTick is null)
        {
            return 10; // never changed: Explorer counts from 1601
        }

        if (alignToMidnight)
        {
            uint sinceInterval = (uint)((long)(now - now.Date).TotalMilliseconds % interval);
            // Explorer's arithmetic is unsigned: a delay shorter than the time past the boundary isn't moved.
            if (delay >= sinceInterval)
            {
                delay -= sinceInterval;
                if (delay < 4000)
                    delay += interval;
            }
        }
        return delay == 0 ? 10 : delay;
    }

    private static IReadOnlyList<string> ReadSources()
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(IniPath);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool inSection = false;
        foreach (string line in lines.Select(line => line.Trim()))
        {
            if (line.StartsWith('['))
                inSection = string.Equals(line, Section, StringComparison.OrdinalIgnoreCase);
            else if (inSection && line.IndexOf('=') is var equals and > 0)
                values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }

        byte[]? root = values.TryGetValue(RootKey, out string? encoded) ? Decode(encoded) : null;
        string? folder = root is null ? null : ShellItems.GetPath(root);
        if (folder is null && values.TryGetValue(RootPathKey, out string? rootPath))
        {
            folder = Environment.ExpandEnvironmentVariables(rootPath);
            root = ShellItems.GetIDList(folder);
        }
        if (folder is null || root is null)
            return [];

        var files = new List<string>();
        for (int i = 0; values.TryGetValue(ItemKey + i, out string? item); i++)
        {
            if (Decode(item) is { } child && ShellItems.GetPath([.. root[..^2], .. child]) is { } file)
                files.Add(file);
        }
        return files.Count > 0 ? files : [folder];
    }

    // Explorer keeps the file hidden, and replacing a hidden file (File.WriteAllBytes) is refused: rewrite it in place.
    private static void WriteIni(byte[] content)
    {
        using (var file = new FileStream(IniPath, FileMode.OpenOrCreate, FileAccess.Write))
        {
            file.SetLength(0);
            file.Write(content);
        }
        File.SetAttributes(IniPath, File.GetAttributes(IniPath) | FileAttributes.Hidden);
    }

    private static byte[] IDList(string path) =>
        ShellItems.GetIDList(path) ?? throw new FileNotFoundException("Not found", path);

    // An ID list in a profile: its size (2 bytes) and the list, as ILSaveToStream writes it.
    private static string Encode(byte[] idList) =>
        WallpaperRegistry.EncodeProfileBinary([.. BitConverter.GetBytes((ushort)idList.Length), .. idList]);

    private static byte[]? Decode(string text)
    {
        byte[]? bytes = WallpaperRegistry.DecodeProfileBinary(text);
        if (bytes is null || bytes.Length < 2)
            return null;
        int size = BitConverter.ToUInt16(bytes);
        return size >= 2 && size <= bytes.Length - 2 ? bytes[2..(2 + size)] : null;
    }
}
