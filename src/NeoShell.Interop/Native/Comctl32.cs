using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class Comctl32
{
    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowSubclass(
        nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> subclassProc,
        nuint subclassId, nuint refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RemoveWindowSubclass(
        nint hwnd, delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint> subclassProc,
        nuint subclassId);

    [LibraryImport("comctl32.dll")]
    public static partial nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
}
