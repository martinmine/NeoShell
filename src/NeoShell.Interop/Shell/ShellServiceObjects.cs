using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>
/// The shell service objects Explorer starts with its taskbar, through stobject.dll's SysTray object: Safely Remove
/// Hardware's tray icon (and a battery icon), and every object registered under
/// <c>HKLM\…\Explorer\ShellServiceObjects</c> (Bluetooth pairing prompts, Sync Center, the volume service…). SysTray
/// runs them all on a thread of its own ("SSO Main"). It's created here on an STA thread of NeoShell's, not the UI
/// thread as Explorer does, so a DLL that hangs while loading or closing can't stop the shell.
/// </summary>
public sealed class ShellServiceObjects
{
    private static readonly Guid CLSID_SysTray = new("35cec8a3-2be6-11d2-8773-92e220524153");
    private static readonly Guid CGID_ShellServiceObject = new("000214d2-0000-0000-c000-000000000046");
    private const uint SSOCMDID_OPEN = 2;
    private const uint SSOCMDID_CLOSE = 3;
    private const uint INFINITE = uint.MaxValue;

    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;

    private ShellServiceObjects(Action<Exception> failed)
    {
        _thread = new Thread(() => Run(failed)) { IsBackground = true, Name = "Shell service objects" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Starts them; <paramref name="failed"/> is called on their thread if SysTray can't be started or closed.</summary>
    public static ShellServiceObjects Start(Action<Exception> failed) => new(failed);

    /// <summary>
    /// Closes them, as Explorer does as its taskbar goes. SysTray waits for every object to close; false if that
    /// takes longer than <paramref name="timeout"/>, and the thread is left to the end of the process.
    /// </summary>
    public bool Stop(TimeSpan timeout)
    {
        _stop.Set();
        if (!_thread.Join(timeout))
            return false;
        _stop.Dispose();
        return true;
    }

    private void Run(Action<Exception> failed)
    {
        IOleCommandTarget? sysTray = null;
        try
        {
            sysTray = Ole32.Create<IOleCommandTarget>(CLSID_SysTray, Ole32.CLSCTX_INPROC_SERVER);
            // Explorer passes a VT_UI4 startup cookie, which SysTray only posts back to Shell_TrayWnd (message 0x574)
            // once the objects are loaded, for Explorer's startup tracking. Without one it posts nothing.
            Exec(sysTray, SSOCMDID_OPEN);
            PumpMessagesUntil(_stop);
            Exec(sysTray, SSOCMDID_CLOSE);
        }
        catch (Exception ex)
        {
            failed(ex);
        }
        finally
        {
            // Released here, on its own apartment's thread, not later by the finalizer.
            if (sysTray is not null)
                ((ComObject)(object)sysTray).FinalRelease();
        }
    }

    private static unsafe void Exec(IOleCommandTarget target, uint command)
    {
        Guid group = CGID_ShellServiceObject;
        Marshal.ThrowExceptionForHR(target.Exec(&group, command, 0, 0, 0));
    }

    // An STA thread must keep handling messages, or a broadcast sent to its COM window hangs the sender.
    private static unsafe void PumpMessagesUntil(WaitHandle stop)
    {
        nint handle = stop.SafeWaitHandle.DangerousGetHandle();
        while (User32.MsgWaitForMultipleObjectsEx(1, &handle, INFINITE, User32.QS_ALLINPUT, User32.MWMO_INPUTAVAILABLE)
            == User32.WAIT_OBJECT_0 + 1)
        {
            while (User32.PeekMessage(out User32.MSG message, 0, 0, 0, User32.PM_REMOVE))
            {
                User32.TranslateMessage(message);
                User32.DispatchMessage(message);
            }
        }
    }
}
