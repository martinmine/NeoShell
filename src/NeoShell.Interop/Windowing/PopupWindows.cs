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
    // Set while Place moves a popup: an offset only applies to the moves WinUI makes.
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
    /// Moves the popup to <paramref name="y"/> and shows only what's above <paramref name="visibleBottom"/> (screen
    /// pixels); a <paramref name="visibleBottom"/> of null shows it all again.
    /// </summary>
    public static void Place(nint popup, int y, int? visibleBottom)
    {
        if (!User32.GetWindowRect(popup, out User32.RECT rect))
            return;

        s_placing = true;
        try
        {
            User32.SetWindowPos(popup, 0, rect.left, y, 0, 0, User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }
        finally
        {
            s_placing = false;
        }
        // The system owns a region once it's set, and deletes it.
        nint region = visibleBottom is { } bottom ? Gdi32.CreateRectRgn(0, 0, rect.right - rect.left, Math.Max(0, bottom - y)) : 0;
        User32.SetWindowRgn(popup, region, true);
    }

    /// <summary>
    /// Shows the popup <paramref name="pixels"/>() further down than WinUI places it, now and whenever WinUI moves it:
    /// WinUI keeps popups inside the monitor's work area, so one over the taskbar has to be moved there after it.
    /// Call on the UI thread; calling again for the same popup does nothing.
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

    /// <summary>The popup's place on screen, in pixels.</summary>
    public static RectInt32 GetBounds(nint popup) => TopLevelWindows.GetBounds(popup);
}
