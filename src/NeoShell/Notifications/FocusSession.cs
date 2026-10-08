using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using NeoShell.Interop.Notifications;
using NeoShell.Logging;

namespace NeoShell.Notifications;

/// <summary>
/// What a focus session changes in Explorer's settings, as Windows' focus manager does it: "Show badges on taskbar apps"
/// and "Show flashing on taskbar apps" (under Explorer\Advanced) go to 0 while it runs, then back to whether each was
/// shown before (1 or 0, as the manager's off theme keeps it).
/// </summary>
public static class FocusChanges
{
    public const string Badges = "TaskbarBadges";
    public const string Flashing = "TaskbarFlashing";

    public static IReadOnlyDictionary<string, int> During(FocusSettings settings) => Hidden(settings).ToDictionary(name => name, _ => 0);

    /// <param name="before">A value as it was before the session.</param>
    public static IReadOnlyDictionary<string, int> After(FocusSettings settings, Func<string, object?> before) =>
        Hidden(settings).ToDictionary(name => name, name => before(name) is int value && value == 0 ? 0 : 1);

    private static IEnumerable<string> Hidden(FocusSettings settings) =>
        (settings.HideBadges ? [Badges] : Array.Empty<string>()).Concat(settings.HideFlashing ? [Flashing] : []);
}

/// <summary>
/// The calendar's focus session. Alongside Explorer it's Windows' own (see <see cref="FocusSessions"/>): started and
/// ended as Explorer's calendar does, one started in Settings or the Clock app shows too, and Windows applies the focus
/// settings and plays the end chime (the Clock app's toast). As the shell Windows' manager can't run (it needs Explorer
/// to start the Clock app), so NeoShell applies the settings itself, without the Clock app's timer. UI thread only.
/// </summary>
internal sealed class FocusSession : IDisposable
{
    private const string ExplorerAdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly FocusSessions _sessions = new();
    private readonly NotificationCenter _center;
    private readonly bool _asShell;
    private readonly DispatcherQueueTimer _timer;
    // As the shell, while a session runs: what to put back.
    private (IReadOnlyDictionary<string, int> Values, bool DoNotDisturbOff)? _restore;

    public FocusSession(NotificationCenter center, bool asShell)
    {
        _center = center;
        _asShell = asShell;
        _timer = _dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => End();
        if (!asShell)
        {
            _sessions.Changed += () => _dispatcher.TryEnqueue(Refresh);
            IsActive = _sessions.IsActive;
        }
    }

    public bool IsAvailable => _sessions.IsAvailable;

    public bool IsActive { get; private set; }

    /// <summary>Raised when a session starts or ends.</summary>
    public event Action? Changed;

    public void Start(int minutes)
    {
        try
        {
            if (_asShell)
                StartOwn(TimeSpan.FromMinutes(minutes));
            else
                _sessions.Start(TimeSpan.FromMinutes(minutes));
            Log.Info($"Focus session for {minutes} minutes");
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't start a focus session", ex);
        }
        Refresh();
    }

    public void End()
    {
        try
        {
            if (_asShell)
                EndOwn();
            else
                _sessions.End();
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't end the focus session", ex);
        }
        Refresh();
    }

    /// <summary>
    /// Windows' session outlives NeoShell, as it outlives Explorer; NeoShell's own (as the shell) ends with it and puts
    /// the settings back.
    /// </summary>
    public void Dispose()
    {
        if (_asShell)
            EndOwn();
        _sessions.Dispose();
    }

    private void StartOwn(TimeSpan length)
    {
        if (_restore is null && _sessions.ReadSettings() is { } settings)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(ExplorerAdvancedKey);
            _restore = (FocusChanges.After(settings, name => key.GetValue(name)), settings.DoNotDisturb && !_center.DoNotDisturb);
            foreach ((string name, int value) in FocusChanges.During(settings))
                key.SetValue(name, value, RegistryValueKind.DWord);
            if (_restore.Value.DoNotDisturbOff)
                _center.SetDoNotDisturb(true);
        }
        _timer.Interval = length;
        _timer.Start();
    }

    private void EndOwn()
    {
        _timer.Stop();
        if (_restore is not { } restore)
            return;
        _restore = null;
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(ExplorerAdvancedKey);
        foreach ((string name, int value) in restore.Values)
            key.SetValue(name, value, RegistryValueKind.DWord);
        if (restore.DoNotDisturbOff)
            _center.SetDoNotDisturb(false);
    }

    private void Refresh()
    {
        bool active = _asShell ? _timer.IsRunning : _sessions.IsActive;
        if (active == IsActive)
            return;
        IsActive = active;
        Changed?.Invoke();
    }
}
