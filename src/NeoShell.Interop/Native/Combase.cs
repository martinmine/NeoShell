using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

/// <summary>Activation of Windows Runtime classes that have no projection (Windows' own, undocumented ones).</summary>
internal static partial class Combase
{
    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WindowsCreateString(string source, uint length, out nint hstring);

    [LibraryImport("combase.dll")]
    public static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    public static partial int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);

    /// <summary>The activation factory of a Windows Runtime class as <typeparamref name="T"/>, or throws.</summary>
    public static T GetActivationFactory<T>(string className)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, (uint)className.Length, out nint name));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, typeof(T).GUID, out nint factory));
            return Com.ComPointer.TakeOwnership<T>(factory);
        }
        finally
        {
            WindowsDeleteString(name);
        }
    }
}
