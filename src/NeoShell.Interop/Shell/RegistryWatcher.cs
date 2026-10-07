using Microsoft.Win32;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>
/// Raises <see cref="Changed"/> whenever a value of a key under HKEY_CURRENT_USER is set or deleted, on a thread-pool
/// thread: for settings Explorer picks up from the registry as they change, without any message. With
/// <c>subtree</c>, values of the keys below it too.
/// </summary>
public sealed class RegistryWatcher : IDisposable
{
    private readonly RegistryKey? _key;
    private readonly AutoResetEvent _signal = new(false);
    private readonly RegisteredWaitHandle? _wait;
    private readonly bool _subtree;

    public RegistryWatcher(string path, bool subtree = false)
    {
        _subtree = subtree;
        _key = Registry.CurrentUser.OpenSubKey(path);
        if (_key is null || !Arm())
            return;

        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) =>
        {
            try
            {
                // Each notification fires once; ask for the next before passing this one on, so none is missed.
                Arm();
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                NativeCallback.Report(ex);
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public event Action? Changed;

    public void Dispose()
    {
        _wait?.Unregister(null);
        _key?.Dispose();
        _signal.Dispose();
    }

    // Thread-agnostic: a request made on a thread-pool thread would otherwise end with the thread. Below the key, keys
    // added and deleted count too.
    private bool Arm() =>
        Advapi32.RegNotifyChangeKeyValue(_key!.Handle.DangerousGetHandle(), _subtree,
            Advapi32.REG_NOTIFY_CHANGE_LAST_SET | Advapi32.REG_NOTIFY_THREAD_AGNOSTIC | (_subtree ? Advapi32.REG_NOTIFY_CHANGE_NAME : 0),
            _signal.SafeWaitHandle.DangerousGetHandle(), true) == 0;
}
