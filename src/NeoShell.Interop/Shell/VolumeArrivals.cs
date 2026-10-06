using System.Runtime.InteropServices;
using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;

namespace NeoShell.Interop.Shell;

/// <summary>
/// Drives that appear (a USB stick plugged in, an image mounted) and media inserted into a drive, from the
/// <c>WM_DEVICECHANGE</c> broadcasts Windows sends every top-level window. Create it on the UI thread; the events are
/// raised there.
/// </summary>
public sealed unsafe class VolumeArrivals : IDisposable
{
    private const uint DBT_DEVICEARRIVAL = 0x8000;
    private const uint DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const uint DBT_DEVTYP_VOLUME = 2;

    private readonly MessageWindow _window;

    public VolumeArrivals()
    {
        // Top-level: broadcasts don't reach message-only windows.
        _window = new MessageWindow("NeoShell.VolumeArrivals", OnMessage, parent: 0, User32.WS_POPUP, User32.WS_EX_TOOLWINDOW);
    }

    /// <summary>A drive's root (<c>F:\</c>) that came, or whose media was inserted.</summary>
    public event Action<string>? Arrived;

    /// <summary>A drive's root whose drive or media went away.</summary>
    public event Action<string>? Removed;

    public void Dispose() => _window.Dispose();

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != WindowMessages.DeviceChange || lParam == 0 || (uint)wParam is not (DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE))
            return null;

        var volume = (DEV_BROADCAST_VOLUME*)lParam;
        if (volume->dbcv_devicetype != DBT_DEVTYP_VOLUME)
            return null;

        Action<string>? handler = (uint)wParam == DBT_DEVICEARRIVAL ? Arrived : Removed;
        for (int drive = 0; drive < 26; drive++)
        {
            if ((volume->dbcv_unitmask & (1u << drive)) != 0)
                handler?.Invoke($"{(char)('A' + drive)}:\\");
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_VOLUME
    {
        public uint dbcv_size;
        public uint dbcv_devicetype;
        public uint dbcv_reserved;
        public uint dbcv_unitmask;
        public ushort dbcv_flags;
    }
}
