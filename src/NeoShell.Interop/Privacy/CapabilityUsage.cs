using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Privacy;

/// <summary>
/// The apps using a capability right now (the microphone, the location), by display name, as Explorer's taskbar learns
/// them for its privacy indicator. Windows' capability access manager keeps this for every app, packaged or not, from
/// the moment an app starts using the device until it stops.
/// </summary>
/// <remarks>
/// Read on thread-pool threads only, as <see cref="Notifications.AppBadges"/> reads Windows' badge store: the change
/// event comes on a WNF thread, and the names are asked for once it has returned.
/// </remarks>
public sealed class CapabilityUsage : IDisposable
{
    public const string Microphone = "microphone";
    public const string Location = "location";

    private readonly string _capability;
    private readonly Lock _lock = new();
    private ICapabilityUsageInfo? _info;
    private long _token;
    private bool _disposed;

    /// <summary>Starts watching the capability, on the thread pool; <see cref="Changed"/> follows the first read.</summary>
    /// <param name="capability">Its name in the capability access manager: <see cref="Microphone"/>, <see cref="Location"/>.</param>
    public CapabilityUsage(string capability)
    {
        _capability = capability;
        ThreadPool.QueueUserWorkItem(_ => Watch());
    }

    /// <summary>The apps using the capability changed. Raised on a thread-pool thread.</summary>
    public event Action? Changed;

    /// <summary>The display names of the apps using the capability, as last read; empty while none is.</summary>
    public IReadOnlyList<string> Apps { get; private set; } = [];

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            if (_info is not null && _token != 0)
                _info.RemoveUsageChanged(_token);
            _info = null;
        }
    }

    private void Watch()
    {
        try
        {
            var factory = Combase.GetActivationFactory<ICapabilityUsageInfoFactory>(
                "WindowsUdk.Security.Authorization.AppCapabilityAccess.CapabilityUsageInfo");
            Marshal.ThrowExceptionForHR(Combase.WindowsCreateString(_capability, (uint)_capability.Length, out nint name));
            ICapabilityUsageInfo info;
            try
            {
                Marshal.ThrowExceptionForHR(factory.CreateInstance(name, out info));
            }
            finally
            {
                Combase.WindowsDeleteString(name);
            }

            lock (_lock)
            {
                if (_disposed)
                    return;
                _info = info;
                var handler = new CapabilityUsageChangedHandler(() => ThreadPool.QueueUserWorkItem(_ => Read()));
                Marshal.ThrowExceptionForHR(info.AddUsageChanged(handler, out _token));
            }
            Read();
        }
        catch (Exception ex)
        {
            // Not in this Windows build, for one: no privacy indicator for the capability.
            NativeCallback.Report(new InvalidOperationException($"Can't watch which apps use the {_capability}", ex));
        }
    }

    private void Read()
    {
        try
        {
            lock (_lock)
            {
                if (_info is null)
                    return;
                Marshal.ThrowExceptionForHR(_info.GetDisplayNamesForAppsUsingCapability(out IStringVectorView view));
                Marshal.ThrowExceptionForHR(view.GetSize(out uint size));
                var apps = new List<string>((int)size);
                for (uint i = 0; i < size; i++)
                {
                    Marshal.ThrowExceptionForHR(view.GetAt(i, out nint name));
                    apps.Add(Combase.GetString(name));
                    Combase.WindowsDeleteString(name);
                }
                if (apps.SequenceEqual(Apps))
                    return;
                Apps = apps;
            }
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
    }
}

[GeneratedComClass]
internal sealed partial class CapabilityUsageChangedHandler(Action onChange) : ICapabilityUsageChangedHandler
{
    public int Invoke(nint sender, nint args)
    {
        try
        {
            onChange();
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 0;
    }
}
