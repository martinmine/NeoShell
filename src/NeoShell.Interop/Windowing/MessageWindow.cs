using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// A message-only window: receives posted and sent messages, is never visible.
/// Create and dispose it on the same thread; its handler runs on that thread.
/// </summary>
public sealed unsafe class MessageWindow : IDisposable
{
    private readonly MessageHandler _handler;
    private GCHandle _self;

    public MessageWindow(string className, MessageHandler handler)
    {
        _handler = handler;
        RegisterClass(className);

        _self = GCHandle.Alloc(this);
        Handle = User32.CreateWindowEx(
            0, className, null, 0, 0, 0, 0, 0,
            User32.HWND_MESSAGE, 0, Kernel32.GetModuleHandle(null), GCHandle.ToIntPtr(_self));
        if (Handle == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            _self.Free();
            throw new Win32Exception(error);
        }
    }

    public nint Handle { get; private set; }

    /// <summary>Finds a message-only window by class name, e.g. one owned by another process. Returns 0 if none.</summary>
    public static nint Find(string className) => User32.FindWindowEx(User32.HWND_MESSAGE, 0, className, null);

    public void Dispose()
    {
        if (Handle == 0)
            return;

        User32.DestroyWindow(Handle);
        Handle = 0;
        _self.Free();
    }

    private static void RegisterClass(string className)
    {
        fixed (char* name = className)
        {
            var windowClass = new User32.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(User32.WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = Kernel32.GetModuleHandle(null),
                lpszClassName = name,
            };
            if (User32.RegisterClassEx(&windowClass) == 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error != User32.ERROR_CLASS_ALREADY_EXISTS)
                    throw new Win32Exception(error);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == User32.WM_NCCREATE)
        {
            // lParam is a CREATESTRUCTW whose first field, lpCreateParams, holds the GCHandle passed to CreateWindowEx.
            User32.SetWindowLongPtr(hwnd, User32.GWLP_USERDATA, *(nint*)lParam);
        }

        nint self = User32.GetWindowLongPtr(hwnd, User32.GWLP_USERDATA);
        if (self != 0 && GCHandle.FromIntPtr(self).Target is MessageWindow window)
        {
            try
            {
                if (window._handler(message, wParam, lParam) is { } result)
                    return result;
            }
            catch (Exception ex)
            {
                NativeCallback.Report(ex);
            }
        }

        return User32.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
