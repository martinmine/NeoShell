using System.ComponentModel;

namespace NeoShell.Interop.Wireless;

/// <summary>
/// The Audeze Maxwell headset, through its dongle (the protocol as documented by the HeadsetControl project, which is
/// GPL: reimplemented, not copied). Requests go out as output report 0x06; answers are read as input report 0x07, the
/// battery level following the bytes D6 0C 00 00. The headset doesn't report charging.
/// </summary>
internal static class AudezeMaxwellBattery
{
    private const ushort VendorId = 0x3329;
    private static readonly ushort[] s_productIds = [0x4B18 /* Xbox dongle */, 0x4B19 /* PlayStation/PC dongle */];
    private const ushort VendorUsagePage = 0xFF13;
    private const byte InputReportId = 0x07;

    // Audeze HQ sends packets 50–60 ms apart; the dongle misbehaves when they come faster.
    private static readonly TimeSpan s_packetDelay = TimeSpan.FromMilliseconds(60);
    private const int MaxReads = 5;
    private const int MaxAttempts = 2;

    private static readonly byte[] s_batteryRequest = [0x06, 0x07, 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C];
    private static readonly byte[] s_batteryMarker = [0xD6, 0x0C, 0x00, 0x00];
    private const byte MessageStart = 0x05;
    private const byte ReplyType = 0x5D;

    public static async Task<IReadOnlyList<WirelessDevice>> ReadAsync(CancellationToken cancel)
    {
        var devices = new List<WirelessDevice>();
        foreach (HidDeviceInfo info in HidDevice.Enumerate(VendorId).Where(info => s_productIds.Contains(info.ProductId) && info.UsagePage == VendorUsagePage))
        {
            int? level = await TryReadAsync(info, cancel);
            devices.Add(new WirelessDevice($"audeze:{VendorId:X4}:{info.ProductId:X4}", "Audeze Maxwell", WirelessDeviceKind.Headset,
                level is not null, level, null));
        }
        return devices;
    }

    private static async Task<int?> TryReadAsync(HidDeviceInfo info, CancellationToken cancel)
    {
        using HidDevice? device = HidDevice.TryOpen(info);
        if (device is null)
            return null;

        try
        {
            var buffer = new byte[Math.Max(info.InputReportLength, 62)];
            // Now and then the dongle never answers (seen while Audeze HQ talks to it too): ask once more before
            // calling the headset unavailable.
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                device.Write(s_batteryRequest);
                // The answer may not be the first input report waiting.
                for (int i = 0; i < MaxReads; i++)
                {
                    await Task.Delay(s_packetDelay, cancel);
                    Array.Clear(buffer);
                    buffer[0] = InputReportId;
                    device.GetInputReport(buffer.AsSpan(0, info.InputReportLength));
                    if (TryParseBattery(buffer) is { } level)
                        return level;
                }
            }
        }
        catch (Win32Exception)
        {
            // Unplugged meanwhile, or busy.
        }
        return null;
    }

    /// <summary>The battery reply (a 5D message with payload <c>D6 0C 00 00 &lt;level&gt;</c>) in an input report.</summary>
    internal static int? TryParseBattery(ReadOnlySpan<byte> report)
    {
        foreach ((byte type, byte[] payload) in ParseMessages(report))
        {
            if (type == ReplyType && payload.AsSpan().StartsWith(s_batteryMarker) && payload.Length > s_batteryMarker.Length)
            {
                byte level = payload[s_batteryMarker.Length];
                return level <= 100 ? level : null;
            }
        }
        return null;
    }

    /// <summary>
    /// The messages in input report 0x07: <c>07 &lt;length&gt; 80</c> then <c>length</c> bytes of messages, each
    /// <c>05 &lt;type&gt; &lt;payload length, LE16&gt; &lt;payload&gt;</c> (5B echoes a request, 5D answers it).
    /// Bytes past <c>length</c> are left over from earlier reports, stale answers among them: they're ignored.
    /// </summary>
    internal static List<(byte Type, byte[] Payload)> ParseMessages(ReadOnlySpan<byte> report)
    {
        var messages = new List<(byte, byte[])>();
        if (report.Length < 3 || report[0] != InputReportId)
            return messages;

        ReadOnlySpan<byte> remaining = report.Slice(3, Math.Min(report[1], report.Length - 3));
        while (remaining.Length >= 4 && remaining[0] == MessageStart)
        {
            int length = remaining[2] | (remaining[3] << 8);
            if (4 + length > remaining.Length)
                break;
            messages.Add((remaining[1], remaining.Slice(4, length).ToArray()));
            remaining = remaining[(4 + length)..];
        }
        return messages;
    }
}
