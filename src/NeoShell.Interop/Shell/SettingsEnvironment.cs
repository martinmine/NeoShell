using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32;
using NeoShell.Interop.Com;

namespace NeoShell.Interop.Shell;

/// <summary>
/// Windows' rules for which settings this PC has, as Quick Settings applies them to its quick actions: a quick action
/// shows only while its setting, its Settings page and its group are all applicable here (QuickActionsDataModel's
/// <c>QuickSetting::get_IsApplicable</c>). That hides the VPN without a VPN set up, the hotspot without Wi-Fi and
/// airplane mode without radios, which their own handlers count as applicable.
/// </summary>
/// <remarks>
/// Free-threaded. Some rules are worked out in the background the first time they're asked about (the VPN's says
/// "no" at first and "yes" a few seconds later), so ask again whenever Quick Settings opens.
/// </remarks>
public sealed class SettingsEnvironment : IDisposable
{
    private readonly ISettingsEnvironment _environment;
    private bool _disposed;

    private SettingsEnvironment(ISettingsEnvironment environment) => _environment = environment;

    /// <summary>Windows' settings environment; null when this Windows has none.</summary>
    public static unsafe SettingsEnvironment? Open()
    {
        string path = Path.Combine(Environment.SystemDirectory, "SettingsEnvironment.Desktop.dll");
        if (!NativeLibrary.TryLoad(path, out nint library)
            || !NativeLibrary.TryGetExport(library, "GetDesktopSettingsEnvironment", out nint export))
        {
            return null;
        }

        nint environment;
        if (((delegate* unmanaged<nint*, int>)export)(&environment) < 0 || environment == 0)
            return null;
        return new SettingsEnvironment(ComPointer.TakeOwnershipUnique<ISettingsEnvironment>(environment));
    }

    /// <summary>
    /// Whether the quick action of this setting ("SystemSettings_Network_VPN_QuickAction") applies here: the setting
    /// and the Settings page and group it's registered with (HKLM\…\ActionCenter\Quick Actions\All\&lt;id&gt;).
    /// </summary>
    public bool IsQuickActionApplicable(string settingId)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\ActionCenter\Quick Actions\All\{settingId}");
        return IsApplicable(settingId)
            && (key?.GetValue("Page") is not string { Length: > 0 } page || IsApplicable(page))
            && (key?.GetValue("Group") is not string { Length: > 0 } group || IsApplicable(group));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ((ComObject)(object)_environment).FinalRelease();
    }

    // As Quick Settings: an ID the environment doesn't know (TYPE_E_ELEMENTNOTFOUND) applies, one it fails on doesn't.
    private bool IsApplicable(string id)
    {
        int result = _environment.IsApplicable(id, out byte applicable);
        return result == unchecked((int)0x8002802B) || result >= 0 && applicable != 0;
    }
}
