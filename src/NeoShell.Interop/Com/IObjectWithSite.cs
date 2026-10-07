using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// A site for an object to ask services of (ocidl.h, servprov.h). The methods after the last called one are left out.

[GeneratedComInterface]
[Guid("fc4801a3-2ba9-11cf-a229-00aa003d7352")]
internal partial interface IObjectWithSite
{
    [PreserveSig]
    int SetSite(nint site);
}

// IServiceProvider, named apart from System.IServiceProvider.
[GeneratedComInterface]
[Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
internal unsafe partial interface IOleServiceProvider
{
    [PreserveSig]
    int QueryService(Guid* service, Guid* iid, nint* result);
}
