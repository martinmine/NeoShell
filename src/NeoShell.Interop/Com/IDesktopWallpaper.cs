using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

/// <summary>
/// The wallpaper API apps and Settings call on <c>CLSID_DesktopWallpaper</c>. Explorer's desktop serves it; as the
/// shell NeoShell does. Strings are raw so the server controls their allocation (returned ones are CoTaskMem).
/// </summary>
[GeneratedComInterface]
[Guid("b92b56a9-8b55-4e14-9a89-0199bbb6f93b")]
internal unsafe partial interface IDesktopWallpaper
{
    [PreserveSig] int SetWallpaper(char* monitorId, char* wallpaper);
    [PreserveSig] int GetWallpaper(char* monitorId, char** wallpaper);
    [PreserveSig] int GetMonitorDevicePathAt(uint monitorIndex, char** monitorId);
    [PreserveSig] int GetMonitorDevicePathCount(uint* count);
    [PreserveSig] int GetMonitorRECT(char* monitorId, Native.User32.RECT* displayRect);
    [PreserveSig] int SetBackgroundColor(uint color);
    [PreserveSig] int GetBackgroundColor(uint* color);
    [PreserveSig] int SetPosition(int position);
    [PreserveSig] int GetPosition(int* position);
    [PreserveSig] int SetSlideshow(nint items);
    [PreserveSig] int GetSlideshow(nint* items);
    [PreserveSig] int SetSlideshowOptions(int options, uint slideshowTick);
    [PreserveSig] int GetSlideshowOptions(int* options, uint* slideshowTick);
    [PreserveSig] int AdvanceSlideshow(char* monitorId, int direction);
    [PreserveSig] int GetStatus(int* state);
    [PreserveSig] int Enable(int enable);
}

[GeneratedComInterface]
[Guid("00000001-0000-0000-c000-000000000046")]
internal unsafe partial interface IClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, Guid* iid, nint* instance);
    [PreserveSig] int LockServer(int doLock);
}

[GeneratedComInterface]
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
internal unsafe partial interface IShellItemArray
{
    [PreserveSig] int BindToHandler(nint bindContext, Guid* handler, Guid* iid, nint* result);
    [PreserveSig] int GetPropertyStore(int flags, Guid* iid, nint* store);
    [PreserveSig] int GetPropertyDescriptionList(nint keyType, Guid* iid, nint* list);
    [PreserveSig] int GetAttributes(int attributeFlags, uint mask, uint* attributes);
    [PreserveSig] int GetCount(uint* count);
    [PreserveSig] int GetItemAt(uint index, nint* item);
    [PreserveSig] int EnumItems(nint* items);
}
