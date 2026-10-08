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

    /// <summary>
    /// As <see cref="TakeOwnership"/>, but always a wrapper of its own, never the one cached for an object that was at
    /// the same address: for wrappers released early with <see cref="ComObject.FinalRelease"/>, whose cache entries
    /// outlive their objects (handed out for a new object, one would drop that object's reference instead).
    /// </summary>
    public static T TakeOwnershipUnique<T>(nint pointer)
    {
        var wrapper = (T)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
        Marshal.Release(pointer);
        return wrapper;
    }

    private static readonly StrategyBasedComWrappers Wrappers = new();
}
