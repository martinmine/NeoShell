using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

public static class WindowMessages
{
    /// <summary><c>WM_SYSCOLORCHANGE</c>: a system colour, such as the desktop background colour, changed.</summary>
    public const uint SysColorChange = 0x0015;

    /// <summary><c>WM_SETTINGCHANGE</c>: a system-wide setting changed (wallpaper, theme, work area…).</summary>
    public const uint SettingChange = 0x001A;

    /// <summary><c>WM_TIMECHANGE</c>: the system time or time zone changed.</summary>
    public const uint TimeChange = 0x001E;

    /// <summary><c>WM_DISPLAYCHANGE</c>: monitors were added, removed or changed resolution.</summary>
    public const uint DisplayChange = 0x007E;

    /// <summary><c>WM_DEVICECHANGE</c>: a device was added or removed (wParam <c>DBT_DEVNODES_CHANGED</c>, and others).</summary>
    public const uint DeviceChange = 0x0219;

    /// <summary><c>WM_DPICHANGED</c>: the window's DPI changed; the low word of wParam is the new DPI.</summary>
    public const uint DpiChanged = 0x02E0;

    /// <summary>Returns the system-wide message ID for <paramref name="name"/>; every process gets the same ID.</summary>
    public static uint Register(string name) => User32.RegisterWindowMessage(name);

    public static bool Post(nint hwnd, uint message, nint wParam = 0, nint lParam = 0) =>
        User32.PostMessage(hwnd, message, wParam, lParam);
}
