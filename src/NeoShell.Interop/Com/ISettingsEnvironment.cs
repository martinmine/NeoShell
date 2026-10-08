using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Settings' environment: Windows' rules for which settings, Settings pages and groups this PC has (a VPN set up, a
// Wi-Fi adapter for the hotspot, radios for airplane mode). SettingsEnvironment.Desktop.dll exports
// GetDesktopSettingsEnvironment(ISettingsEnvironment**), which SystemSettings.DataModel's environment database uses
// (through shell32's GetSettingsEnvironmentInstance) and so ShellHost's Quick Settings. Undocumented and without
// metadata: the IID is the object's own (GetIids), the method order from the DLL's symbols (Windows 11 25H2).

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0adb9837-6628-48d2-ad8a-3c138cdc9b62")]
internal partial interface ISettingsEnvironment : IInspectable
{
    // The C++ class's virtual destructor sits in this slot; never called.
    void Destructor();

    /// <summary>Unknown IDs count as applicable; some rules are only known a moment after the first question.</summary>
    [PreserveSig] int IsApplicable(string id, out byte applicable);
}
