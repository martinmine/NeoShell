using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// The Radio Management API behind Windows' airplane mode (RmSvc). Undocumented, but unchanged since Windows 8 and
// what airplane mode tools use. Methods NeoShell never calls are declared without parameters.

internal static class RadioManagement
{
    public static readonly Guid CLSID_RadioManagementAPI = new("581333f6-28db-41be-bc7a-ff201f12f3f6");
}

[GeneratedComInterface]
[Guid("db3afbfb-08e6-46c6-aa70-bf9a34c30ab7")]
internal partial interface IRadioManager
{
    [PreserveSig] int IsRMSupported(out int supported);
    [PreserveSig] int GetUIRadioInstances();
    /// <param name="radiosEnabled">0 while airplane mode is on.</param>
    [PreserveSig] int GetSystemRadioState(out int radiosEnabled, out int reserved, out int changeReason);
    [PreserveSig] int SetSystemRadioState(int radiosEnabled);
}
