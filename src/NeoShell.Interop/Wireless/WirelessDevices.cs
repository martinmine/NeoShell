namespace NeoShell.Interop.Wireless;

/// <summary>
/// Wireless devices with a battery: Bluetooth devices Windows knows the level of, and the 2.4 GHz devices Windows
/// doesn't (Razer mice, the Audeze Maxwell), asked over HID. As the WirelessStatus app lists them.
/// </summary>
public sealed class WirelessDevices
{
    // A read that takes longer is given up; its devices stay as last read.
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(15);

    private static readonly Func<CancellationToken, Task<IReadOnlyList<WirelessDevice>>>[] s_readers =
        [RazerBattery.ReadAsync, AudezeMaxwellBattery.ReadAsync, BluetoothBattery.ReadAsync];

    private readonly SemaphoreSlim _reading = new(1, 1);
    private readonly IReadOnlyList<WirelessDevice>[] _last = [.. s_readers.Select(_ => (IReadOnlyList<WirelessDevice>)[])];

    /// <summary>
    /// Reads every device, off the calling thread (HID calls block). A kind of device that fails or doesn't answer in
    /// time keeps what it last read; <paramref name="failed"/> is told why.
    /// </summary>
    public async Task<IReadOnlyList<WirelessDevice>> ReadAsync(Action<Exception>? failed = null, CancellationToken cancel = default)
    {
        await _reading.WaitAsync(cancel);
        try
        {
            IReadOnlyList<WirelessDevice>?[] read = await Task.WhenAll(s_readers.Select(reader => ReadOneAsync(reader, failed, cancel)));
            for (int i = 0; i < read.Length; i++)
            {
                if (read[i] is { } devices)
                    _last[i] = devices;
            }
            return [.. _last.SelectMany(devices => devices)];
        }
        finally
        {
            _reading.Release();
        }
    }

    private static async Task<IReadOnlyList<WirelessDevice>?> ReadOneAsync(
        Func<CancellationToken, Task<IReadOnlyList<WirelessDevice>>> reader, Action<Exception>? failed, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        try
        {
            return await Task.Run(() => reader(timeout.Token), timeout.Token).WaitAsync(s_timeout, cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await timeout.CancelAsync();
            failed?.Invoke(ex);
            return null;
        }
    }
}
