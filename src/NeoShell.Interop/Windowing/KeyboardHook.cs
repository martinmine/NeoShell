using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// Sees every key press and release in the session before apps and hotkeys do (<c>WH_KEYBOARD_LL</c>), and can swallow
/// them. Create it on the UI thread; <see cref="Key"/> is called there and must return quickly: Windows drops a hook
/// that keeps keystrokes waiting.
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

    /// <summary>
    /// A key going down (true) or up (false), by its virtual-key code. Returning true swallows it: no app or hotkey
    /// sees it.
    /// </summary>
    public Func<int, bool, bool>? Key { get; set; }

    /// <summary>
    /// Tells Windows another key went down while the Windows or Alt key is held, after the hook swallowed the real one:
    /// let go of, the Windows key would otherwise count as pressed alone and open Start, and Alt would open the menu
    /// bar of the app in front. The key is vkE8, which no keyboard has (AutoHotkey masks these keys with it too).
    /// </summary>
    public static void MaskModifierKeys()
    {
        const ushort Unassigned = 0xE8;
        User32.INPUT* inputs = stackalloc User32.INPUT[2];
        inputs[0] = new User32.INPUT { type = User32.INPUT_KEYBOARD, ki = new User32.KEYBDINPUT { wVk = Unassigned } };
        inputs[1] = new User32.INPUT { type = User32.INPUT_KEYBOARD, ki = new User32.KEYBDINPUT { wVk = Unassigned, dwFlags = User32.KEYEVENTF_KEYUP } };
        User32.SendInput(2, inputs, sizeof(User32.INPUT));
    }

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
                if (hook.Key?.Invoke((int)key->vkCode, (key->flags & User32.LLKHF_UP) == 0) == true)
                    return 1;
            }
            catch (Exception ex)
            {
                NativeCallback.Report(ex);
            }
        }
        return User32.CallNextHookEx(0, code, wParam, lParam);
    }
}
