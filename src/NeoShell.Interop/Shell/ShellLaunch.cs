using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

public static unsafe class ShellLaunch
{
    /// <summary>
    /// Opens a file, folder, URI or <c>shell:</c> path as Explorer does, recording the start in
    /// <see cref="UserAssist"/> so the app shows among recent apps; as administrator (asking first) with
    /// <paramref name="elevated"/>. Throws <see cref="Win32Exception"/> on failure, including a declined elevation.
    /// </summary>
    public static void Open(string file, string? arguments = null, bool elevated = false)
    {
        fixed (char* filePointer = file)
        fixed (char* argumentsPointer = arguments)
        fixed (char* verbPointer = "runas")
        {
            var info = new Shell32.SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(Shell32.SHELLEXECUTEINFOW),
                fMask = Shell32.SEE_MASK_NOASYNC | Shell32.SEE_MASK_FLAG_NO_UI | Shell32.SEE_MASK_FLAG_LOG_USAGE,
                lpVerb = elevated ? verbPointer : null,
                lpFile = filePointer,
                lpParameters = string.IsNullOrEmpty(arguments) ? null : argumentsPointer,
                nShow = User32.SW_SHOWNORMAL,
            };
            if (!Shell32.ShellExecuteEx(&info))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    // Where the Run dialog's bottom-left corner goes, for the hook on its thread.
    [ThreadStatic]
    private static (int Left, int Bottom) t_runDialogCorner;

    /// <summary>
    /// Shows Windows' Run dialog, as Win+R and the Quick Link menu do, with its bottom-left corner at
    /// (<paramref name="left"/>, <paramref name="bottom"/>) in screen pixels: Explorer's sits above the Start button.
    /// It runs its own message loop until closed, so it gets a thread of its own.
    /// </summary>
    public static void ShowRunDialog(int left, int bottom)
    {
        var thread = new Thread(() =>
        {
            // Without an owner (which it would disable while open) the dialog goes to the screen's corner; it's
            // moved as it's first activated, before it's drawn.
            t_runDialogCorner = (left, bottom);
            nint hook = User32.SetWindowsHookEx(User32.WH_CBT, &PlaceRunDialog, 0, Kernel32.GetCurrentThreadId());
            Shell32.RunFileDlg(0, 0, null, null, null, 0);
            if (hook != 0)
                User32.UnhookWindowsHookEx(hook);
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    [UnmanagedCallersOnly]
    private static nint PlaceRunDialog(int code, nint wParam, nint lParam)
    {
        if (code == User32.HCBT_ACTIVATE && t_runDialogCorner != default && User32.GetWindowRect(wParam, out User32.RECT rect))
        {
            (int left, int bottom) = t_runDialogCorner;
            t_runDialogCorner = default;
            User32.SetWindowPos(wParam, 0, left, bottom - (rect.bottom - rect.top), 0, 0,
                User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }
        return User32.CallNextHookEx(0, code, wParam, lParam);
    }
}
