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
    internal const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
    internal const uint SIGDN_FILESYSPATH = 0x80058000;
    internal const uint SIIGBF_BIGGERSIZEOK = 0x01;
    internal const uint SIIGBF_ICONONLY = 0x04;
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

    // PKEY_Link_Arguments
    private static readonly Ole32.PROPERTYKEY LinkArgumentsKey = new()
    {
        fmtid = new Guid("436F2667-14E2-4FEB-B30A-146C53B5B674"),
        pid = 100,
    };

    public static string AppsFolderPath(string appUserModelId) => @"shell:AppsFolder\" + appUserModelId;

    /// <summary>The name Explorer shows for the item, or null if it doesn't exist.</summary>
    public static string? GetDisplayName(string parsingName) =>
        Create(parsingName) is { } item ? GetDisplayName(item) : null;

    internal static string? GetDisplayName(IShellItem item, uint sigdn = SIGDN_NORMALDISPLAY)
    {
        char* name;
        if (item.GetDisplayName(sigdn, &name) != 0)
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

    /// <summary>A shortcut's target (a file system path) and arguments; null if the file isn't a shortcut to a file.</summary>
    public static (string Target, string? Arguments)? ReadShortcut(string path)
    {
        if (Create(path) is not { } item || GetString(item, LinkTargetPathKey) is not { Length: > 0 } target)
            return null;
        return (target, GetString(item, LinkArgumentsKey));
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

    /// <summary>The item's absolute ID list as bytes, or null.</summary>
    internal static byte[]? GetIDList(IShellItem item)
    {
        if (Shell32.SHGetIDListFromObject(item, out nint idList) != 0 || idList == 0)
            return null;

        try
        {
            byte[] bytes = new byte[Shell32.ILGetSize(idList)];
            Marshal.Copy(idList, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Marshal.FreeCoTaskMem(idList);
        }
    }

    /// <summary>Compares file names as Explorer sorts them: digits by their value ("img2" before "img10").</summary>
    public static int CompareNames(string first, string second) => Shlwapi.StrCmpLogicalW(first, second);

    /// <summary>The absolute ID list of a parsing name as bytes, or null if it doesn't exist.</summary>
    public static byte[]? GetIDList(string parsingName) => Create(parsingName) is { } item ? GetIDList(item) : null;

    /// <summary>The file system path of an absolute ID list, or null if it has none (or the bytes aren't one).</summary>
    public static string? GetPath(ReadOnlySpan<byte> idList)
    {
        // Each item starts with its size and a zero size ends the list: walk it first, so Windows can't read past it.
        int offset = 0;
        while (offset + 2 <= idList.Length && BitConverter.ToUInt16(idList[offset..]) is var size and > 0)
            offset += size;
        if (offset + 2 > idList.Length)
            return null;

        nint native = Marshal.AllocCoTaskMem(idList.Length);
        try
        {
            idList.CopyTo(new Span<byte>((void*)native, idList.Length));
            char* path = stackalloc char[260];
            return Shell32.SHGetPathFromIDListW(native, path) ? new string(path) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(native);
        }
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
