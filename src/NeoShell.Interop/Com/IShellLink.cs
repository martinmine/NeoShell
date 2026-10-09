using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Shell links and the lists jump lists are made of (shobjidl_core.h, objidl.h). Methods NeoShell never calls are
// declared without parameters: only their place in the vtable matters.

[GeneratedComInterface]
[Guid("000214f9-0000-0000-c000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    [PreserveSig] int GetPath();
    [PreserveSig] int GetIDList(nint* idList);
    [PreserveSig] int SetIDList();
    [PreserveSig] int GetDescription(char* name, int length);
    [PreserveSig] int SetDescription();
    [PreserveSig] int GetWorkingDirectory();
    [PreserveSig] int SetWorkingDirectory();
    [PreserveSig] int GetArguments();
    [PreserveSig] int SetArguments();
    [PreserveSig] int GetHotkey();
    [PreserveSig] int SetHotkey();
    [PreserveSig] int GetShowCmd();
    [PreserveSig] int SetShowCmd();
    [PreserveSig] int GetIconLocation(char* path, int length, int* index);
}

[GeneratedComInterface]
[Guid("00000109-0000-0000-c000-000000000046")]
internal partial interface IPersistStream
{
    [PreserveSig] int GetClassID();
    [PreserveSig] int IsDirty();
    [PreserveSig] int Load(nint stream);
    [PreserveSig] int Save(nint stream, [MarshalAs(UnmanagedType.Bool)] bool clearDirty);
}

[GeneratedComInterface]
[Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9")]
internal partial interface IObjectArray
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, in Guid iid, out nint item);
}
