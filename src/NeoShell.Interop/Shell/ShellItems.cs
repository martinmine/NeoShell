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
    private static readonly Guid BHID_PropertyStore = new("0384e1a4-1523-439c-a4c8-ab911052f586");

    // PKEY_Link_TargetParsingPath
    internal static readonly Ole32.PROPERTYKEY LinkTargetPathKey = new()
    {
        fmtid = new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"),
        pid = 2,
    };

    // PKEY_AppUserModel_ID
    internal static readonly Ole32.PROPERTYKEY AppUserModelIdKey = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };

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

    internal static IShellItem? Create(string parsingName)
    {
        Guid iid = typeof(IShellItem).GUID;
        return Shell32.SHCreateItemFromParsingName(parsingName, 0, iid, out IShellItem item) == 0 ? item : null;
    }

    /// <summary>A string property of the item, such as a shortcut's target; null if it has none.</summary>
    internal static string? GetString(IShellItem item, Ole32.PROPERTYKEY key)
    {
        Guid handler = BHID_PropertyStore;
        Guid iid = typeof(IPropertyStore).GUID;
        nint storePointer;
        if (item.BindToHandler(0, &handler, &iid, &storePointer) != 0)
            return null;

        IPropertyStore store = ComPointer.TakeOwnership<IPropertyStore>(storePointer);
        Ole32.PROPVARIANT value = default;
        try
        {
            return store.GetValue(&key, &value) == 0 && value.vt == Ole32.VT_LPWSTR && value.pointer != 0
                ? new string((char*)value.pointer)
                : null;
        }
        finally
        {
            Ole32.PropVariantClear(&value);
        }
    }
}
