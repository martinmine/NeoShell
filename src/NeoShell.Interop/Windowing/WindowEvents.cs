using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

public enum WindowEvent
{
    Foreground = 0x0003,
    MinimizeStart = 0x0016,
    MinimizeEnd = 0x0017,
    Destroyed = 0x8001,
    Shown = 0x8002,
    Hidden = 0x8003,
    NameChanged = 0x800C,
    Cloaked = 0x8017,
    Uncloaked = 0x8018,
}

/// <summary>
/// WinEvents about top-level windows of other processes, which catch what shell hooks miss (windows shown, hidden,
/// cloaked, retitled). Create it on the UI thread: the events arrive through that thread's message loop.
/// </summary>
public sealed unsafe class WindowEvents : IDisposable
{
    // The hooks a callback belongs to; only touched on the UI thread.
    private static readonly Dictionary<nint, WindowEvents> s_byHook = [];

    private static readonly (WindowEvent First, WindowEvent Last)[] s_ranges =
    [
        (WindowEvent.Foreground, WindowEvent.Foreground),
        (WindowEvent.MinimizeStart, WindowEvent.MinimizeEnd),
        (WindowEvent.Destroyed, WindowEvent.Hidden),
        (WindowEvent.NameChanged, WindowEvent.NameChanged),
        (WindowEvent.Cloaked, WindowEvent.Uncloaked),
    ];

    private readonly List<nint> _hooks = [];

    public WindowEvents()
    {
        foreach ((WindowEvent first, WindowEvent last) in s_ranges)
        {
            nint hook = User32.SetWinEventHook((uint)first, (uint)last, 0, &OnEvent, 0, 0,
                User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS);
            if (hook == 0)
            {
                Dispose();
                throw new InvalidOperationException("SetWinEventHook failed.");
            }
            _hooks.Add(hook);
            s_byHook[hook] = this;
        }
    }

    /// <summary>The event and the top-level window it is about.</summary>
    public event Action<WindowEvent, nint>? Raised;

    public void Dispose()
    {
        foreach (nint hook in _hooks)
        {
            User32.UnhookWinEvent(hook);
            s_byHook.Remove(hook);
        }
        _hooks.Clear();
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        // Most of these fire for controls, carets and child windows too; only whole top-level windows matter.
        const int OBJID_WINDOW = 0, CHILDID_SELF = 0;
        if (hwnd == 0 || objectId != OBJID_WINDOW || childId != CHILDID_SELF)
            return;

        try
        {
            if (s_byHook.TryGetValue(hook, out WindowEvents? events) && User32.GetAncestor(hwnd, User32.GA_ROOT) == hwnd)
                events.Raised?.Invoke((WindowEvent)eventType, hwnd);
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
    }
}
