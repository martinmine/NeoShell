using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Windowing;

/// <summary>The <c>HSHELL_*</c> notifications NeoShell uses.</summary>
public enum ShellHookEvent
{
    WindowCreated = 1,
    WindowDestroyed = 2,
    WindowActivated = 4,
    /// <summary>A window's title or icon changed.</summary>
    Redraw = 6,
    WindowReplaced = 13,
    WindowReplacing = 14,
    /// <summary>A full-screen app was activated.</summary>
    RudeAppActivated = 0x8004,
    /// <summary>A window asks for attention (<c>FlashWindowEx</c>).</summary>
    Flash = 0x8006,
}

/// <summary>
/// Receives the shell hook messages Windows sends to taskbars: windows created, destroyed, activated, redrawn and
/// flashing. Create it on the UI thread; <see cref="Raised"/> is raised there.
/// </summary>
public sealed unsafe class ShellHook : IDisposable
{
    private const int HSHELL_GETMINRECT = 5;

    private readonly uint _shellHookMessage = User32.RegisterWindowMessage("SHELLHOOK");
    private readonly MessageWindow _window;

    public ShellHook()
    {
        _window = new MessageWindow("NeoShell.ShellHook", OnMessage);
        if (!User32.RegisterShellHookWindow(_window.Handle))
        {
            _window.Dispose();
            throw new InvalidOperationException("RegisterShellHookWindow failed.");
        }
    }

    /// <summary>The event and the window it is about.</summary>
    public event Action<ShellHookEvent, nint>? Raised;

    /// <summary>
    /// Where a window minimizes to and restores from, in screen pixels: its taskbar button, which Windows animates it
    /// to and from. Without an answer it doesn't animate. Asked on the UI thread while Windows waits (100 ms at most).
    /// </summary>
    public Func<nint, RectInt32?>? MinimizeRect { get; set; }

    public void Dispose()
    {
        User32.DeregisterShellHookWindow(_window.Handle);
        _window.Dispose();
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != _shellHookMessage)
            return null;

        // DefWindowProc turns Windows' WM_KLUDGEMINRECT into this, with a SHELLHOOKINFO in this process.
        if (wParam == HSHELL_GETMINRECT)
        {
            var info = (User32.SHELLHOOKINFO*)lParam;
            if (MinimizeRect?.Invoke(info->hwnd) is not { } bounds)
                return 0;
            info->left = (short)bounds.X;
            info->top = (short)bounds.Y;
            info->right = (short)(bounds.X + bounds.Width);
            info->bottom = (short)(bounds.Y + bounds.Height);
            return 1;
        }

        if (Enum.IsDefined((ShellHookEvent)(int)wParam))
            Raised?.Invoke((ShellHookEvent)(int)wParam, lParam);
        return 0;
    }
}
