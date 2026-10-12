using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Shell;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// Icons at XP's sizes (16 on the taskbar and in menus, 24 and 32 in Start), read off the UI thread and kept: the
/// shell's for files, folders and apps, and icons from resources.
/// </summary>
internal static class XpIcons
{
    private static readonly Dictionary<string, Task<ImageSource?>> s_cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A file's, folder's or shell item's icon (a path or a parsing name such as <c>shell:MyComputerFolder</c>).</summary>
    public static Task<ImageSource?> Of(string parsingName, int size) =>
        Cached($"{size}|{parsingName}", () => ShellItems.GetIcon(parsingName, size));

    /// <summary>An app's icon, as the taskbar and Start find it.</summary>
    public static Task<ImageSource?> Of(PinnedApp app, int size) =>
        Cached($"{size}|app|{app.AppUserModelId}|{app.Path}", () => AppIcons.Read(app, size));

    /// <summary>An icon from a resource, such as <c>shell32.dll,-27</c>.</summary>
    public static Task<ImageSource?> FromResource(string location, int size) =>
        Cached($"{size}|res|{location}", () => AutoPlayHandlers.ExtractIcon(location, size));

    /// <summary>A window's small icon; not kept, as a window changes its icon.</summary>
    public static Task<ImageSource?> OfWindow(nint hwnd) => Load(() => Interop.Windowing.WindowInfo.ReadSmallIcon(hwnd));

    private static Task<ImageSource?> Cached(string key, Func<IconBitmap?> read)
    {
        if (!s_cache.TryGetValue(key, out Task<ImageSource?>? icon))
            s_cache[key] = icon = Load(read);
        return icon;
    }

    private static async Task<ImageSource?> Load(Func<IconBitmap?> read)
    {
        try
        {
            IconBitmap? icon = await Task.Run(read);
            return icon is null ? null : AppIcons.ToImageSource(icon);
        }
        catch (Exception ex)
        {
            Log.Warn("Loading an icon failed", ex);
            return null;
        }
    }
}
