using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal unsafe partial interface IShellItem
{
    [PreserveSig] int BindToHandler(nint bindContext, Guid* handler, Guid* iid, nint* result);
    [PreserveSig] int GetParent(nint* parent);
    [PreserveSig] int GetDisplayName(uint sigdn, char** name);
    [PreserveSig] int GetAttributes(uint mask, uint* attributes);
    [PreserveSig] int Compare(nint other, uint hint, int* order);
}

[GeneratedComInterface]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
internal partial interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(Native.User32.SIZE size, uint flags, out nint bitmap);
}
