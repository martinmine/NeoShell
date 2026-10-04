using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

public static unsafe class ShellLaunch
{
    /// <summary>
    /// Opens a file, folder, URI or <c>shell:</c> path as Explorer does, recording the start in
    /// <see cref="UserAssist"/> so the app shows among recent apps. Throws <see cref="Win32Exception"/> on failure.
    /// </summary>
    public static void Open(string file, string? arguments = null)
    {
        fixed (char* filePointer = file)
        fixed (char* argumentsPointer = arguments)
        {
            var info = new Shell32.SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(Shell32.SHELLEXECUTEINFOW),
                fMask = Shell32.SEE_MASK_NOASYNC | Shell32.SEE_MASK_FLAG_NO_UI | Shell32.SEE_MASK_FLAG_LOG_USAGE,
                lpFile = filePointer,
                lpParameters = string.IsNullOrEmpty(arguments) ? null : argumentsPointer,
                nShow = User32.SW_SHOWNORMAL,
            };
            if (!Shell32.ShellExecuteEx(&info))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }
}
