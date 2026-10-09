using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// The windows WinUI shows unconstrained flyouts and menus in (<c>ShouldConstrainToRootBounds="False"</c>): one window
/// per popup, owned by the window the flyout belongs to.
/// </summary>
public static unsafe class PopupWindows
{
    private const string ClassName = "Microsoft.UI.Content.PopupWindowSiteBridge";

    // The popups given an offset, each kept for as long as its window lives.
    private static readonly Dictionary<nint, WindowSubclass> s_offsets = [];
    // The popups hidden whenever WinUI shows them, each kept for as long as its window lives.
    private static readonly Dictionary<nint, WindowSubclass> s_concealed = [];
    // Where concealed popups are partly shown (sliding), held there against WinUI's moves until shown in full.
    private static readonly Dictionary<nint, int> s_held = [];
    // Where popups' right edges are held (KeepRight), and the subclasses holding them.
    private static readonly Dictionary<nint, int> s_rights = [];
    private static readonly Dictionary<nint, WindowSubclass> s_rightSubclasses = [];
    // Set while Place moves a popup: an offset or a hold only applies to the moves WinUI makes.
    private static bool s_placing;

    /// <summary>
    /// The popup windows of <paramref name="owner"/>, shown or not: WinUI keeps a flyout's window (hidden) between
    /// openings and shows it again, and only creates or places it after the flyout's <c>Opened</c>.
    /// </summary>
    public static IReadOnlyList<nint> OwnedBy(nint owner)
    {
        var windows = new List<nint>();
        char* name = stackalloc char[64];
        foreach (nint hwnd in TopLevelWindows.GetAll())
        {
            if (User32.GetWindow(hwnd, User32.GW_OWNER) == owner && User32.GetClassName(hwnd, name, 64) > 0 && new string(name) == ClassName)
                windows.Add(hwnd);
        }
        return windows;
    }

    public static bool IsShown(nint popup) => User32.IsWindowVisible(popup);

    public static bool Exists(nint popup) => User32.IsWindow(popup);

    /// <summary>
    /// Moves the popup to <paramref name="y"/> (and <paramref name="x"/>, when given) and shows only what's above
    /// <paramref name="visibleBottom"/> (screen pixels); a <paramref name="visibleBottom"/> of null shows it all again. A
    /// concealed popup shows again, and while it's partly shown, WinUI's own moves (a layout pass placing it again) leave
    /// it at that height.
    /// </summary>
    public static void Place(nint popup, int y, int? visibleBottom, int? x = null)
    {
        if (!User32.GetWindowRect(popup, out User32.RECT rect))
            return;

        // Less of it shows before it moves down, more only after it moves up: DWM may draw a frame in between.
        bool down = y > rect.top;
        if (down)
            WindowRegion.SetVisibleHeight(popup, rect.right - rect.left, visibleBottom - y);
        s_placing = true;
        try
        {
            User32.SetWindowPos(popup, 0, x ?? rect.left, y, 0, 0, User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }
        finally
        {
            s_placing = false;
        }
        if (!down)
            WindowRegion.SetVisibleHeight(popup, rect.right - rect.left, visibleBottom - y);
        if (s_concealed.ContainsKey(popup))
        {
            Cloak(popup, false);
            if (visibleBottom is null)
                s_held.Remove(popup);
            else
                s_held[popup] = y;
        }
    }

    /// <summary>
    /// Shows the popup <paramref name="pixels"/>() further down than WinUI places it, now and whenever WinUI moves it:
    /// WinUI places a menu inside the monitor's work area, so one over the taskbar goes there after it until WinUI is
    /// told (by the popup's offset). Call on the UI thread; calling again for the same popup does nothing.
    /// </summary>
    public static void Offset(nint popup, Func<int> pixels)
    {
        foreach (nint gone in s_offsets.Keys.Where(w => !Exists(w)).ToList())
            s_offsets.Remove(gone);
        if (s_offsets.ContainsKey(popup))
            return;

        s_offsets[popup] = new WindowSubclass(popup, (message, _, lParam) =>
        {
            var position = (User32.WINDOWPOS*)lParam;
            if (message == User32.WM_WINDOWPOSCHANGING && !s_placing && (position->flags & User32.SWP_NOMOVE) == 0)
                position->y += pixels();
            return null;
        });
        // Where it is now is where WinUI put it.
        if (IsShown(popup))
            Place(popup, GetBounds(popup).Y + pixels(), null);
    }

    /// <summary>
    /// Hides the popup now, and whenever WinUI shows it, until <see cref="Place"/> shows it again: WinUI shows a
    /// flyout's window again where it last was, before the flyout's <c>Opened</c>, so a flyout that slides in would
    /// flash there first. The window is cloaked, as WinUI clears a window region of its own accord. Call on the UI
    /// thread.
    /// </summary>
    public static void Conceal(nint popup)
    {
        Cloak(popup, true);
        s_held.Remove(popup);
        foreach (nint gone in s_concealed.Keys.Where(w => !Exists(w)).ToList())
            s_concealed.Remove(gone);
        if (s_concealed.ContainsKey(popup))
            return;

        s_concealed[popup] = new WindowSubclass(popup, (message, _, lParam) =>
        {
            var position = (User32.WINDOWPOS*)lParam;
            if (message != User32.WM_WINDOWPOSCHANGING)
                return null;
            if ((position->flags & User32.SWP_SHOWWINDOW) != 0)
                Cloak(popup, true);
            // Closed: the next opening is placed by WinUI afresh.
            if ((position->flags & User32.SWP_HIDEWINDOW) != 0)
                s_held.Remove(popup);
            if (!s_placing && (position->flags & User32.SWP_NOMOVE) == 0 && s_held.TryGetValue(popup, out int y))
                position->y = y;
            return null;
        });
    }

    /// <summary>
    /// Keeps the popup's right edge at <paramref name="right"/> (screen pixels) whenever WinUI moves or resizes it, or
    /// lets it go again (null). WinUI places a popup inside the monitor's work area again each time its size changes,
    /// which leaves out the widget sidebar: a flyout at the screen's right edge would jump left of the sidebar until
    /// WinUI is told otherwise by its popup's offset. Call on the UI thread.
    /// </summary>
    public static void KeepRight(nint popup, int? right)
    {
        foreach (nint gone in s_rightSubclasses.Keys.Where(w => !Exists(w)).ToList())
        {
            s_rightSubclasses.Remove(gone);
            s_rights.Remove(gone);
        }
        if (right is not { } edge)
        {
            s_rights.Remove(popup);
            return;
        }

        s_rights[popup] = edge;
        if (s_rightSubclasses.ContainsKey(popup))
            return;
        s_rightSubclasses[popup] = new WindowSubclass(popup, (message, _, lParam) =>
        {
            var position = (User32.WINDOWPOS*)lParam;
            if (message != User32.WM_WINDOWPOSCHANGING || s_placing || !s_rights.TryGetValue(popup, out int held)
                || (position->flags & (User32.SWP_NOMOVE | User32.SWP_NOSIZE)) == (User32.SWP_NOMOVE | User32.SWP_NOSIZE)
                || !User32.GetWindowRect(popup, out User32.RECT rect))
            {
                return null;
            }
            if ((position->flags & User32.SWP_NOMOVE) != 0)
            {
                position->y = rect.top;
                position->flags &= ~User32.SWP_NOMOVE;
            }
            int width = (position->flags & User32.SWP_NOSIZE) != 0 ? rect.right - rect.left : position->cx;
            position->x = held - width;
            return null;
        });
    }

    private static void Cloak(nint popup, bool cloak)
    {
        int value = cloak ? 1 : 0;
        Dwmapi.DwmSetWindowAttribute(popup, Dwmapi.DWMWA_CLOAK, &value, sizeof(int));
    }

    /// <summary>The popup's place on screen, in pixels.</summary>
    public static RectInt32 GetBounds(nint popup) => TopLevelWindows.GetBounds(popup);
}
