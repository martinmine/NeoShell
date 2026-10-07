using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using NeoShell.Interop.Native;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace NeoShell.Interop.Shell;

/// <summary>How All apps is shown, as Explorer's Start keeps it in <c>AllAppsViewMode</c>.</summary>
public enum AllAppsView { Category = 0, Grid = 1, List = 2 }

/// <summary>Start's sections and All apps view, from Settings → Personalization → Start.</summary>
public sealed record StartSections(bool ShowPinned, bool ShowAll, bool ShowMostUsed, AllAppsView View);

/// <summary>
/// What Explorer's 25H2 Start knows about the apps it lists, read where it keeps it: the category of each app, which
/// apps it has seen (the others show "New"), Windows' own app-to-category mappings, and Start's layout settings.
/// See docs/design.md, "All apps".
/// </summary>
public static class StartAppData
{
    public const string StartKey = @"Software\Microsoft\Windows\CurrentVersion\Start";
    private const string TilePropertiesKey = StartKey + @"\TileProperties";
    // Start's tile store, a Bond map of tile id to properties, in the CloudStore cache.
    private const string RoamedTilesKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\Cache\DefaultAccount\"
        + @"$de${6af0fa78-d3c0-497a-9e70-6449f07e70ff}$$windows.data.unifiedtile.roamedtilepropertiesmap\Current";
    private const string MappingsPath = @"%SystemRoot%\SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy\StartMenu\Assets\AllAppCategoryMappings";

    private static readonly Lazy<IReadOnlyDictionary<string, int>> s_mappings = new(ReadCategoryMappings);

    /// <summary>Start's sections and view: Settings' Pinned, All and "Show most used apps", and the view picked in Start.</summary>
    public static StartSections LoadSections()
    {
        using RegistryKey? start = Registry.CurrentUser.OpenSubKey(StartKey);
        bool On(string name, bool missing) => start?.GetValue(name) is int value ? value != 0 : missing;
        int view = start?.GetValue("AllAppsViewMode") as int? ?? 0;
        return new StartSections(
            On("ShowPinnedSection", true),
            On("ShowAllAppsSection", true),
            On("ShowFrequentList", false),
            Enum.IsDefined((AllAppsView)view) ? (AllAppsView)view : AllAppsView.Category);
    }

    /// <summary>Keeps the view picked in Start where Explorer's Start keeps it, so both show the same.</summary>
    public static void SaveView(AllAppsView view)
    {
        using RegistryKey start = Registry.CurrentUser.CreateSubKey(StartKey);
        start.SetValue("AllAppsViewMode", (int)view, RegistryValueKind.DWord);
    }

    /// <summary>
    /// The category Explorer's Start gave each app, by tile id (<c>P~</c> or <c>W~</c> and the app's id in the
    /// AppsFolder): Windows asks a web service for it when an app turns up, and keeps the answer in the
    /// <c>TileProperties</c> keys. An id with backslashes is a key under a key.
    /// </summary>
    public static IReadOnlyDictionary<string, int> LoadSavedCategories()
    {
        var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using RegistryKey? tiles = Registry.CurrentUser.OpenSubKey(TilePropertiesKey);
        if (tiles is not null)
            AddCategories(tiles, "", categories);
        return categories;
    }

    private static void AddCategories(RegistryKey key, string prefix, Dictionary<string, int> categories)
    {
        foreach (string name in key.GetSubKeyNames())
        {
            using RegistryKey? sub = key.OpenSubKey(name);
            if (sub is null)
                continue;
            string id = prefix + name;
            if (sub.GetValue("Category") is int category)
                categories[id] = category;
            AddCategories(sub, id + @"\", categories);
        }
    }

    /// <summary>
    /// Windows' own mappings of app ids to categories, which Start falls back on when the web service has no answer:
    /// package family names, AppUserModelIDs and paths below the Windows and Program Files folders. Read once.
    /// </summary>
    public static IReadOnlyDictionary<string, int> LoadCategoryMappings() => s_mappings.Value;

    private static IReadOnlyDictionary<string, int> ReadCategoryMappings()
    {
        string path = Environment.ExpandEnvironmentVariables(MappingsPath);
        return File.Exists(path) ? ParseMappings(Decompress(File.ReadAllBytes(path))) : new Dictionary<string, int>();
    }

    /// <summary><c>{"13": ["notepad", …], …}</c>: the first category an id is listed under.</summary>
    internal static IReadOnlyDictionary<string, int> ParseMappings(byte[] json)
    {
        var mappings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        foreach (JsonProperty category in document.RootElement.EnumerateObject())
        {
            if (!int.TryParse(category.Name, out int value) || category.Value.ValueKind != JsonValueKind.Array)
                continue;
            foreach (JsonElement id in category.Value.EnumerateArray())
            {
                if (id.GetString() is { } text)
                    mappings.TryAdd(text, value);
            }
        }
        return mappings;
    }

    // The file is LZMS-compressed with the Compression API's own header, which Decompress reads.
    private static unsafe byte[] Decompress(byte[] data)
    {
        if (!Cabinet.CreateDecompressor(Cabinet.COMPRESS_ALGORITHM_LZMS, 0, out nint decompressor))
            throw new Win32Exception();
        try
        {
            fixed (byte* input = data)
            {
                Cabinet.Decompress(decompressor, input, (nuint)data.Length, null, 0, out nuint size);
                byte[] output = new byte[size];
                fixed (byte* buffer = output)
                {
                    if (!Cabinet.Decompress(decompressor, input, (nuint)data.Length, buffer, size, out size))
                        throw new Win32Exception();
                }
                return output;
            }
        }
        finally
        {
            Cabinet.CloseDecompressor(decompressor);
        }
    }

    /// <summary>
    /// The tiles Explorer's Start has seen (it gives them a first-seen time, the others show "New" until started from
    /// Start); null when Start has kept no tiles at all.
    /// </summary>
    public static IReadOnlySet<string>? LoadSeenTiles()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RoamedTilesKey);
        return key?.GetValue("Data") is byte[] data ? ParseSeenTiles(data) : null;
    }

    // The CloudStore value is a 16-byte header, then Bond compact binary ("CB", version 1): a struct whose field 0 maps
    // tile ids to their properties, where field 0 is StartTileProperties and its field 10 the first-seen time.
    internal static IReadOnlySet<string>? ParseSeenTiles(byte[] data)
    {
        int start = data.AsSpan().IndexOf("CB\u0001\0"u8);
        if (start < 0)
            return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reader = new CompactBinary(data, start + 4);
        while (reader.NextField() is { } field)
        {
            if (field.Id != 0 || field.Type != CompactBinary.Map)
            {
                reader.Skip(field.Type);
                continue;
            }
            foreach ((string tile, bool hasFirstSeen) in reader.ReadTileMap())
            {
                if (hasFirstSeen)
                    seen.Add(tile);
            }
        }
        return seen;
    }

    /// <summary>
    /// Package family names of the packages that ship with Windows (Start shows "System" under such apps), and of
    /// those Explorer's Start marks as system anyway.
    /// </summary>
    public static IReadOnlySet<string> LoadSystemPackageFamilies()
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Package package in new PackageManager().FindPackagesForUser(""))
        {
            if (package.SignatureKind == PackageSignatureKind.System)
                families.Add(package.Id.FamilyName);
        }
        return families;
    }

    /// <summary>A Bond compact binary (v1) reader: just enough to walk Start's tile maps.</summary>
    internal sealed class CompactBinary(byte[] data, int position)
    {
        public const int Stop = 0, StopBase = 1, Bool = 2, UInt8 = 3, UInt16 = 4, UInt32 = 5, UInt64 = 6, Float = 7,
            Double = 8, String = 9, Struct = 10, List = 11, Set = 12, Map = 13, Int8 = 14, Int16 = 15, Int32 = 16,
            Int64 = 17, WString = 18;

        private int _position = position;

        /// <summary>The next field of the struct being read; null at its end.</summary>
        public (int Type, int Id)? NextField()
        {
            while (true)
            {
                byte header = data[_position++];
                int type = header & 0x1F;
                int id = header >> 5;
                if (id == 6)
                    id = data[_position++];
                else if (id == 7)
                {
                    id = BitConverter.ToUInt16(data, _position);
                    _position += 2;
                }
                if (type == Stop)
                    return null;
                // A base class's fields come first, ended by BT_STOP_BASE; they read on as the struct's.
                if (type != StopBase)
                    return (type, id);
            }
        }

        public ulong ReadVarint()
        {
            ulong value = 0;
            for (int shift = 0; ; shift += 7)
            {
                byte b = data[_position++];
                value |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                    return value;
            }
        }

        public string ReadWString()
        {
            int length = (int)ReadVarint();
            string text = Encoding.Unicode.GetString(data, _position, length * 2);
            _position += length * 2;
            return text;
        }

        /// <summary>A map of tile id to tile properties, and whether each has a first-seen time.</summary>
        public IEnumerable<(string Tile, bool HasFirstSeen)> ReadTileMap()
        {
            int keyType = data[_position++] & 0x1F;
            int valueType = data[_position++] & 0x1F;
            int count = (int)ReadVarint();
            for (int i = 0; i < count; i++)
            {
                if (keyType != WString || valueType != Struct)
                    throw new InvalidDataException("Not a map of tile ids to properties");
                string tile = ReadWString();
                bool hasFirstSeen = false;
                while (NextField() is { } field)
                {
                    if (field is (Struct, 0))
                        hasFirstSeen = ReadFirstSeen();
                    else
                        Skip(field.Type);
                }
                yield return (tile, hasFirstSeen);
            }
        }

        // StartTileProperties: field 10 is FirstSeenTime, a FILETIME.
        private bool ReadFirstSeen()
        {
            bool seen = false;
            while (NextField() is { } field)
            {
                if (field is (UInt64, 10))
                    seen = ReadVarint() != 0;
                else
                    Skip(field.Type);
            }
            return seen;
        }

        public void Skip(int type)
        {
            switch (type)
            {
                case Bool or UInt8 or Int8:
                    _position++;
                    break;
                case UInt16 or UInt32 or UInt64 or Int16 or Int32 or Int64:
                    ReadVarint();
                    break;
                case Float:
                    _position += 4;
                    break;
                case Double:
                    _position += 8;
                    break;
                case String:
                    _position += (int)ReadVarint();
                    break;
                case WString:
                    _position += (int)ReadVarint() * 2;
                    break;
                case Struct:
                    while (NextField() is { } field)
                        Skip(field.Type);
                    break;
                case List or Set:
                {
                    int elementType = data[_position++] & 0x1F;
                    int count = (int)ReadVarint();
                    for (int i = 0; i < count; i++)
                        Skip(elementType);
                    break;
                }
                case Map:
                {
                    int keyType = data[_position++] & 0x1F;
                    int valueType = data[_position++] & 0x1F;
                    int count = (int)ReadVarint();
                    for (int i = 0; i < count; i++)
                    {
                        Skip(keyType);
                        Skip(valueType);
                    }
                    break;
                }
                default:
                    throw new InvalidDataException($"Unknown Bond type {type}");
            }
        }
    }
}
