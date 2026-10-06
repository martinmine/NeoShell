using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Com;

// IDataObject pointers are passed as nint: NeoShell only hands them on to the shell.

[GeneratedComInterface]
[Guid("00000122-0000-0000-c000-000000000046")]
internal unsafe partial interface IDropTarget
{
    [PreserveSig] int DragEnter(nint dataObject, uint keyState, User32.POINT point, uint* effect);
    [PreserveSig] int DragOver(uint keyState, User32.POINT point, uint* effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(nint dataObject, uint keyState, User32.POINT point, uint* effect);
}

/// <summary>Draws the drag image (and the shell's "Move to Desktop" label) over a drop target's window.</summary>
[GeneratedComInterface]
[Guid("4657278b-411b-11d2-839a-00c04fd918d0")]
internal unsafe partial interface IDropTargetHelper
{
    [PreserveSig] int DragEnter(nint target, nint dataObject, User32.POINT* point, uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int DragOver(User32.POINT* point, uint effect);
    [PreserveSig] int Drop(nint dataObject, User32.POINT* point, uint effect);
    [PreserveSig] int Show(int show);
}
