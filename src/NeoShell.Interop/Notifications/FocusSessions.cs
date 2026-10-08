using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;
using NeoShell.Interop.Shell;

namespace NeoShell.Interop.Notifications;

/// <summary>What a focus session does (Settings → System → Focus); all on unless the user turned them off.</summary>
public sealed record FocusSettings(bool ClockTimer, bool HideBadges, bool HideFlashing, bool DoNotDisturb);

/// <summary>
/// Windows' own focus sessions, the ones Explorer's calendar, Settings and the Clock app start: while one runs, the
/// focus settings are in force — the Clock app's timer, no taskbar badges or flashing, Do not disturb — and Windows
/// puts them back when it ends.
/// </summary>
/// <remarks>
/// The public <c>Windows.UI.Shell.FocusSessionManager</c> only starts and ends sessions for Microsoft's apps (a limited
/// access feature); it hands them to an undocumented manager in Explorer, which NeoShell calls as it does. That manager
/// applies the settings itself: it writes <c>TaskbarBadges</c> and <c>TaskbarFlashing</c> (Explorer\Advanced, which
/// the taskbar follows), turns on the notification platform's focus quiet moment and starts or stops the Clock app's
/// timer (<c>ms-clock:</c>). The sessions live in the cloud store (in the registry), so a session started anywhere shows
/// here: <see cref="Changed"/> follows its key. Without Explorer the manager can't be used: it starts the Clock app
/// with <c>ShellExecuteEx</c>, which fails without Explorer and shows an error box.
/// </remarks>
public sealed class FocusSessions : IDisposable
{
    private const string SessionsKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\" +
        @"default$windows.data.shell.focusactivesessions\windows.data.shell.focusactivesessions";

    // This process's own manager: for the settings, and for sessions while Explorer restarts.
    private readonly IFocusSessionThemeManager? _own;
    private RegistryWatcher? _watcher;

    public FocusSessions()
    {
        _own = OnThreadPool(() =>
        {
            try
            {
                Combase.GetActivationFactory<IFocusSessionThemeManagerStatics>("Windows.Internal.Shell.FocusSessionThemeManager")
                    .GetDefault(out IFocusSessionThemeManager own);
                return own;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                // Not on this build of Windows.
                return null;
            }
        });
        Watch();
    }

    /// <summary>A session started or ended, here or elsewhere. Raised on a thread-pool thread.</summary>
    public event Action? Changed;

    public bool IsAvailable => _own is not null;

    /// <summary>Whether a session runs now; false when focus sessions aren't available.</summary>
    public bool IsActive => OnThreadPool(() =>
        Manager() is { } manager
        && manager.GetCurrentThemeId(out Guid current) == 0
        && manager.GetOffThemeId(out Guid off) == 0
        && current != off);

    /// <summary>Starts a session with the focus settings, as Explorer's calendar does. Throws on failure.</summary>
    public void Start(TimeSpan length)
    {
        OnThreadPool(() =>
        {
            IFocusSessionThemeManager manager = Manager() ?? throw new InvalidOperationException("Focus sessions aren't available");
            Marshal.ThrowExceptionForHR(manager.GetDefaultThemeId(out Guid theme));
            Marshal.ThrowExceptionForHR(manager.AddSession(theme, DateTime.UtcNow.Add(length).ToFileTimeUtc(), out _));
            return true;
        });
        // The sessions' key only exists once a session was ever started.
        Watch();
    }

    /// <summary>Ends every running session, as Explorer's End session does. Throws on failure.</summary>
    public void End() => OnThreadPool(() =>
    {
        if (Manager() is { } manager)
            Marshal.ThrowExceptionForHR(manager.RemoveAllSessions());
        return true;
    });

    /// <summary>The focus settings as Settings last saved them; null when they can't be read.</summary>
    public FocusSettings? ReadSettings() => OnThreadPool(() =>
    {
        try
        {
            if (_own is null || _own.GetDefaultThemeId(out Guid id) != 0)
                return null;
            Combase.GetActivationFactory<IFocusSessionActiveThemeFactory>("Windows.Internal.Shell.FocusSessionActiveTheme")
                .CreateInstance(id, out IFocusSessionTheme theme);
            return theme.GetIsStartFocusTimerEnabled(out byte clock) == 0
                && theme.GetIsHideTaskbarBadgesEnabled(out byte badges) == 0
                && theme.GetIsHideTaskbarFlashesEnabled(out byte flashing) == 0
                && theme.GetIsMuteNotificationsEnabled(out byte mute) == 0
                    ? new FocusSettings(clock != 0, badges != 0, flashing != 0, mute != 0)
                    : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
    });

    public void Dispose() => _watcher?.Dispose();

    // The manager keeps cloud store objects of the apartment it was first made in and uses them from its own
    // thread-pool timer: made on the UI thread (an STA), that fails with RPC_E_WRONG_THREAD and Windows ends the
    // process. So it's made and called on the thread pool, which is multithreaded, as in Explorer's component.
    private static T OnThreadPool<T>(Func<T> work) => Task.Run(work).GetAwaiter().GetResult();

    // Explorer's, so its timer ends the session even if NeoShell has gone by then.
    private IFocusSessionThemeManager? Manager()
    {
        try
        {
            var services = Ole32.Create<IOleServiceProvider>(FocusSessionService.CLSID_ImmersiveShell, Ole32.CLSCTX_LOCAL_SERVER);
            return Component(services) is { } component && component.GetThemeManager(out IFocusSessionThemeManager manager) == 0
                ? manager
                : _own;
        }
        catch (COMException)
        {
            // Explorer is restarting.
            return _own;
        }
    }

    private static unsafe IFocusSessionComponent? Component(IOleServiceProvider services)
    {
        Guid service = FocusSessionService.SID_FocusSessionComponent;
        Guid iid = typeof(IFocusSessionComponent).GUID;
        nint component;
        return services.QueryService(&service, &iid, &component) == 0 ? ComPointer.TakeOwnership<IFocusSessionComponent>(component) : null;
    }

    private void Watch()
    {
        if (_watcher?.IsWatching == true)
            return;
        _watcher?.Dispose();
        _watcher = new RegistryWatcher(SessionsKey);
        _watcher.Changed += () => Changed?.Invoke();
    }
}
