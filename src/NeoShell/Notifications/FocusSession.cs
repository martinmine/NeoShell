using Microsoft.UI.Dispatching;
using NeoShell.Logging;

namespace NeoShell.Notifications;

/// <summary>
/// A focus session from the calendar: Do not disturb for a set time, then back as it was.
/// </summary>
/// <remarks>
/// Windows' own focus sessions (<c>Windows.UI.Shell.FocusSessionManager</c>) are a limited access feature that only
/// Microsoft's apps can unlock, so NeoShell keeps its own timer and does the part that matters, silencing toasts.
/// It doesn't hide taskbar badges or flashing as Windows' does.
/// </remarks>
internal sealed class FocusSession : IDisposable
{
    private readonly NotificationCenter _center;
    private readonly DispatcherQueueTimer _timer;
    private bool _doNotDisturbBefore;

    public FocusSession(NotificationCenter center)
    {
        _center = center;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            if (Remaining <= TimeSpan.Zero)
                Stop();
            else
                Changed?.Invoke();
        };
    }

    /// <summary>When the running session ends; null when none is running.</summary>
    public DateTime? EndsAt { get; private set; }

    public TimeSpan Remaining => EndsAt is { } end ? end - DateTime.Now : TimeSpan.Zero;

    /// <summary>Raised when a session starts or stops, and every second while one runs.</summary>
    public event Action? Changed;

    public void Start(int minutes)
    {
        if (EndsAt is null)
            _doNotDisturbBefore = _center.DoNotDisturb;
        EndsAt = DateTime.Now.AddMinutes(minutes);
        if (!_center.DoNotDisturb)
            _center.SetDoNotDisturb(true);
        _timer.Start();
        Log.Info($"Focus session for {minutes} minutes");
        Changed?.Invoke();
    }

    public void Stop()
    {
        if (EndsAt is null)
            return;

        EndsAt = null;
        _timer.Stop();
        if (!_doNotDisturbBefore)
            _center.SetDoNotDisturb(false);
        Changed?.Invoke();
    }

    /// <summary>Exiting mid-session leaves Do not disturb as it was before.</summary>
    public void Dispose() => Stop();
}
