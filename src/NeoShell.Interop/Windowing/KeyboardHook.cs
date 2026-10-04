using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// Sees every key press and release in the session before apps do (<c>WH_KEYBOARD_LL</c>), without changing them.
/// Create it on the UI thread; <see cref="Key"/> is raised there and must return quickly: Windows drops a hook that
/// keeps keystrokes waiting.
/// </summary>
public sealed unsafe class KeyboardHook : IDisposable
{
    // Only one hook per process is needed; the callback has no other way to find it.
    private static KeyboardHook? s_current;

    private nint _hook;

    public KeyboardHook()
    {
        if (s_current is not null)
            throw new InvalidOperationException("A keyboard hook is already installed.");

        _hook = User32.SetWindowsHookEx(User32.WH_KEYBOARD_LL, &OnKey, Kernel32.GetModuleHandle(null), 0);
        if (_hook == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        s_current = this;
    }

    /// <summary>A key went down (true) or up (false); the int is its virtual-key code.</summary>
    public event Action<int, bool>? Key;

    public void Dispose()
    {
        if (_hook == 0)
            return;

        User32.UnhookWindowsHookEx(_hook);
        _hook = 0;
        s_current = null;
    }

    [UnmanagedCallersOnly]
    private static nint OnKey(int code, nint wParam, nint lParam)
    {
        const int HC_ACTION = 0;
        if (code == HC_ACTION && s_current is { } hook)
        {
            try
            {
                var key = (User32.KBDLLHOOKSTRUCT*)lParam;
                hook.Key?.Invoke((int)key->vkCode, (key->flags & User32.LLKHF_UP) == 0);
            }
            catch (Exception ex)
            {
                NativeCallback.Report(ex);
            }
        }
        return User32.CallNextHookEx(0, code, wParam, lParam);
    }
}
