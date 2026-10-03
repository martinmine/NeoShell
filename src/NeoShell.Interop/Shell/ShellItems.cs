using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>
/// Shell items by parsing name: a file path, or <c>shell:AppsFolder\&lt;AUMID&gt;</c> for any installed app,
/// Win32 or packaged. Safe to call from a background thread.
/// </summary>
public static unsafe class ShellItems
{
    private const uint SIGDN_NORMALDISPLAY = 0;
    private const uint SIIGBF_BIGGERSIZEOK = 0x01;
    private const uint SIIGBF_ICONONLY = 0x04;

    public static string AppsFolderPath(string appUserModelId) => @"shell:AppsFolder\" + appUserModelId;

    /// <summary>The name Explorer shows for the item, or null if it doesn't exist.</summary>
    public static string? GetDisplayName(string parsingName)
    {
        if (Create(parsingName) is not { } item)
            return null;

        char* name;
        if (item.GetDisplayName(SIGDN_NORMALDISPLAY, &name) != 0)
            return null;

        try
        {
            return new string(name);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)name);
        }
    }

    /// <summary>The item's icon at <paramref name="size"/> pixels square, or null.</summary>
    public static IconBitmap? GetIcon(string parsingName, int size)
    {
        if (Create(parsingName) is not IShellItemImageFactory factory)
            return null;

        return factory.GetImage(new User32.SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out nint bitmap) == 0
            ? IconBitmap.FromBitmap(bitmap)
            : null;
    }

    private static IShellItem? Create(string parsingName)
    {
        Guid iid = typeof(IShellItem).GUID;
        return Shell32.SHCreateItemFromParsingName(parsingName, 0, iid, out IShellItem item) == 0 ? item : null;
    }
}
