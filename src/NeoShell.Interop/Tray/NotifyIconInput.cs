using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Tray;

/// <summary>What the user did to a tray icon.</summary>
public enum TrayMouseEvent
{
    LeftDown,
    LeftUp,
    LeftDoubleClick,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    Move,
    /// <summary>Hovering long enough for a tooltip; version 4 icons show their own popup.</summary>
    HoverStart,
    HoverEnd,
}

/// <summary>What became of an icon's balloon notification, as its app hears it.</summary>
public enum BalloonEvent : uint
{
    Shown = 0x0402,         // NIN_BALLOONSHOW
    Hidden = 0x0403,        // NIN_BALLOONHIDE
    TimedOut = 0x0404,      // NIN_BALLOONTIMEOUT: also when closed with its close button
    Clicked = 0x0405,       // NIN_BALLOONUSERCLICK
}

/// <summary>Tells a tray icon's app what the user did, in the form the icon's version expects.</summary>
public static class NotifyIconInput
{
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_POPUPOPEN = 0x0406;
    private const uint NIN_POPUPCLOSE = 0x0407;

    /// <summary>The system's double-click time in milliseconds.</summary>
    public static uint DoubleClickTime => User32.GetDoubleClickTime();

    /// <summary>
    /// Posts the callback messages for <paramref name="mouseEvent"/>. Clicks first let the icon's process take the
    /// foreground, so the menu or window it opens comes to the front.
    /// </summary>
    /// <param name="anchor">Screen point the app should open its menu or popup at.</param>
    public static void Send(nint window, uint id, uint callbackMessage, uint version, TrayMouseEvent mouseEvent, PointInt32 anchor)
    {
        if (mouseEvent is not (TrayMouseEvent.Move or TrayMouseEvent.HoverStart or TrayMouseEvent.HoverEnd))
        {
            User32.GetWindowThreadProcessId(window, out uint processId);
            User32.AllowSetForegroundWindow(processId);
        }

        foreach ((nint wParam, nint lParam) in Messages(id, version, mouseEvent, anchor))
            User32.PostMessage(window, callbackMessage, wParam, lParam);
    }

    /// <summary>Tells the app what became of its balloon.</summary>
    public static void Send(nint window, uint id, uint callbackMessage, uint version, BalloonEvent balloonEvent)
    {
        (nint wParam, nint lParam) = Message(id, version, balloonEvent);
        User32.PostMessage(window, callbackMessage, wParam, lParam);
    }

    /// <summary>A balloon's message as Explorer sends it: version 4 icons get no anchor (0, 0).</summary>
    internal static (nint WParam, nint LParam) Message(uint id, uint version, BalloonEvent balloonEvent) =>
        version >= 4 ? (0, MakeLong((ushort)balloonEvent, (ushort)id)) : ((nint)id, (nint)(uint)balloonEvent);

    /// <summary>
    /// The callback messages, as Explorer sends them. Version 4 icons get the anchor in wParam and the message and ID
    /// in lParam; older icons get the ID in wParam and the mouse message in lParam. Since version 3 a left click also
    /// sends NIN_SELECT and a right click WM_CONTEXTMENU, which is what most apps act on.
    /// </summary>
    internal static IReadOnlyList<(nint WParam, nint LParam)> Messages(uint id, uint version, TrayMouseEvent mouseEvent, PointInt32 anchor)
    {
        bool v3 = version >= 3;
        bool v4 = version >= 4;
        uint[] messages = mouseEvent switch
        {
            TrayMouseEvent.LeftDown => [WM_LBUTTONDOWN],
            TrayMouseEvent.LeftUp => v3 ? [WM_LBUTTONUP, NIN_SELECT] : [WM_LBUTTONUP],
            TrayMouseEvent.LeftDoubleClick => [WM_LBUTTONDBLCLK],
            TrayMouseEvent.RightDown => [WM_RBUTTONDOWN],
            TrayMouseEvent.RightUp => v3 ? [WM_RBUTTONUP, WM_CONTEXTMENU] : [WM_RBUTTONUP],
            TrayMouseEvent.MiddleDown => [WM_MBUTTONDOWN],
            TrayMouseEvent.MiddleUp => [WM_MBUTTONUP],
            TrayMouseEvent.Move => [WM_MOUSEMOVE],
            TrayMouseEvent.HoverStart => v4 ? [NIN_POPUPOPEN] : [],
            TrayMouseEvent.HoverEnd => v4 ? [NIN_POPUPCLOSE] : [],
            _ => [],
        };

        return [.. messages.Select(message => v4
            ? (MakeLong((ushort)(short)anchor.X, (ushort)(short)anchor.Y), MakeLong((ushort)message, (ushort)id))
            : ((nint)id, (nint)message))];
    }

    private static nint MakeLong(ushort low, ushort high) => (nint)(uint)((high << 16) | low);
}
