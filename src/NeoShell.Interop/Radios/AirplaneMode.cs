using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Radios;

/// <summary>
/// Airplane mode: every radio off at once, as Windows' own switch does it, through the Radio Management API (an
/// undocumented COM interface of the radio management service; there is no public one).
/// </summary>
public static class AirplaneMode
{
    /// <summary>On or off; null when the PC has no radio management.</summary>
    public static bool? Read()
    {
        try
        {
            IRadioManager manager = Create();
            if (manager.IsRMSupported(out int supported) != 0 || supported == 0)
                return null;
            return manager.GetSystemRadioState(out int radiosEnabled, out _, out _) == 0 ? radiosEnabled == 0 : null;
        }
        catch (COMException)
        {
            return null; // the service is stopped or missing
        }
    }

    /// <summary>Throws on failure.</summary>
    public static void Set(bool on) => Marshal.ThrowExceptionForHR(Create().SetSystemRadioState(on ? 0 : 1));

    private static IRadioManager Create() => Ole32.Create<IRadioManager>(RadioManagement.CLSID_RadioManagementAPI, CoreAudio.CLSCTX_ALL);
}
