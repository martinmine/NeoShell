using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Packaged app activation (shobjidl_core.h). Methods NeoShell never calls are declared without parameters: only their
// place in the vtable matters.

[GeneratedComInterface]
[Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
internal partial interface IApplicationActivationManager
{
    [PreserveSig]
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
        int options,
        out uint processId);

    [PreserveSig] int ActivateForFile();
    [PreserveSig] int ActivateForProtocol();
}
