using System.Globalization;
using System.Text;
using Microsoft.Win32;
using NeoShell.Logging;

namespace NeoShell.Desktop;

/// <summary>What an icon's name in <see cref="IconLayouts"/> says about it, as Explorer marks it.</summary>
[Flags]
public enum LayoutIconFlags
{
    None = 0,
    /// <summary>Set on every icon Explorer saves.</summary>
    Marked = 1,
    Folder = 2,
    /// <summary>On the public Desktop rather than the user's.</summary>
    Common = 4,
}

/// <summary>An icon's place on one monitor's grid, in cells (whole numbers while icons are aligned to the grid).</summary>
/// <param name="Name">The item's parent-relative parsing name: a file name, or <c>::{CLSID}</c> for a system folder.</param>
public sealed record LayoutIcon(string Name, LayoutIconFlags Flags, float X, float Y);

/// <summary>
/// One monitor's grid in a saved desktop: its size in cells and the icons on it. Explorer finds a saved desktop again
/// by its monitors' grids (<see cref="LayoutDesktop.Key"/>).
/// </summary>
/// <param name="Flags">1 on the primary monitor.</param>
/// <param name="ShiftX">Saved by Explorer and kept as is.</param>
public sealed record LayoutWorkspace(int Version, int ShiftX, int ShiftY, int Columns, int Rows, int Flags, IReadOnlyList<LayoutIcon> Icons)
{
    public const int CurrentVersion = 0x10002;
    public const int PrimaryFlag = 1;

    public bool IsPrimary => (Flags & PrimaryFlag) != 0;

    /// <summary>This workspace's part of the desktop's key, as Explorer formats it.</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Flags:00}:({Columns:000}x{Rows:000})");
}

/// <summary>
/// The icons of one monitor arrangement. A desktop Explorer considers the same as another may be linked to it
/// (<see cref="LinkedKey"/>) rather than keep icons of its own.
/// </summary>
/// <param name="LinkFlags">Explorer's per-workspace values for a linked desktop; kept as is.</param>
public sealed record LayoutDesktop(int Version, string? LinkedKey, IReadOnlyList<LayoutWorkspace> Workspaces, IReadOnlyList<int>? LinkFlags)
{
    public const int CurrentVersion = 0x10002;

    /// <summary>How Explorer tells its saved desktops apart: each monitor's flags and grid size, in work-area order.</summary>
    public string Key => KeyOf(Workspaces.Select(workspace => workspace.Key));

    public static string KeyOf(IEnumerable<string> workspaceKeys) => string.Join('_', workspaceKeys);
}

/// <summary>
/// Explorer's saved desktop icon places: <c>HKCU\Software\Microsoft\Windows\Shell\Bags\1\Desktop\IconLayouts</c>,
/// written by shell32's desktop icon layout engine when Explorer's desktop closes. One layout per monitor arrangement
/// (each monitor's grid size), so icons go back where they were when a monitor comes back. NeoShell reads and writes
/// the same value, so icons stay put when switching shells. Found by reading shell32 (DesktopDictionary,
/// DesktopData, WorkspaceData and IconNameTable's Serialize/Deserialize); see docs/design/desktop-icons.md.
/// </summary>
/// <param name="Header">The property bag's 16 bytes before Explorer's stream; kept as read.</param>
public sealed record IconLayouts(byte[] Header, IReadOnlyList<LayoutDesktop> Desktops)
{
    private const string KeyPath = @"Software\Microsoft\Windows\Shell\Bags\1\Desktop";
    private const string ValueName = "IconLayouts";
    private const int DictionaryVersion = 0x10003;
    private const int NameTableVersion = 0x10001;
    // Explorer's own limits when reading.
    private const int MaxDesktops = 0x100;
    private const int MaxWorkspaces = 0x10;
    private const int MaxIcons = 0x1000;

    public static IconLayouts Empty { get; } = new(new byte[16], []);

    /// <summary>
    /// The saved layouts, <see cref="Empty"/> when there are none, or null when they can't be read: then they mustn't
    /// be written over either.
    /// </summary>
    public static IconLayouts? Read()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is byte[] bytes ? Parse(bytes) : Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($@"Could not read HKCU\{KeyPath}\{ValueName}", ex);
            return null;
        }
    }

    public void Save()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(ValueName, Serialize(), RegistryValueKind.Binary);
            // Explorer reads names written this way (version 1) only with this set.
            key.SetValue("IconNameVersion", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($@"Could not save HKCU\{KeyPath}\{ValueName}", ex);
        }
    }

    /// <summary>The desktop with this key, or null.</summary>
    public LayoutDesktop? Find(string key) =>
        Desktops.FirstOrDefault(desktop => string.Equals(desktop.Key, key, StringComparison.Ordinal));

    /// <summary>These layouts with <paramref name="desktop"/> in place of the one with its key, or added last.</summary>
    public IconLayouts With(LayoutDesktop desktop)
    {
        string key = desktop.Key;
        List<LayoutDesktop> desktops = [.. Desktops.Where(other => other.Key != key)];
        desktops.Add(desktop);
        if (desktops.Count > MaxDesktops)
            desktops.RemoveRange(0, desktops.Count - MaxDesktops);
        return this with { Desktops = desktops };
    }

    /// <summary>Reads Explorer's value; null if it isn't in a format this knows.</summary>
    public static IconLayouts? Parse(byte[] bytes)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.Unicode);
            byte[] header = reader.ReadBytes(16);
            if (reader.ReadInt32() != DictionaryVersion || reader.ReadInt32() != NameTableVersion)
                return null;

            // Names are kept once, with their flags after a '>': "name> |" is on the public Desktop.
            var names = new (string Name, LayoutIconFlags Flags)[Count(reader, MaxIcons)];
            for (int i = 0; i < names.Length; i++)
                names[i] = ParseName(ReadString(reader) ?? "");

            var desktops = new LayoutDesktop[Count(reader, MaxDesktops)];
            for (int i = 0; i < desktops.Length; i++)
            {
                int version = reader.ReadInt32();
                if (version is not (0x10001 or 0x10002))
                    return null;
                string? linkedKey = ReadString(reader);
                var workspaces = new LayoutWorkspace[Count(reader, MaxWorkspaces)];
                for (int j = 0; j < workspaces.Length; j++)
                    workspaces[j] = ReadWorkspace(reader, names);
                int[]? linkFlags = version > 0x10001 && linkedKey is not null
                    ? [.. workspaces.Select(_ => reader.ReadInt32())]
                    : null;
                desktops[i] = new LayoutDesktop(version, linkedKey, workspaces, linkFlags);
            }
            return new IconLayouts(header, desktops);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IndexOutOfRangeException or InvalidDataException)
        {
            Log.Warn("Explorer's desktop icon layouts are in a format NeoShell doesn't know", ex);
            return null;
        }
    }

    public byte[] Serialize()
    {
        var names = new List<(string Name, LayoutIconFlags Flags)>();
        var indexes = new Dictionary<(string, LayoutIconFlags), int>(LayoutNameComparer.Instance);
        foreach (LayoutIcon icon in Desktops.SelectMany(desktop => desktop.Workspaces).SelectMany(workspace => workspace.Icons))
        {
            if (indexes.TryAdd((icon.Name, icon.Flags), names.Count))
                names.Add((icon.Name, icon.Flags));
        }

        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write(Header.Length == 16 ? Header : new byte[16]);
            writer.Write(DictionaryVersion);
            writer.Write(NameTableVersion);
            writer.Write((ulong)names.Count);
            foreach ((string name, LayoutIconFlags flags) in names)
                WriteString(writer, FormatName(name, flags));

            writer.Write((ulong)Desktops.Count);
            foreach (LayoutDesktop desktop in Desktops)
            {
                writer.Write(desktop.Version);
                WriteString(writer, desktop.LinkedKey);
                writer.Write((ulong)desktop.Workspaces.Count);
                foreach (LayoutWorkspace workspace in desktop.Workspaces)
                {
                    writer.Write(workspace.Version);
                    if (workspace.Version >= 0x10002)
                    {
                        writer.Write(workspace.ShiftX);
                        writer.Write(workspace.ShiftY);
                    }
                    writer.Write(workspace.Columns);
                    writer.Write(workspace.Rows);
                    writer.Write(workspace.Flags);
                    writer.Write((ulong)workspace.Icons.Count);
                    foreach (LayoutIcon icon in workspace.Icons)
                    {
                        writer.Write(icon.X);
                        writer.Write(icon.Y);
                        writer.Write((ushort)indexes[(icon.Name, icon.Flags)]);
                    }
                }
                if (desktop.Version > 0x10001 && desktop.LinkedKey is not null)
                {
                    for (int i = 0; i < desktop.Workspaces.Count; i++)
                        writer.Write(desktop.LinkFlags?.ElementAtOrDefault(i) ?? 0);
                }
            }
        }
        return stream.ToArray();
    }

    /// <summary>"name" and its flags as Explorer writes them: the name, '>', then '\' for a folder and '|' for common.</summary>
    public static string FormatName(string name, LayoutIconFlags flags) =>
        (flags & LayoutIconFlags.Marked) == 0
            ? name
            : $"{name}>{((flags & LayoutIconFlags.Folder) != 0 ? '\\' : ' ')}{((flags & LayoutIconFlags.Common) != 0 ? '|' : ' ')}";

    public static (string Name, LayoutIconFlags Flags) ParseName(string text)
    {
        var flags = LayoutIconFlags.None;
        int end = text.Length;
        for (int i = 0; i < text.Length; i++)
        {
            LayoutIconFlags flag = text[i] switch
            {
                '>' => LayoutIconFlags.Marked,
                '\\' => LayoutIconFlags.Folder,
                '|' => LayoutIconFlags.Common,
                _ => LayoutIconFlags.None,
            };
            if (flag != LayoutIconFlags.None)
            {
                flags |= flag;
                end = Math.Min(end, i);
            }
        }
        return (text[..end], flags);
    }

    private static LayoutWorkspace ReadWorkspace(BinaryReader reader, (string Name, LayoutIconFlags Flags)[] names)
    {
        int version = reader.ReadInt32();
        if (version is not (0x10001 or 0x10002))
            throw new InvalidDataException($"Workspace version {version:x}");
        int shiftX = 0, shiftY = 0;
        if (version >= 0x10002)
        {
            shiftX = reader.ReadInt32();
            shiftY = reader.ReadInt32();
        }
        int columns = reader.ReadInt32();
        int rows = reader.ReadInt32();
        int flags = reader.ReadInt32();
        var icons = new LayoutIcon[Count(reader, MaxIcons)];
        for (int i = 0; i < icons.Length; i++)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            (string name, LayoutIconFlags iconFlags) = names[reader.ReadUInt16()];
            icons[i] = new LayoutIcon(name, iconFlags, x, y);
        }
        return new LayoutWorkspace(version, shiftX, shiftY, columns, rows, flags, icons);
    }

    private static int Count(BinaryReader reader, int max)
    {
        ulong count = reader.ReadUInt64();
        return count <= (ulong)max ? (int)count : throw new InvalidDataException($"{count} items");
    }

    /// <summary>A length in characters, its terminating null included, then the characters; 0 for none.</summary>
    private static string? ReadString(BinaryReader reader)
    {
        ulong length = reader.ReadUInt64();
        if (length == 0)
            return null;
        if (length > (ulong)short.MaxValue)
            throw new InvalidDataException($"A string of {length} characters");
        string text = new(reader.ReadChars((int)length));
        return text.TrimEnd('\0');
    }

    private static void WriteString(BinaryWriter writer, string? text)
    {
        if (text is null)
        {
            writer.Write(0UL);
            return;
        }
        writer.Write((ulong)text.Length + 1);
        writer.Write((text + '\0').ToCharArray());
    }
}

/// <summary>Icon names compare as Explorer compares them: ignoring case, flags exactly.</summary>
internal sealed class LayoutNameComparer : IEqualityComparer<(string Name, LayoutIconFlags Flags)>
{
    public static readonly LayoutNameComparer Instance = new();

    public bool Equals((string Name, LayoutIconFlags Flags) x, (string Name, LayoutIconFlags Flags) y) =>
        x.Flags == y.Flags && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string Name, LayoutIconFlags Flags) obj) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name), obj.Flags);
}
