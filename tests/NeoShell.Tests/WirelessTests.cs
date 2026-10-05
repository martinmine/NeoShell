using NeoShell.Interop.Wireless;

namespace NeoShell.Tests;

/// <summary>The wireless devices' protocols, with packets captured by the WirelessStatus app.</summary>
public sealed class WirelessTests
{
    [Fact]
    public void Razer_battery_request_has_its_layout_and_crc()
    {
        byte[] buffer = RazerReport.Create(transactionId: 0x1F, commandClass: 0x07, commandId: 0x80, dataSize: 0x02);

        Assert.Equal(RazerReport.FeatureReportLength, buffer.Length);
        Assert.Equal(0x00, buffer[0]); // report id
        Assert.Equal(0x1F, buffer[2]); // transaction id
        Assert.Equal(0x02, buffer[6]); // data size
        Assert.Equal(0x07, buffer[7]); // command class
        Assert.Equal(0x80, buffer[8]); // command id
        Assert.Equal(0x02 ^ 0x07 ^ 0x80, buffer[89]); // CRC: XOR of report bytes 2 to 87 (not the transaction id)
    }

    [Fact]
    public void Razer_answer_is_read()
    {
        // From a DeathAdder V4 Pro on its HyperSpeed receiver.
        var response = new byte[RazerReport.FeatureReportLength];
        Convert.FromHexString("00021F00000002078000FF").CopyTo(response, 0);
        byte[] request = RazerReport.Create(0x1F, 0x07, 0x80, 0x02);

        Assert.True(RazerReport.Matches(request, response));
        Assert.Equal(RazerReport.StatusSuccess, RazerReport.GetStatus(response));
        Assert.Equal(0xFF, RazerReport.GetArgument(response, 1));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(128, 50)]
    [InlineData(255, 100)]
    public void Razer_level_is_scaled_to_percent(byte raw, int percent)
    {
        Assert.Equal(percent, RazerBattery.ToPercent(raw));
    }

    [Fact]
    public void Maxwell_battery_is_found_among_queued_messages()
    {
        // From the Xbox dongle: the request's echo (5B …), then the battery reply (5D … D6 0C 00 00 34).
        var report = new byte[62];
        Convert.FromHexString("071080055B0300D60C00055D0500D60C00003400B12C0002").CopyTo(report, 0);

        Assert.Equal(0x34, AudezeMaxwellBattery.TryParseBattery(report));
    }

    [Fact]
    public void Maxwell_report_without_a_battery_reply_gives_nothing()
    {
        var report = new byte[62];
        Convert.FromHexString("073B80055C0300802C01055D0E00B12C000201013E984763").CopyTo(report, 0);

        Assert.Null(AudezeMaxwellBattery.TryParseBattery(report));
    }

    [Fact]
    public void Maxwell_stale_battery_reply_past_the_report_length_is_ignored()
    {
        // A reply to a settings read (length 0x0A), then leftovers of an earlier battery reply.
        byte[] report = Convert.FromHexString(
            "070A80055B060001092000000500D60C000033055D0500D60C00003402801A060000000000400D0300C8000000010200350C0000000000801A0600C80000");

        Assert.Null(AudezeMaxwellBattery.TryParseBattery(report));
        (byte type, byte[] payload) = Assert.Single(AudezeMaxwellBattery.ParseMessages(report));
        Assert.Equal(0x5B, type);
        Assert.Equal(Convert.FromHexString("010920000005"), payload);
    }

    [Fact]
    public void Maxwell_level_out_of_range_is_rejected()
    {
        byte[] report = Convert.FromHexString("070980055D0500D60C0000C8");

        Assert.Null(AudezeMaxwellBattery.TryParseBattery(report));
    }
}
