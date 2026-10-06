using System.Runtime.InteropServices;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// Windows 8's immersive colour set, which the Windows 8 era parts of the shell (AutoPlay's chooser) still colour
/// themselves from: shades derived from the accent colour, looked up by name (<c>ImmersiveStartDesktopTilesBackground</c>).
/// uxtheme exports these by ordinal only; they have stayed put since Windows 8.
/// </summary>
public static partial class ImmersiveColors
{
    /// <summary>The colour as 0xAARRGGBB, or null when Windows doesn't know the name.</summary>
    public static uint? Get(string name)
    {
        uint type = GetImmersiveColorTypeFromName(name);
        if (type == uint.MaxValue)
            return null;

        // ABGR.
        uint color = GetImmersiveColorFromColorSetEx(GetImmersiveUserColorSetPreference(false, false), type, true, 0);
        return (color & 0xFF00FF00) | ((color & 0xFF) << 16) | ((color >> 16) & 0xFF);
    }

    [LibraryImport("uxtheme.dll", EntryPoint = "#95")]
    private static partial uint GetImmersiveColorFromColorSetEx(
        uint colorSet, uint colorType, [MarshalAs(UnmanagedType.Bool)] bool ignoreHighContrast, uint highContrastCacheMode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#96", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetImmersiveColorTypeFromName(string name);

    [LibraryImport("uxtheme.dll", EntryPoint = "#98")]
    private static partial uint GetImmersiveUserColorSetPreference(
        [MarshalAs(UnmanagedType.Bool)] bool forceCheckRegistry, [MarshalAs(UnmanagedType.Bool)] bool skipCheckOnFail);
}
