namespace NeoShell.Interop.Wireless;

public enum WirelessDeviceKind { Unknown, Headset, Mouse, Keyboard, Controller, Bluetooth }

/// <summary>A wireless device with a battery, and what it last reported.</summary>
/// <param name="Id">Stable across reads and sign-ins, e.g. "razer:deathadder-v4-pro" or "bt:&lt;container id&gt;".</param>
/// <param name="IsConnected">
/// It answered. A receiver or pairing can be there while the device itself is off, asleep or out of range.
/// </param>
/// <param name="Level">Battery level 0–100; null when unknown.</param>
/// <param name="IsCharging">Null when the device doesn't say.</param>
public sealed record WirelessDevice(string Id, string Name, WirelessDeviceKind Kind, bool IsConnected, int? Level, bool? IsCharging);
