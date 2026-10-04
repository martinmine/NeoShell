using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

internal static unsafe class ComPointer
{
    /// <summary>Wraps a COM pointer we were handed and drops our own reference: the wrapper holds one now.</summary>
    public static T TakeOwnership<T>(nint pointer)
    {
        T wrapper = ComInterfaceMarshaller<T>.ConvertToManaged((void*)pointer)!;
        Marshal.Release(pointer);
        return wrapper;
    }
}
