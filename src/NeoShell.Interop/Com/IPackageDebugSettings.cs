using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Package process control (shobjidl_core.h). Methods NeoShell never calls are declared without parameters: only their
// place in the vtable matters; the ones after the last called method are left out.

[GeneratedComInterface]
[Guid("f27c3930-8029-4ad1-94e3-3dba417810c1")]
internal partial interface IPackageDebugSettings
{
    [PreserveSig] int EnableDebugging();
    [PreserveSig] int DisableDebugging();
    [PreserveSig] int Suspend();
    [PreserveSig] int Resume();

    [PreserveSig]
    int TerminateAllProcesses([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);
}
