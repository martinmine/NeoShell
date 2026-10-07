using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;
using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NeoShell.Interop.Notifications;

/// <summary>A notification an app has sent: the texts of its toast, without images or buttons.</summary>
/// <param name="Audio">The sound it asks for; null when its XML wasn't found (it plays the default sound).</param>
public sealed record ToastInfo(uint Id, string AppId, string AppName, DateTimeOffset Time, string Title, string Body, ToastAudio? Audio = null);

/// <summary>
/// The notifications kept by Windows' notification platform, which stores them whether or not a shell shows them,
/// read and removed through <see cref="UserNotificationListener"/>.
/// </summary>
/// <remarks>
/// An unpackaged app can read them, but not get the listener's <c>NotificationChanged</c> event (it needs package
/// identity), so the caller polls. The listener gives a toast's texts but not its XML: the sound comes from the app's
/// toast history instead, and a click is carried out by the notification platform's own controller.
/// </remarks>
public static class UserNotifications
{
    private const uint SPI_GETMESSAGEDURATION = 0x2016;
    private const uint ASFW_ANY = unchecked((uint)-1);

    /// <summary>The notifications there are, or null when the user has denied access to them (Privacy settings).</summary>
    /// <param name="known">Notifications read before, reused as they are: each property of a new one is a call into
    /// the notification service, too slow to repeat for every notification on every poll.</param>
    public static async Task<IReadOnlyList<ToastInfo>?> ReadAsync(IReadOnlyDictionary<uint, ToastInfo> known)
    {
        UserNotificationListener listener = UserNotificationListener.Current;
        if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Unspecified)
            await listener.RequestAccessAsync();
        if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            return null;

        var toasts = new List<ToastInfo>();
        var histories = new Dictionary<string, XDocument[]>(StringComparer.OrdinalIgnoreCase);
        foreach (UserNotification notification in await listener.GetNotificationsAsync(NotificationKinds.Toast))
        {
            if (known.TryGetValue(notification.Id, out ToastInfo? toast))
            {
                toasts.Add(toast);
                continue;
            }
            if (AppOf(notification) is not { } app)
                continue;

            (string title, string body) = Texts(notification.Notification.Visual);
            ToastAudio? audio = AudioOf(app.AppUserModelId, title, body, histories);
            toasts.Add(new ToastInfo(notification.Id, app.AppUserModelId, app.DisplayInfo.DisplayName, notification.CreationTime, title, body, audio));
        }
        return toasts;
    }

    /// <summary>Removes one notification. Ones that have gone already are ignored.</summary>
    public static void Remove(uint id) => UserNotificationListener.Current.RemoveNotification(id);

    /// <summary>
    /// Does what a click on the toast does in Explorer, through the notification platform's controller (as Explorer's
    /// toasts do): the app is activated with the toast's own arguments (its launch arguments, a protocol, a background
    /// task, or an unpackaged app's COM activator) and the notification is removed. Throws on failure. A cross-process
    /// call that starts the app, so call it off the UI thread.
    /// </summary>
    public static void Activate(string appId, uint id)
    {
        var controller = Ole32.Create<INotificationController>(NotificationControllers.CLSID_MainController, Ole32.CLSCTX_LOCAL_SERVER);
        // The app the controller starts may take the foreground, as one started from the clicked toast should.
        User32.AllowSetForegroundWindow(ASFW_ANY);
        Marshal.ThrowExceptionForHR(controller.ActivateNotification(appId, id.ToString(CultureInfo.InvariantCulture), 0));
    }

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

    // Explorer's tray balloons (apps NotifyIconGeneratedAumid_…, banner-only, stored only while their toast shows) have
    // no app the platform can describe: reading theirs throws "not implemented".
    private static AppInfo? AppOf(UserNotification notification)
    {
        try
        {
            return notification.AppInfo;
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }

    // The toast's XML, for its sound: the app's toast history (newest first) has it, found by its texts. Read once per
    // app per reading, and only for notifications not read before.
    private static ToastAudio? AudioOf(string appId, string title, string body, Dictionary<string, XDocument[]> histories)
    {
        if (!histories.TryGetValue(appId, out XDocument[]? history))
            histories[appId] = history = History(appId);
        XDocument? toast = history.FirstOrDefault(t => ToastAudio.Texts(t) == (title, body));
        return toast is null ? null : ToastAudio.Parse(toast);
    }

    private static XDocument[] History(string appId)
    {
        try
        {
            return [.. ToastNotificationManager.History.GetHistory(appId).Select(Xml).OfType<XDocument>()];
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return [];
        }
    }

    // Null for a toast whose XML can't be had: some older ones fail with an XML error (0xC00CE558).
    private static XDocument? Xml(ToastNotification toast)
    {
        try
        {
            return XDocument.Parse(toast.Content.GetXml());
        }
        catch (Exception ex) when (ex is COMException or XmlException)
        {
            return null;
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
