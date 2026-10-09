using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using NeoShell.Interop.Audio;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;
using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NeoShell.Interop.Notifications;

/// <summary>A notification an app has sent: the texts of its toast, and what else it shows when its XML was found.</summary>
/// <param name="Audio">The sound it asks for; null when its XML wasn't found (it plays the default sound).</param>
/// <param name="Content">Its images, buttons, inputs and the like; null when its XML wasn't found.</param>
public sealed record ToastInfo(
    uint Id, string AppId, string AppName, DateTimeOffset Time, string Title, string Body, ToastAudio? Audio = null, ToastContent? Content = null);

/// <summary>
/// The notifications kept by Windows' notification platform, which stores them whether or not a shell shows them,
/// read and removed through <see cref="UserNotificationListener"/>.
/// </summary>
/// <remarks>
/// An unpackaged app can read them, but not get the listener's <c>NotificationChanged</c> event (it needs package
/// identity), so the caller polls. The listener gives a toast's texts but not its XML: its sound, images, buttons and
/// inputs come from the app's toast history instead, and clicks are carried out by the notification platform's own
/// controller.
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
        var histories = new Dictionary<string, HistoryToast[]>(StringComparer.OrdinalIgnoreCase);
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
            HistoryToast? xml = XmlOf(app.AppUserModelId, title, body, histories);
            ToastContent? content = xml is null ? null : Content(xml, app.AppUserModelId);
            toasts.Add(new ToastInfo(notification.Id, app.AppUserModelId, app.DisplayInfo.DisplayName, notification.CreationTime,
                Localized(content?.Title ?? title, app), Localized(content?.Body ?? body, app),
                xml is null ? null : ToastAudio.Parse(xml.Xml), content));
        }
        return toasts;
    }

    /// <summary>Removes one notification. Ones that have gone already are ignored.</summary>
    public static void Remove(uint id) => UserNotificationListener.Current.RemoveNotification(id);

    /// <summary>
    /// Does what a click on the toast, one of its buttons or one of its menu items does in Explorer, through the
    /// notification platform's controller (as Explorer's toasts do): the app is activated with the toast's or the
    /// button's own arguments and the values of the toast's inputs (a launch, a protocol, a background task, or an
    /// unpackaged app's COM activator), a system button snoozes or dismisses it, and the notification is removed.
    /// Throws on failure. A cross-process call that starts the app, so call it off the UI thread.
    /// </summary>
    /// <param name="invokeId">The button's or menu item's <see cref="ToastAction.InvokeId"/>; null for the toast itself.</param>
    /// <param name="inputs">Each input's ID and value: a text box's text, a selection box's choice.</param>
    public static unsafe void Activate(string appId, uint id, string? invokeId = null, IReadOnlyList<KeyValuePair<string, string>>? inputs = null)
    {
        var controller = Ole32.Create<INotificationController>(NotificationControllers.CLSID_MainController, Ole32.CLSCTX_LOCAL_SERVER);
        // The app the controller starts may take the foreground, as one started from the clicked toast should.
        User32.AllowSetForegroundWindow(ASFW_ANY);
        string item = id.ToString(CultureInfo.InvariantCulture);
        if (invokeId is null && inputs is not { Count: > 0 })
        {
            Marshal.ThrowExceptionForHR(controller.ActivateNotification(appId, item, 0));
            return;
        }

        // Explorer's toasts send a button's ID and every input's value (an empty text box's too).
        var strings = new List<nint>();
        nint Copy(string text)
        {
            nint copy = Marshal.StringToCoTaskMemUni(text);
            strings.Add(copy);
            return copy;
        }
        inputs ??= [];
        var pairs = new NOTIFICATION_USER_INPUT_DATA[inputs.Count];
        try
        {
            for (int i = 0; i < pairs.Length; i++)
                pairs[i] = new NOTIFICATION_USER_INPUT_DATA { Key = Copy(inputs[i].Key), Value = Copy(inputs[i].Value) };
            fixed (NOTIFICATION_USER_INPUT_DATA* first = pairs)
            {
                var data = new NOC_ITEM_ACTIVATION_DATA
                {
                    InvokeId = invokeId is null ? 0 : Copy(invokeId),
                    Inputs = (nint)first,
                    InputCount = (uint)pairs.Length,
                };
                Marshal.ThrowExceptionForHR(controller.ActivateNotification(appId, item, (nint)(&data)));
            }
        }
        finally
        {
            strings.ForEach(Marshal.FreeCoTaskMem);
        }
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

    // A toast in the app's history: its XML and the values its data-bound parts (a progress bar's) show.
    private sealed record HistoryToast(XDocument Xml, IReadOnlyDictionary<string, string>? Data);

    // The toast's XML: the app's toast history (newest first) has it, found by its texts. Read once per app per
    // reading, and only for notifications not read before.
    private static HistoryToast? XmlOf(string appId, string title, string body, Dictionary<string, HistoryToast[]> histories)
    {
        if (!histories.TryGetValue(appId, out HistoryToast[]? history))
            histories[appId] = history = History(appId);
        return history.FirstOrDefault(t => ToastAudio.Texts(t.Xml) == (title, body));
    }

    private static HistoryToast[] History(string appId)
    {
        try
        {
            return [.. ToastNotificationManager.History.GetHistory(appId).Select(Xml).OfType<HistoryToast>()];
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return [];
        }
    }

    // Null for a toast whose XML can't be had: some older ones fail with an XML error (0xC00CE558).
    private static HistoryToast? Xml(ToastNotification toast)
    {
        try
        {
            return new HistoryToast(XDocument.Parse(toast.Content.GetXml()), toast.Data?.Values.ToDictionary(p => p.Key, p => p.Value));
        }
        catch (Exception ex) when (ex is COMException or XmlException)
        {
            return null;
        }
    }

    // Its images are looked for where the notification platform looks for them: in the app's package (only a packaged
    // app with an internet capability may show pictures from the web), or on disk.
    private static ToastContent Content(HistoryToast toast, string appId)
    {
        string? folder = NotificationSound.PackageFolder(appId);
        string? data = NotificationSound.PackageData(appId);
        bool web = folder is not null && AllowsInternet(folder);
        return ToastContent.Parse(toast.Xml, source => ToastContent.LocateImage(source, folder, data, web), toast.Data);
    }

    private static bool AllowsInternet(string packageFolder)
    {
        try
        {
            return XDocument.Load(Path.Combine(packageFolder, "AppxManifest.xml")).Descendants()
                .Any(e => e.Name.LocalName == "Capability" && (string?)e.Attribute("Name") is "internetClient" or "internetClientServer");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return false;
        }
    }

    // A packaged app's toast may name strings of its own resources (the Clock app's "focus session completed"
    // toast does): the listener gives them as written, and Explorer shows them in the user's language.
    private static unsafe string Localized(string text, AppInfo app)
    {
        if (!text.Contains("ms-resource:", StringComparison.Ordinal) || PackageFullName(app.PackageFamilyName) is not { } fullName)
            return text;
        string packageName = fullName[..fullName.IndexOf('_', StringComparison.Ordinal)];
        string[] lines = text.Split('\n');
        char* buffer = stackalloc char[1024];
        for (int i = 0; i < lines.Length; i++)
        {
            if (ResourceUri(lines[i], packageName) is { } uri
                && Shlwapi.SHLoadIndirectString($"@{{{fullName}?{uri}}}", buffer, 1024, 0) == 0)
                lines[i] = new string(buffer);
        }
        return string.Join("\n", lines);
    }

    // AppInfo.Package isn't available to this process (it fails with "specified cast is not valid").
    private static unsafe string? PackageFullName(string familyName)
    {
        if (string.IsNullOrEmpty(familyName))
            return null;
        uint count = 1;
        uint length = 256;
        nint* names = stackalloc nint[1];
        char* buffer = stackalloc char[256];
        return Kernel32.GetPackagesByPackageFamily(familyName, ref count, names, ref length, buffer) == 0 && count > 0
            ? new string((char*)names[0])
            : null;
    }

    /// <summary>
    /// The full <c>ms-resource</c> URI of a toast text naming one of the app's strings, or null for plain text: a bare
    /// name is in the app's Resources, a path is from the package's root.
    /// </summary>
    internal static string? ResourceUri(string text, string packageName)
    {
        const string scheme = "ms-resource:";
        if (!text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return null;
        string reference = text[scheme.Length..];
        if (reference.StartsWith("//", StringComparison.Ordinal))
            return reference.StartsWith("///", StringComparison.Ordinal) ? $"ms-resource://{packageName}/{reference[3..]}" : text;
        return reference.Contains('/') ? $"ms-resource://{packageName}/{reference.TrimStart('/')}" : $"ms-resource://{packageName}/Resources/{reference}";
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
