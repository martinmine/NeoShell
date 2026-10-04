using Windows.Devices.Power;
using WinRtBatteryStatus = Windows.System.Power.BatteryStatus;

namespace NeoShell.Interop.Power;

/// <param name="Percent">Charge left, 0 to 100.</param>
/// <param name="IsCharging">Plugged in and charging (not when plugged in and full).</param>
public sealed record BatteryState(int Percent, bool IsCharging);

/// <summary>The charge of the PC's batteries, together. <see cref="Changed"/> comes from a thread-pool thread.</summary>
public sealed class BatteryMonitor : IDisposable
{
    public BatteryMonitor()
    {
        Battery.AggregateBattery.ReportUpdated += OnReportUpdated;
    }

    public event Action? Changed;

    /// <summary>Null on a PC without a battery.</summary>
    public static BatteryState? Read()
    {
        BatteryReport report = Battery.AggregateBattery.GetReport();
        if (report.Status == WinRtBatteryStatus.NotPresent
            || report.FullChargeCapacityInMilliwattHours is not { } full
            || full <= 0
            || report.RemainingCapacityInMilliwattHours is not { } remaining)
        {
            return null;
        }

        int percent = (int)Math.Clamp(Math.Round(remaining * 100.0 / full), 0, 100);
        return new BatteryState(percent, report.Status == WinRtBatteryStatus.Charging);
    }

    public void Dispose() => Battery.AggregateBattery.ReportUpdated -= OnReportUpdated;

    private void OnReportUpdated(Battery sender, object args) => Changed?.Invoke();
}
