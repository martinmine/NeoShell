using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Which apps are using a capability, as Explorer's privacy indicator learns it (SystemTray.dll,
// PrivacySystemTrayIconDataModel): the Windows Runtime class WindowsUdk.Security.Authorization.AppCapabilityAccess.
// CapabilityUsageInfo in windowsudk.shellcommon.dll, in-process, over camsvc's CapabilityUsage and its WNF state.
// Undocumented and without metadata: the IIDs and method order are from the DLL's symbols (Windows 11 25H2).

[GeneratedComInterface]
[Guid("2135ec12-5eb8-5f7b-89a3-dbe27b6cebc7")]
internal partial interface ICapabilityUsageInfoFactory : IInspectable
{
    /// <param name="capability">The capability's name as an HSTRING: "microphone", "location"…</param>
    [PreserveSig] int CreateInstance(nint capability, out ICapabilityUsageInfo info);
}

[GeneratedComInterface]
[Guid("d494ab35-5e4a-5533-a0a6-ac684a2feea2")]
internal partial interface ICapabilityUsageInfo : IInspectable
{
    [PreserveSig] int IsAnyAppUsingCapability([MarshalAs(UnmanagedType.U1)] out bool inUse);

    /// <param name="names">An <c>IVectorView&lt;String&gt;</c> of the apps' display names, in Windows' order.</param>
    [PreserveSig] int GetDisplayNamesForAppsUsingCapability(out IStringVectorView names);

    [PreserveSig] int AddUsageChanged(ICapabilityUsageChangedHandler handler, out long token);

    [PreserveSig] int RemoveUsageChanged(long token);
}

/// <summary><c>IVectorView&lt;String&gt;</c>.</summary>
[GeneratedComInterface]
[Guid("2f13c006-a03a-5f69-b090-75a43e33423e")]
internal partial interface IStringVectorView : IInspectable
{
    /// <param name="value">An HSTRING the caller deletes.</param>
    [PreserveSig] int GetAt(uint index, out nint value);

    [PreserveSig] int GetSize(out uint size);
}

/// <summary><c>TypedEventHandler&lt;CapabilityUsageInfo, Object&gt;</c>.</summary>
[GeneratedComInterface]
[Guid("48a0f8bb-b057-5aae-8439-e917492e4e88")]
internal partial interface ICapabilityUsageChangedHandler
{
    [PreserveSig] int Invoke(nint sender, nint args);
}
