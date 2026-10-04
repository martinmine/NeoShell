using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// PIDLs (ITEMIDLIST pointers) are passed as nint.

[GeneratedComInterface]
[Guid("000214e6-0000-0000-c000-000000000046")]
internal unsafe partial interface IShellFolder
{
    [PreserveSig] int ParseDisplayName(nint hwnd, nint bindContext, char* name, uint* eaten, nint* idList, uint* attributes);
    [PreserveSig] int EnumObjects(nint hwnd, uint flags, nint* enumIdList);
    [PreserveSig] int BindToObject(nint idList, nint bindContext, Guid* iid, nint* result);
    [PreserveSig] int BindToStorage(nint idList, nint bindContext, Guid* iid, nint* result);
    [PreserveSig] int CompareIDs(nint lParam, nint idList1, nint idList2);
    [PreserveSig] int CreateViewObject(nint owner, Guid* iid, nint* result);
    [PreserveSig] int GetAttributesOf(uint count, nint* idLists, uint* attributes);
    [PreserveSig] int GetUIObjectOf(nint owner, uint count, nint* idLists, Guid* iid, uint* reserved, nint* result);
    [PreserveSig] int GetDisplayNameOf(nint idList, uint flags, void* name);
    [PreserveSig] int SetNameOf(nint hwnd, nint idList, char* name, uint flags, nint* newIdList);
}

[GeneratedComInterface]
[Guid("000214f2-0000-0000-c000-000000000046")]
internal unsafe partial interface IEnumIDList
{
    [PreserveSig] int Next(uint count, nint* idLists, uint* fetched);
    [PreserveSig] int Skip(uint count);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(nint* clone);
}

[GeneratedComInterface]
[Guid("000214e4-0000-0000-c000-000000000046")]
internal unsafe partial interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(nint menu, uint index, uint firstCommand, uint lastCommand, uint flags);
    [PreserveSig] int InvokeCommand(Native.Shell32.CMINVOKECOMMANDINFOEX* info);
    [PreserveSig] int GetCommandString(nuint command, uint type, uint* reserved, char* name, uint maxLength);
}

[GeneratedComInterface]
[Guid("000214f4-0000-0000-c000-000000000046")]
internal unsafe partial interface IContextMenu2 : IContextMenu
{
    [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
}

[GeneratedComInterface]
[Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
internal unsafe partial interface IContextMenu3 : IContextMenu2
{
    [PreserveSig] int HandleMenuMsg2(uint message, nint wParam, nint lParam, nint* result);
}
