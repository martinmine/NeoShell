using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Accessibility;

/// <summary>Sticky keys: shortcuts one key at a time (<c>SPI_GETSTICKYKEYS</c>).</summary>
public static unsafe class StickyKeys
{
    public static bool IsOn
    {
        get => (Read().dwFlags & User32.SKF_STICKYKEYSON) != 0;
        set
        {
            User32.STICKYKEYS keys = Read();
            keys.dwFlags = value ? keys.dwFlags | User32.SKF_STICKYKEYSON : keys.dwFlags & ~User32.SKF_STICKYKEYSON;
            // Saved to the profile, as Settings does, and announced so apps see it.
            if (!User32.SystemParametersInfo(User32.SPI_SETSTICKYKEYS, keys.cbSize, &keys, User32.SPIF_UPDATEINIFILE | User32.SPIF_SENDCHANGE))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static User32.STICKYKEYS Read()
    {
        var keys = new User32.STICKYKEYS { cbSize = (uint)sizeof(User32.STICKYKEYS) };
        if (!User32.SystemParametersInfo(User32.SPI_GETSTICKYKEYS, keys.cbSize, &keys, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return keys;
    }
}
