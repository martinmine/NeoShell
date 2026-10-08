using Microsoft.Win32;
using NeoShell.Logging;

namespace NeoShell.Desktop;

/// <summary>
/// The desktop's view settings, kept where Explorer keeps them, so they carry over when switching shells.
/// </summary>
/// <param name="IconSize">Icon size in effective pixels: 32 small, 48 medium, 96 large.</param>
/// <param name="HiddenSystemIcons">The <c>HideDesktopIcons\NewStartPanel</c> values by CLSID: 1 hides, 0 shows.</param>
/// <param name="AutoArrange">"Auto arrange icons": they stay packed in sort order and can't be put anywhere else.</param>
/// <param name="AlignToGrid">"Align icons to grid": icons go to the nearest cell; off, they stay where they're put.</param>
public sealed record DesktopViewSettings(
    bool ShowIcons, int IconSize, bool ShowHidden, bool ShowProtected, IReadOnlyDictionary<string, int> HiddenSystemIcons,
    bool AutoArrange, bool AlignToGrid)
{
    public const int SmallIcons = 32;
    public const int MediumIcons = 48;
    public const int LargeIcons = 96;

    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string DesktopBagKey = @"Software\Microsoft\Windows\Shell\Bags\1\Desktop";
    /// <summary>The desktop view's folder flags (<c>FOLDERFLAGS</c>).</summary>
    private const string FlagsValue = "FFlags";
    private const int AutoArrangeFlag = 0x1; // FWF_AUTOARRANGE
    private const int AlignToGridFlag = 0x4; // FWF_SNAPTOGRID
    /// <summary>Explorer's flags for the desktop until it first writes its own: Align icons to grid is on.</summary>
    private const int DefaultFlags = 0x40200224;
    private const string HideDesktopIconsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    public static DesktopViewSettings Read()
    {
        using RegistryKey? advanced = Registry.CurrentUser.OpenSubKey(AdvancedKey);
        using RegistryKey? bag = Registry.CurrentUser.OpenSubKey(DesktopBagKey);
        using RegistryKey? hideIcons = Registry.CurrentUser.OpenSubKey(HideDesktopIconsKey);
        int flags = bag?.GetValue(FlagsValue) as int? ?? DefaultFlags;

        var hiddenSystemIcons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in hideIcons?.GetValueNames() ?? [])
        {
            if (hideIcons!.GetValue(name) is int value)
                hiddenSystemIcons[name] = value;
        }

        return new DesktopViewSettings(
            advanced?.GetValue("HideIcons") is not 1,
            ClampIconSize(bag?.GetValue("IconSize") as int?),
            advanced?.GetValue("Hidden") is 1,
            advanced?.GetValue("ShowSuperHidden") is 1,
            hiddenSystemIcons,
            (flags & AutoArrangeFlag) != 0,
            (flags & AlignToGridFlag) != 0);
    }

    /// <summary>Explorer also sizes desktop icons freely with Ctrl+wheel; anything unusable means medium.</summary>
    public static int ClampIconSize(int? size) => size is >= 16 and <= 256 ? size.Value : MediumIcons;

    public static void SaveShowIcons(bool show) => Save(AdvancedKey, "HideIcons", show ? 0 : 1);

    public static void SaveIconSize(int size) => Save(DesktopBagKey, "IconSize", size);

    public static void SaveAutoArrange(bool autoArrange) => SaveFlag(AutoArrangeFlag, autoArrange);

    public static void SaveAlignToGrid(bool alignToGrid) => SaveFlag(AlignToGridFlag, alignToGrid);

    private static void SaveFlag(int flag, bool on)
    {
        using RegistryKey? bag = Registry.CurrentUser.OpenSubKey(DesktopBagKey);
        int flags = bag?.GetValue(FlagsValue) as int? ?? DefaultFlags;
        Save(DesktopBagKey, FlagsValue, on ? flags | flag : flags & ~flag);
    }

    private static void Save(string keyPath, string name, int value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($@"Could not save HKCU\{keyPath}\{name}", ex);
        }
    }
}
