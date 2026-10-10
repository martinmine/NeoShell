using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Notifications;

/// <summary>
/// Tells when the notification platform's notifications or Do not disturb change, so they're read then rather than
/// polled. The platform publishes the count of new notifications (<see cref="NewNotifications"/>) again whenever a
/// notification comes or goes, seen or not, and the quiet hours profile whenever Do not disturb is switched.
/// </summary>
public sealed unsafe class NotificationChanges : IDisposable
{
    // The instances by the context their callbacks carry: a callback that comes after Dispose finds none.
    private static readonly Dictionary<nint, NotificationChanges> s_byContext = [];
    private static nint s_lastContext;

    private readonly nint _context;
    private readonly List<nint> _subscriptions = [];

    /// <summary>Throws if the platform's states can't be followed.</summary>
    public NotificationChanges()
    {
        lock (s_byContext)
        {
            _context = ++s_lastContext;
            s_byContext[_context] = this;
        }
        foreach (ulong state in (ulong[])[NewNotifications.WNF_SHEL_NOTIFICATIONS, DoNotDisturb.WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED])
        {
            // From the stamp it has now: only later changes call back.
            long data = 0;
            uint length = sizeof(long);
            Ntdll.NtQueryWnfStateData(state, 0, 0, out uint stamp, &data, ref length);
            int status = Ntdll.RtlSubscribeWnfStateChangeNotification(out nint subscription, state, stamp, &OnChanged, _context, 0, 0, 0);
            if (status < 0)
            {
                Dispose();
                throw new InvalidOperationException($"Can't follow the notification platform's state 0x{state:X16}: NTSTATUS 0x{status:X8}.");
            }
            _subscriptions.Add(subscription);
        }
    }

    /// <summary>Notifications came or went, or Do not disturb changed. Raised on a thread-pool thread.</summary>
    public event Action? Changed;

    public void Dispose()
    {
        lock (s_byContext)
            s_byContext.Remove(_context);
        foreach (nint subscription in _subscriptions)
            Ntdll.RtlUnsubscribeWnfStateChangeNotification(subscription);
        _subscriptions.Clear();
    }

    [UnmanagedCallersOnly]
    private static int OnChanged(ulong state, uint stamp, nint typeId, nint context, void* buffer, uint length)
    {
        try
        {
            NotificationChanges? changes;
            lock (s_byContext)
                s_byContext.TryGetValue(context, out changes);
            changes?.Changed?.Invoke();
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 0;
    }
}
