using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Notifications;

/// <summary>
/// The notifications that came since a notification center was last open, which Explorer's bell counts. The
/// notification platform keeps the count (WpnUserService's <c>IndicatorController</c>) and publishes it in the WNF
/// state <c>WNF_SHEL_NOTIFICATIONS</c>, where Explorer's taskbar reads it; a notification center opening or closing
/// (<c>SetNocenterStatus</c>, 1 and 0 as Explorer's) marks them all seen.
/// </summary>
public static class NewNotifications
{
    internal const ulong WNF_SHEL_NOTIFICATIONS = 0x0D83063EA3BC1035;

    /// <summary>The count, or null if it can't be read.</summary>
    public static unsafe int? ReadCount()
    {
        uint count = 0;
        uint length = sizeof(uint);
        int status = Ntdll.NtQueryWnfStateData(WNF_SHEL_NOTIFICATIONS, 0, 0, out _, &count, ref length);
        return status >= 0 ? (length == sizeof(uint) ? (int)count : 0) : null;
    }

    /// <summary>
    /// Tells the notification platform a notification center opened or closed; either marks every notification seen.
    /// A cross-process call; throws on failure.
    /// </summary>
    public static void SetCenterOpen(bool open)
    {
        var controller = Ole32.Create<INotificationController>(NotificationControllers.CLSID_MainController, Ole32.CLSCTX_LOCAL_SERVER);
        Marshal.ThrowExceptionForHR(controller.SetNocenterStatus(open ? 1 : 0));
    }
}
