using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>
/// The shell's undo history for file operations, which Explorer's desktop offers as "Undo Delete", "Redo Rename"...
/// It is one per session, shared by every process: file operations that keep an undo record (the shell's own
/// commands and drop targets, <c>IFileOperation</c> with <c>FOFX_ADDUNDORECORD</c>) add it to the thread's undo
/// manager (<c>SHGetThreadUndoManager</c>), which passes it on to the desktop undo manager: a local server
/// (<c>CLSID_DesktopUndoManager</c>) that the shell window's process serves when asked, or else rundll32. Read from
/// shell32 (<c>SHGetThreadUndoManager</c>, <c>_CreateDesktopLocalServer</c>, <c>_InitEditUndoRedo</c>,
/// <c>CUndoRedoCommand</c>); see docs/design/desktop-icons.md, "Undo". UI thread: the thread's undo manager is the
/// calling thread's.
/// </summary>
public static unsafe class ShellUndo
{
    private static readonly Guid CLSID_UndoCommand = new("0dbd7044-8ea5-4573-a924-fe8565cce18f");
    private static readonly Guid CLSID_RedoCommand = new("1cc6b704-d0f5-4dc3-a521-13620d89e8bc");
    private const uint ECS_ENABLED = 0;
    private const int MenuTextFlag = 1;
    private const int MaxText = 80;

    /// <summary>What Undo would undo, as Explorer's menu shows it ("Undo Delete"), or null when there is nothing.</summary>
    public static string? UndoText() => Text(undo: true);

    /// <summary>What Redo would redo ("Redo Rename"), or null when there is nothing.</summary>
    public static string? RedoText() => Text(undo: false);

    /// <summary>Undoes the latest file operation, on a thread of the shell's own, with its progress and dialogs.</summary>
    public static void Undo() => Invoke(CLSID_UndoCommand);

    public static void Redo() => Invoke(CLSID_RedoCommand);

    /// <summary>
    /// The UI thread's undo manager, kept for the thread's life as Explorer's desktop view keeps its own: shell32 frees
    /// a thread's manager with its last reference, so without one held every call would make a new one.
    /// </summary>
    [ThreadStatic]
    private static IOleUndoManager? s_threadManager;

    /// <summary>
    /// Whether there is something to undo (or redo), as File Explorer's Undo and Redo commands tell: shell32's own
    /// test, which also says no while an undo is running.
    /// </summary>
    private static bool CanDo(bool undo)
    {
        IExplorerCommand? command = null;
        try
        {
            command = CreateCommand(undo ? CLSID_UndoCommand : CLSID_RedoCommand);
            uint state;
            return command.GetState(0, 1, &state) >= 0 && state == ECS_ENABLED;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            Release(command);
        }
    }

    /// <summary>
    /// The menu text Explorer's desktop shows: the latest unit's own ("&amp;Undo Delete\tCtrl+Z"), without its access
    /// key and shortcut. Like Explorer, just "Undo" or "Redo" when the unit gives none: units can't be passed to
    /// another process, so only the process serving the history (<see cref="ShellUndoServer"/>) can ask them.
    /// </summary>
    private static string? Text(bool undo)
    {
        if (ThreadManager() is not { } manager || !CanDo(undo))
            return null;

        string text = "";
        nint pointer;
        IEnumOleUndoUnits? units = null;
        IOleUndoUnit? unit = null;
        try
        {
            if ((undo ? manager.EnumUndoable(&pointer) : manager.EnumRedoable(&pointer)) >= 0 && pointer != 0)
            {
                units = ComPointer.TakeOwnershipUnique<IEnumOleUndoUnits>(pointer);
                uint fetched;
                if (units.Next(1, &pointer, &fetched) == 0 && pointer != 0)
                    unit = ComPointer.TakeOwnershipUnique<IOleUndoUnit>(pointer);
            }
            if (unit is IShellUndoUnit shellUnit)
            {
                char* buffer = stackalloc char[MaxText];
                buffer[0] = '\0';
                if (shellUnit.GetUndoText(buffer, MaxText, MenuTextFlag) >= 0)
                    text = MenuText.Clean(new string(buffer));
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // The plain text below.
        }
        finally
        {
            Release(unit);
            Release(units);
        }
        return text.Length > 0 ? text : undo ? "Undo" : "Redo";
    }

    private static IOleUndoManager? ThreadManager()
    {
        nint pointer;
        if (s_threadManager is null && Shell32.GetThreadUndoManager(&pointer, 0) >= 0 && pointer != 0)
            s_threadManager = ComPointer.TakeOwnership<IOleUndoManager>(pointer);
        return s_threadManager;
    }

    private static void Invoke(Guid id)
    {
        IExplorerCommand? command = null;
        try
        {
            command = CreateCommand(id);
            command.Invoke(0, 0);
        }
        catch (COMException)
        {
            // Nothing to undo after all, which shell32 beeps for itself.
        }
        finally
        {
            Release(command);
        }
    }

    private static IExplorerCommand CreateCommand(Guid id)
    {
        Marshal.ThrowExceptionForHR(Ole32.CoCreateInstance(id, 0, Ole32.CLSCTX_INPROC_SERVER, typeof(IExplorerCommand).GUID, out nint command));
        return ComPointer.TakeOwnershipUnique<IExplorerCommand>(command);
    }

    /// <summary>Releases a wrapper now, on this thread: proxies to the history's thread belong to this apartment.</summary>
    private static void Release(object? wrapper)
    {
        if (wrapper is ComObject comObject)
            comObject.FinalRelease();
    }
}

/// <summary>
/// Serves the session's undo history (<c>CLSID_DesktopUndoManager</c>) in this process, as Explorer's desktop does: so
/// the history lives with the shell, and its units, which can't be passed to another process, can tell the desktop's
/// menu what they undo. shell32's rundll32 entry does it on a thread of NeoShell's: it first asks the shell window
/// (NeoShell's, which doesn't take it on) and then registers the class and runs its message loop until told to quit.
/// </summary>
public sealed unsafe class ShellUndoServer : IDisposable
{
    private const string ClassId = "{3eef301f-b596-4c0b-bd92-013beafce793}";

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;

    private ShellUndoServer()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Desktop undo manager" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Starts serving; only while this process is the shell (its window is <c>GetShellWindow</c>).</summary>
    public static ShellUndoServer Start() => new();

    public void Dispose()
    {
        _started.Wait();
        User32.PostThreadMessage(_threadId, User32.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(3));
    }

    private void Run()
    {
        _threadId = Kernel32.GetCurrentThreadId();
        _started.Set();
        byte* classId = stackalloc byte[ClassId.Length + 1];
        for (int i = 0; i < ClassId.Length; i++)
            classId[i] = (byte)ClassId[i];
        classId[ClassId.Length] = 0;
        Shell32.CreateLocalServerRunDll(0, 0, classId, 0);
    }
}
