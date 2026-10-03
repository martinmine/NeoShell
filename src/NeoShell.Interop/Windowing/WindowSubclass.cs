using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

/// <summary>
/// Sees the messages of an existing window (e.g. a WinUI window's HWND) before its own window procedure does.
/// Create and dispose it on the window's thread. The subclass removes itself when the window is destroyed.
/// </summary>
public sealed unsafe class WindowSubclass : IDisposable
{
    private readonly nint _hwnd;
    private readonly MessageHandler _handler;
    private GCHandle _self;
    private bool _removed;

    public WindowSubclass(nint hwnd, MessageHandler handler)
    {
        _hwnd = hwnd;
        _handler = handler;
        _self = GCHandle.Alloc(this);
        if (!Comctl32.SetWindowSubclass(hwnd, &SubclassProc, Id, Id))
        {
            _self.Free();
            throw new Win32Exception($"SetWindowSubclass failed for window 0x{hwnd:X}.");
        }
    }

    // The GCHandle is unique per instance, so it doubles as the subclass ID.
    private nuint Id => (nuint)GCHandle.ToIntPtr(_self);

    public void Dispose()
    {
        if (!_self.IsAllocated)
            return;

        Remove();
        _self.Free();
    }

    private void Remove()
    {
        if (_removed)
            return;

        Comctl32.RemoveWindowSubclass(_hwnd, &SubclassProc, Id);
        _removed = true;
    }

    [UnmanagedCallersOnly]
    private static nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (GCHandle.FromIntPtr((nint)refData).Target is WindowSubclass subclass)
        {
            if (message == User32.WM_NCDESTROY)
            {
                // Documented requirement: remove the subclass before the window is gone.
                subclass.Remove();
            }
            else
            {
                try
                {
                    if (subclass._handler(message, wParam, lParam) is { } result)
                        return result;
                }
                catch (Exception ex)
                {
                    NativeCallback.Report(ex);
                }
            }
        }

        return Comctl32.DefSubclassProc(hwnd, message, wParam, lParam);
    }
}
