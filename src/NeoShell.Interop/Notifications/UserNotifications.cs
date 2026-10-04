using NeoShell.Interop.Native;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NeoShell.Interop.Notifications;

/// <summary>A notification an app has sent: the texts of its toast, without images, buttons or launch arguments.</summary>
public sealed record ToastInfo(uint Id, string AppId, string AppName, DateTimeOffset Time, string Title, string Body);

/// <summary>
/// The notifications kept by Windows' notification platform, which stores them whether or not a shell shows them,
/// read and removed through <see cref="UserNotificationListener"/>.
/// </summary>
/// <remarks>
/// An unpackaged app can read them, but not get the listener's <c>NotificationChanged</c> event (it needs package
/// identity), so the caller polls. The listener exposes no activation: there's no way to hand a toast's launch
/// arguments or buttons back to its app.
/// </remarks>
public static class UserNotifications
{
    private const uint SPI_GETMESSAGEDURATION = 0x2016;

    /// <summary>The notifications there are, or null when the user has denied access to them (Privacy settings).</summary>
    public static async Task<IReadOnlyList<ToastInfo>?> ReadAsync()
    {
        UserNotificationListener listener = UserNotificationListener.Current;
        if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Unspecified)
            await listener.RequestAccessAsync();
        if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            return null;

        var toasts = new List<ToastInfo>();
        foreach (UserNotification notification in await listener.GetNotificationsAsync(NotificationKinds.Toast))
        {
            if (notification.AppInfo is not { } app)
                continue;

            (string title, string body) = Texts(notification.Notification.Visual);
            toasts.Add(new ToastInfo(notification.Id, app.AppUserModelId, app.DisplayInfo.DisplayName, notification.CreationTime, title, body));
        }
        return toasts;
    }

    /// <summary>Removes one notification. Ones that have gone already are ignored.</summary>
    public static void Remove(uint id) => UserNotificationListener.Current.RemoveNotification(id);

    /// <summary>
    /// How long a toast stays on screen: Settings → Accessibility → Visual effects → "Dismiss notifications after
    /// this amount of time" (5 seconds unless changed).
    /// </summary>
    public static unsafe TimeSpan PopupDuration
    {
        get
        {
            uint seconds = 0;
            return User32.SystemParametersInfo(SPI_GETMESSAGEDURATION, 0, &seconds, 0) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromSeconds(5);
        }
    }

    // The first text is the title, the rest the body. Toasts in the old templates (ToastText02 and so on) come
    // through as ToastGeneric too.
    private static (string Title, string Body) Texts(NotificationVisual visual)
    {
        NotificationBinding? binding = visual.GetBinding(KnownNotificationBindings.ToastGeneric) ?? visual.Bindings.FirstOrDefault();
        string[] texts = binding is null ? [] : [.. binding.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t))];
        return texts.Length == 0 ? ("", "") : (texts[0], string.Join("\n", texts.Skip(1)));
    }
}
