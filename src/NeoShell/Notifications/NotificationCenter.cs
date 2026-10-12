using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Notifications;
using NeoShell.Interop.Shell;
using NeoShell.Logging;

namespace NeoShell.Notifications;

/// <summary>
/// The notifications Windows keeps and the state of Do not disturb, read whenever the notification platform says they
/// changed (an unpackaged app gets no change event from the listener), or once a second if it can't. UI thread only.
/// </summary>
internal sealed class NotificationCenter : IDisposable
{
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _timer;
    private readonly Dictionary<string, Task<ImageSource?>> _logos = new(StringComparer.OrdinalIgnoreCase);
    private NotificationChanges? _changes;
    // Null until the first reading.
    private Dictionary<uint, ToastInfo>? _known;
    private bool _reading;
    // A change came during a reading, which may have missed it.
    private bool _readAgain;

    public NotificationCenter()
    {
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Read();
    }

    /// <summary>The notifications the notification center shows, in no particular order.</summary>
    public IReadOnlyList<ToastInfo> Toasts { get; private set; } = [];

    public bool DoNotDisturb { get; private set; }

    /// <summary>
    /// How many notifications came since a notification center (NeoShell's or Explorer's) was last open, as the
    /// notification platform counts them for Explorer's bell.
    /// </summary>
    public int NewCount { get; private set; }

    /// <summary>Raised when notifications come or go.</summary>
    public event Action? Changed;

    /// <summary>A new notification that should show as a toast (not while Do not disturb is on).</summary>
    public event Action<ToastInfo>? Arrived;

    public event Action? DoNotDisturbChanged;

    /// <summary>Do not disturb or the count of new notifications changed: what the clock's bell shows.</summary>
    public event Action? BellChanged;

    public void Start()
    {
        try
        {
            _changes = new NotificationChanges();
            _changes.Changed += () => _dispatcher.TryEnqueue(Read);
        }
        catch (InvalidOperationException ex)
        {
            Log.Warn("Polling notifications", ex);
            _timer.Start();
        }
        Read();
    }

    public void Dispose()
    {
        _timer.Stop();
        _changes?.Dispose();
    }

    /// <summary>Whether Windows still has the notification, whether the notification center shows it or not.</summary>
    public bool IsStored(uint id) => _known?.ContainsKey(id) == true;

    public void Remove(IEnumerable<ToastInfo> toasts)
    {
        foreach (ToastInfo toast in toasts.ToList())
        {
            try
            {
                UserNotifications.Remove(toast.Id);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not remove notification {toast.Id} of {toast.AppId}", ex);
            }
        }
        Read();
    }

    /// <summary>
    /// Delivers a click on the notification, or on one of its buttons or menu items, to its app as Explorer does: the
    /// notification platform activates the app with the toast's or the button's own arguments and the inputs' values
    /// (or snoozes or dismisses it, for a system button) and removes the notification. If a click on the
    /// notification itself fails, the app opens as from Start.
    /// </summary>
    /// <param name="action">The button or menu item; null for the notification itself.</param>
    /// <param name="inputs">Every input's ID and value.</param>
    public async void Activate(ToastInfo toast, ToastAction? action = null, IReadOnlyList<KeyValuePair<string, string>>? inputs = null)
    {
        try
        {
            await Task.Run(() => UserNotifications.Activate(toast.AppId, toast.Id, action?.InvokeId, inputs));
            Log.Info($"Activated notification {toast.Id} of {toast.AppId}" + (action is null ? "" : $" with {action.InvokeId}"));
            return;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not activate notification {toast.Id} of {toast.AppId}", ex);
        }
        if (action is not null)
            return;
        NotificationPanel.Open(toast);
        Remove([toast]);
    }

    /// <summary>The sound the toast plays, if any, by the user's settings (see <see cref="ToastSounds"/>).</summary>
    public static ToastSound? SoundFor(ToastInfo toast)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        using RegistryKey? app = key?.OpenSubKey(toast.AppId);
        bool allowed = key?.GetValue("NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND") is not int value || value != 0;
        return ToastSounds.Choose(toast.Audio, allowed, app?.GetValue("SoundFile") as string);
    }

    public void SetDoNotDisturb(bool on)
    {
        try
        {
            Interop.Notifications.DoNotDisturb.Set(on);
            DoNotDisturb = on;
            DoNotDisturbChanged?.Invoke();
            BellChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not switch Do not disturb", ex);
        }
    }

    /// <summary>
    /// Tells the notification platform the notification center opened or closed, as Explorer's does: either marks
    /// every notification seen, which clears the count of new ones (Explorer's bell too).
    /// </summary>
    public async void SetOpen(bool open)
    {
        try
        {
            await Task.Run(() => NewNotifications.SetCenterOpen(open));
        }
        catch (Exception ex)
        {
            Log.Warn("Could not tell the notification platform the notification center " + (open ? "opened" : "closed"), ex);
        }
        Read();
    }

    /// <summary>Tells the notification platform the notification center closed, on this thread.</summary>
    public static void TellClosed()
    {
        try
        {
            NewNotifications.SetCenterOpen(false);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not tell the notification platform the notification center closed", ex);
        }
    }

    /// <summary>
    /// Turns off all of an app's notifications, as the toggle in Settings → System → Notifications (whose own
    /// store is this registry key).
    /// </summary>
    /// <remarks>
    /// Windows' notification platform only notices the value later (Settings tells it through a private channel), so
    /// NeoShell hides the app's notifications itself meanwhile; Explorer's toasts still show them until then.
    /// </remarks>
    public void TurnOff(string appId)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey($@"{SettingsKey}\{appId}");
        key.SetValue("Enabled", 0, RegistryValueKind.DWord);
        Remove(Toasts.Where(t => string.Equals(t.AppId, appId, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The app's logo for the group header and toast, loaded once per app.</summary>
    public Task<ImageSource?> GetLogoAsync(string appId)
    {
        if (!_logos.TryGetValue(appId, out Task<ImageSource?>? logo))
            _logos[appId] = logo = LoadLogoAsync(appId);
        return logo;
    }

    // As the taskbar's icons: a packaged app's own logo, otherwise its Start menu entry's icon. 16 effective pixels,
    // sharp up to 200 %. An app registered only for its toasts, without a Start menu entry, has the picture it gave
    // for them (AppUserModelId\<AUMID>\IconUri), as in Explorer's toasts.
    private static async Task<ImageSource?> LoadLogoAsync(string appId)
    {
        const int size = 32;
        try
        {
            IconBitmap? icon = await Task.Run(() => PackagedApps.GetLogo(appId, size) ?? ShellItems.GetIcon(ShellItems.AppsFolderPath(appId), size));
            if (icon is not null)
                return AppIcons.ToImageSource(icon);
            return ToastIconFile(appId) is { } file ? new BitmapImage(new Uri(file)) { DecodePixelWidth = size } : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not load the logo of {appId}", ex);
            return null;
        }
    }

    private static string? ToastIconFile(string appId)
    {
        foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using RegistryKey? key = root.OpenSubKey($@"Software\Classes\AppUserModelId\{appId}");
            if (key?.GetValue("IconUri") is string file && Path.IsPathFullyQualified(file) && File.Exists(file))
                return file;
        }
        return null;
    }

    private async void Read()
    {
        if (_reading)
        {
            _readAgain = true;
            return;
        }

        _reading = true;
        _readAgain = false;
        try
        {
            // Off the UI thread: both are calls into other processes, which can take a while to answer. _known isn't
            // changed meanwhile, as only one reading runs at a time.
            Dictionary<uint, ToastInfo> known = _known ?? [];
            (bool doNotDisturb, int? newCount, IReadOnlyList<ToastInfo>? all) = await Task.Run(async () =>
                (Interop.Notifications.DoNotDisturb.Read() == true, NewNotifications.ReadCount(), await UserNotifications.ReadAsync(known)));

            bool bellChanged = doNotDisturb != DoNotDisturb || (newCount is not null && newCount != NewCount);
            NewCount = newCount ?? NewCount;
            if (doNotDisturb != DoNotDisturb)
            {
                DoNotDisturb = doNotDisturb;
                DoNotDisturbChanged?.Invoke();
            }
            if (bellChanged)
                BellChanged?.Invoke();

            if (all is null)
                return;
            if (_known is not null && all.Count == _known.Count && all.All(t => _known.ContainsKey(t.Id)))
                return;

            // The first reading is what was there before NeoShell started; those don't pop up.
            List<ToastInfo> arrived = _known is null ? [] : [.. all.Where(t => !_known.ContainsKey(t.Id)).OrderBy(t => t.Time)];
            _known = all.ToDictionary(t => t.Id);
            Toasts = [.. all.Where(t => ReadSetting(t.AppId, "Enabled") && ReadSetting(t.AppId, "ShowInActionCenter"))];
            Changed?.Invoke();

            foreach (ToastInfo toast in arrived.Where(t => ShowsBanner(t.AppId)))
                Arrived?.Invoke(toast);
        }
        catch (Exception ex)
        {
            Log.Warn("Reading notifications failed", ex);
        }
        finally
        {
            _reading = false;
            if (_readAgain)
                Read();
        }
    }

    /// <summary>
    /// Whether a new notification of the app pops up as a toast: not while Do not disturb is on, nor when the app's
    /// notifications or banners, or all banners, are turned off.
    /// </summary>
    public bool ShowsBanner(string appId) =>
        !DoNotDisturb && ToastsEnabled() && ReadSetting(appId, "Enabled") && ReadSetting(appId, "ShowBanner");

    // Settings → System → Notifications, per app: on unless set to 0.
    private static bool ReadSetting(string appId, string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{SettingsKey}\{appId}");
        return key?.GetValue(name) is not int value || value != 0;
    }

    // "Show notification banners" for every app (off in Settings → System → Notifications → Notifications).
    private static bool ToastsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications");
        return key?.GetValue("ToastEnabled") is not int value || value != 0;
    }
}
