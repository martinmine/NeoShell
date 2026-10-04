using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>Reads the apps pinned to Explorer's taskbar.</summary>
public static class TaskbarFavorites
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

    /// <summary>
    /// The pins in the taskbar's order. The shortcuts in <c>User Pinned\TaskBar</c> come in no order and packaged
    /// apps have none, so they're read from <c>Taskband\Favorites</c>, which holds every pin's ID list in order.
    /// Call it off the UI thread.
    /// </summary>
    public static IReadOnlyList<ExplorerPin> ReadPinned()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue("Favorites") is byte[] favorites
            ? [.. Parse(favorites).Select(ToPin)]
            : [];
    }

    /// <summary>
    /// The ID lists in a <c>Favorites</c> value: a version byte, then per pin a 32-bit length, the ID list and a
    /// separator byte, 0xFF after the last. A truncated entry ends the list.
    /// </summary>
    internal static IEnumerable<byte[]> Parse(byte[] favorites)
    {
        int offset = 1;
        while (offset + 4 <= favorites.Length)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(favorites.AsSpan(offset));
            offset += 4;
            if (length <= 0 || length > favorites.Length - offset)
                yield break;

            yield return favorites[offset..(offset + length)];
            offset += length;
            if (offset >= favorites.Length || favorites[offset] == 0xFF)
                yield break;
            offset++;
        }
    }

    private static ExplorerPin ToPin(byte[] idList)
    {
        nint pointer = Marshal.AllocCoTaskMem(idList.Length);
        try
        {
            Marshal.Copy(idList, 0, pointer, idList.Length);
            return ExplorerPin.FromItem(Shell32.SHCreateItemFromIDList(pointer, typeof(IShellItem).GUID, out IShellItem item) == 0 ? item : null);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }
}
