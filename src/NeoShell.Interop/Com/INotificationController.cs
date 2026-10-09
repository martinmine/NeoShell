using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// The notification platform's controller in WpnUserService (NotificationController.dll), which Explorer's toasts and
// notification center (ShellExperienceHost's Windows.UI.ActionCenter) drive: it hands them the toasts and carries out
// what the user does with them. Undocumented; the method order is MainControllerImpl's vtable (Windows 11 25H2).
// Only SetNocenterStatus and ActivateNotification are called; the other methods are declared to keep their slots.

internal static class NotificationControllers
{
    /// <summary>CLSID_MainController, served by WpnUserService (AppID "MainController App ID").</summary>
    public static readonly Guid CLSID_MainController = new("1ffe4ffd-25b1-40b1-a1ea-ef633353bb4e");
}

[GeneratedComInterface]
[Guid("2537d644-8c2f-4449-b8b6-10928822630c")]
internal partial interface INotificationController
{
    [PreserveSig] int SaveNotificationDraft(nint group, nint item, nint data);
    [PreserveSig] int RegisterDataSink(nint sink, out uint cookie, int kind);
    [PreserveSig] int RegisterToastSink(nint sink, out uint cookie, uint flags, nint name, int kind);
    [PreserveSig] int RegisterToastFilterSink(nint sink, out uint cookie);
    [PreserveSig] int RegisterBadgeSink(nint sink, out uint cookie, uint flags, int kind);
    [PreserveSig] int UnregisterSink(uint cookie);

    /// <summary>The notification center opened (1) or closed (0); a change marks every notification seen.</summary>
    [PreserveSig]
    int SetNocenterStatus(int status);

    [PreserveSig] int ToastReportStatus(nint group, nint item, int status);
    [PreserveSig] int DeleteNotifications(nint pairs, uint count);
    [PreserveSig] int ActivateNotificationGroup(nint group);

    /// <summary>
    /// Does what a click on the notification does: activates its app with the toast's arguments (launch, protocol,
    /// background, or an unpackaged app's COM activator) and removes the notification.
    /// </summary>
    /// <param name="group">The app's AppUserModelID.</param>
    /// <param name="item">The notification's ID (<c>UserNotification.Id</c>) in decimal.</param>
    /// <param name="data">A <see cref="NOC_ITEM_ACTIVATION_DATA"/> for a button, a menu item or inputs; 0 for a click on
    /// the toast's body.</param>
    [PreserveSig]
    int ActivateNotification(
        [MarshalAs(UnmanagedType.LPWStr)] string group, [MarshalAs(UnmanagedType.LPWStr)] string item, nint data);
}

/// <summary>
/// What the user did with a toast, as Explorer's toasts fill it in (ShellExperienceHost's
/// <c>NotificationItemActivationData</c>; layout read from its setters and the controller's
/// <c>FindUserInputValueByKey</c>). The proxy marshals it, strings and arrays included.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NOC_ITEM_ACTIVATION_DATA
{
    /// <summary>
    /// Which button or menu item: "&lt;" and its place among the toast's actions, or the input ID for the button beside a
    /// text box; null for the toast itself.
    /// </summary>
    public nint InvokeId;

    /// <summary>A <see cref="NOTIFICATION_USER_INPUT_DATA"/> array: every input's ID and value.</summary>
    public nint Inputs;

    public uint InputCount;

    /// <summary>The same pairs for hints of the shell's own; Explorer's toasts send none.</summary>
    public nint SystemHints;

    public uint SystemHintCount;

    public uint CallerWindowId;
}

/// <summary>An input's ID and value, as an unpackaged app's <c>INotificationActivationCallback</c> receives them.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NOTIFICATION_USER_INPUT_DATA
{
    public nint Key;
    public nint Value;
}
