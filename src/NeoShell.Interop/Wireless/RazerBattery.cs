using System.ComponentModel;

namespace NeoShell.Interop.Wireless;

/// <summary>
/// Razer wireless mice, through Razer's control report (the protocol as documented by the OpenRazer project, which is
/// GPL: reimplemented, not copied).
/// </summary>
internal static class RazerBattery
{
    private const ushort VendorId = 0x1532;

    // Between sending a request and reading the answer; newer receivers need about 31 ms.
    private static readonly TimeSpan s_responseDelay = TimeSpan.FromMilliseconds(35);
    private const int MaxAttempts = 3;

    /// <param name="Key">Shared by a mouse's cable and receiver product ids, so they're one device.</param>
    private sealed record Model(string Key, string Name, WirelessDeviceKind Kind, byte TransactionId);

    // Transaction ids per model from OpenRazer's razermouse_driver.c.
    private static readonly Dictionary<ushort, Model> s_models = new()
    {
        [0x00BE] = new("deathadder-v4-pro", "Razer DeathAdder V4 Pro", WirelessDeviceKind.Mouse, 0x1F), // cable
        [0x00BF] = new("deathadder-v4-pro", "Razer DeathAdder V4 Pro", WirelessDeviceKind.Mouse, 0x1F), // HyperSpeed receiver
    };

    public static async Task<IReadOnlyList<WirelessDevice>> ReadAsync(CancellationToken cancel)
    {
        var devices = new List<WirelessDevice>();
        IEnumerable<IGrouping<string, HidDeviceInfo>> collectionsByModel = HidDevice.Enumerate(VendorId)
            .Where(info => s_models.ContainsKey(info.ProductId) && info.FeatureReportLength == RazerReport.FeatureReportLength)
            .GroupBy(info => s_models[info.ProductId].Key);

        foreach (IGrouping<string, HidDeviceInfo> collections in collectionsByModel)
        {
            Model model = s_models[collections.First().ProductId];
            var device = new WirelessDevice($"razer:{model.Key}", model.Name, model.Kind, false, null, null);
            // Several collections (and both the cable and the receiver) may answer; the first one that does counts.
            foreach (HidDeviceInfo info in collections)
            {
                if (await TryReadAsync(info, model, cancel) is { } read)
                {
                    device = device with { IsConnected = true, Level = read.Level, IsCharging = read.IsCharging };
                    break;
                }
            }
            devices.Add(device);
        }
        return devices;
    }

    /// <summary>Razer reports the battery as 0–255.</summary>
    internal static int ToPercent(byte raw) => (int)Math.Round(raw * 100 / 255.0);

    private static async Task<(int Level, bool? IsCharging)?> TryReadAsync(HidDeviceInfo info, Model model, CancellationToken cancel)
    {
        using HidDevice? device = HidDevice.TryOpen(info);
        if (device is null)
            return null;

        byte[]? battery = await SendAsync(device, RazerReport.Create(model.TransactionId, 0x07, 0x80, 0x02), cancel);
        if (battery is null)
            return null;
        byte[]? charging = await SendAsync(device, RazerReport.Create(model.TransactionId, 0x07, 0x84, 0x02), cancel);
        return (ToPercent(RazerReport.GetArgument(battery, 1)), charging is null ? null : RazerReport.GetArgument(charging, 1) != 0);
    }

    private static async Task<byte[]?> SendAsync(HidDevice device, byte[] request, CancellationToken cancel)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                device.SetFeature(request);
                await Task.Delay(s_responseDelay, cancel);
                var response = new byte[RazerReport.FeatureReportLength];
                device.GetFeature(response);

                byte status = RazerReport.GetStatus(response);
                if (RazerReport.Matches(request, response) && status == RazerReport.StatusSuccess)
                    return response;
                // From the receiver these mean the mouse is off or asleep: no point asking again.
                if (status is RazerReport.StatusTimeout or RazerReport.StatusNotSupported or RazerReport.StatusFailure)
                    return null;
            }
            catch (Win32Exception)
            {
                return null;
            }
        }
        return null;
    }
}

/// <summary>
/// Razer's 90-byte control report, sent as HID feature report 0 (91 bytes with the report id). Layout: status,
/// transaction id, remaining packets (big-endian 16 bits), protocol type, data size, command class, command id, 80
/// argument bytes, CRC (XOR of bytes 2 to 87), reserved.
/// </summary>
internal static class RazerReport
{
    public const int Length = 90;
    public const int FeatureReportLength = Length + 1;

    public const byte StatusSuccess = 0x02;
    public const byte StatusFailure = 0x03;
    public const byte StatusTimeout = 0x04;
    public const byte StatusNotSupported = 0x05;

    // Offsets in the 90-byte report; the feature buffer has the report id before it.
    private const int Status = 0;
    private const int TransactionId = 1;
    private const int DataSize = 5;
    private const int CommandClass = 6;
    private const int CommandId = 7;
    private const int Arguments = 8;
    private const int Crc = 88;

    /// <summary>A feature report buffer: report id 0 and the 90 bytes.</summary>
    public static byte[] Create(byte transactionId, byte commandClass, byte commandId, byte dataSize)
    {
        var buffer = new byte[FeatureReportLength];
        Span<byte> report = buffer.AsSpan(1);
        report[TransactionId] = transactionId;
        report[DataSize] = dataSize;
        report[CommandClass] = commandClass;
        report[CommandId] = commandId;
        report[Crc] = CalculateCrc(report);
        return buffer;
    }

    public static byte CalculateCrc(ReadOnlySpan<byte> report)
    {
        byte crc = 0;
        for (int i = 2; i < Crc; i++)
            crc ^= report[i];
        return crc;
    }

    public static byte GetStatus(ReadOnlySpan<byte> featureBuffer) => featureBuffer[1 + Status];

    public static bool Matches(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response) =>
        request[1 + CommandClass] == response[1 + CommandClass] && request[1 + CommandId] == response[1 + CommandId];

    public static byte GetArgument(ReadOnlySpan<byte> featureBuffer, int index) => featureBuffer[1 + Arguments + index];
}
