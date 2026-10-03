using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Com;

[GeneratedComInterface]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
internal unsafe partial interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, Ole32.PROPERTYKEY* key);
    [PreserveSig] int GetValue(Ole32.PROPERTYKEY* key, Ole32.PROPVARIANT* value);
    [PreserveSig] int SetValue(Ole32.PROPERTYKEY* key, Ole32.PROPVARIANT* value);
    [PreserveSig] int Commit();
}
