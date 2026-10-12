using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Notifications;

/// <summary>
/// Windows 11's Do not disturb: notifications go straight to the notification center without a toast, except
/// priority ones. There's no public API; this switches the quiet hours profile as Explorer's bell button does.
/// </summary>
public static class DoNotDisturb
{
    // The profile in force (0 everything shows, else priority only or alarms only), published by the notification
    // platform: it also counts the quiet moments a focus session or an automatic rule turns on without touching the
    // user's choice. Explorer's bell and notification center show Do not disturb on for those too.
    internal const ulong WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED = 0x0D83063EA3BF1C75;

    /// <summary>On or off; null when quiet hours aren't available.</summary>
    public static bool? Read()
    {
        if (ReadActiveProfile() is > 0)
            return true;
        try
        {
            IQuietHoursSettings settings = Create();
            if (settings.GetUserSelectedProfile(out nint profile) != 0)
                return null;
            try
            {
                return Marshal.PtrToStringUni(profile) != QuietHours.Unrestricted;
            }
            finally
            {
                Marshal.FreeCoTaskMem(profile);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Throws on failure.</summary>
    public static void Set(bool on) =>
        Marshal.ThrowExceptionForHR(Create().SetUserSelectedProfile(on ? QuietHours.PriorityOnly : QuietHours.Unrestricted));

    private static unsafe int? ReadActiveProfile()
    {
        int profile = 0;
        uint length = sizeof(int);
        int status = Ntdll.NtQueryWnfStateData(WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED, 0, 0, out _, &profile, ref length);
        return status >= 0 && length == sizeof(int) ? profile : null;
    }

    private static IQuietHoursSettings Create() => Ole32.Create<IQuietHoursSettings>(QuietHours.CLSID_QuietHoursSettings, CoreAudio.CLSCTX_ALL);
}
