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
    /// <summary>On or off; null when quiet hours aren't available.</summary>
    public static bool? Read()
    {
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

    private static IQuietHoursSettings Create() => Ole32.Create<IQuietHoursSettings>(QuietHours.CLSID_QuietHoursSettings, CoreAudio.CLSCTX_ALL);
}
