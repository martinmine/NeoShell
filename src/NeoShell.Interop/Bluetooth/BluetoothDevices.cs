using System.ComponentModel;
using System.Runtime.InteropServices;
using NeoShell.Interop.Native;
using NeoShell.Interop.Wireless;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace NeoShell.Interop.Bluetooth;

public enum BluetoothDeviceKind { Other, Audio, Keyboard, Mouse, Phone, Computer, Gamepad }

public sealed record PairedDevice(string Name, BluetoothDeviceKind Kind, bool IsConnected)
{
    /// <summary>Groups the device's several nodes (its audio endpoints, battery) in Windows.</summary>
    public Guid ContainerId { get; init; }

    public ulong Address { get; init; }

    public bool IsLowEnergy { get; init; }

    /// <summary>
    /// Windows made audio endpoints for it: Windows connects and disconnects such a device, and lists the others only
    /// (DevicesFlow makes a connectable model only for Bluetooth audio devices).
    /// </summary>
    public bool IsAudio { get; init; }

    /// <summary>What of its audio is connected.</summary>
    public BluetoothAudioProfiles Audio { get; init; }

    /// <summary>Its battery level as Windows last heard it, 0-100; null when it reports none.</summary>
    public int? Battery { get; init; }
}

/// <summary>The Bluetooth devices paired with this PC, classic and Low Energy (WinRT), and connecting their audio.</summary>
/// <remarks>
/// Windows has no public API to connect or disconnect a paired device. Its own Quick Settings (DevicesFlowBroker's
/// BluetoothAudioProvider) does it for audio devices only, with documented pieces: a kernel streaming request to the
/// Bluetooth audio driver behind each of the device's endpoints (<see cref="BluetoothAudio"/>), and to disconnect,
/// dropping the device's link (IOCTL_BTH_DISCONNECT_DEVICE). This does the same.
/// </remarks>
public static class BluetoothDevices
{
    // ClassOfDevice's minor class bits for a peripheral (Bluetooth assigned numbers).
    private const int PeripheralKeyboard = 0x10;
    private const int PeripheralPointer = 0x20;

    private const string ContainerIdProperty = "System.Devices.Aep.ContainerId";

    // An array, not a collection expression passed as IEnumerable: CsWinRT can't marshal those under Native AOT.
    private static readonly string[] s_properties = [ContainerIdProperty];

    public static async Task<IReadOnlyList<PairedDevice>> PairedAsync()
    {
        Task<List<AudioEndpointInfo>> endpointsTask = Task.Run(BluetoothAudio.Endpoints);
        Task<Dictionary<Guid, int>> batteryTask = BluetoothBattery.LevelsAsync(CancellationToken.None);

        var devices = new List<PairedDevice>();
        foreach (DeviceInformation info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), s_properties))
        {
            using BluetoothDevice? device = await BluetoothDevice.FromIdAsync(info.Id);
            if (device is not null)
            {
                devices.Add(new PairedDevice(Name(device.Name, info), Kind(device.ClassOfDevice), device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                {
                    ContainerId = ContainerId(info),
                    Address = device.BluetoothAddress,
                });
            }
        }
        foreach (DeviceInformation info in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), s_properties))
        {
            using BluetoothLEDevice? device = await BluetoothLEDevice.FromIdAsync(info.Id);
            if (device is not null)
            {
                devices.Add(new PairedDevice(Name(device.Name, info), Kind(device.Appearance), device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                {
                    ContainerId = ContainerId(info),
                    Address = device.BluetoothAddress,
                    IsLowEnergy = true,
                });
            }
        }

        return Combine(devices, await endpointsTask, await batteryTask);
    }

    /// <summary>
    /// One entry per physical device (a dual-mode device is paired as both classic and LE: the classic one stays), with
    /// its audio and battery, connected ones first.
    /// </summary>
    internal static IReadOnlyList<PairedDevice> Combine(
        IEnumerable<PairedDevice> devices, IReadOnlyList<AudioEndpointInfo> endpoints, IReadOnlyDictionary<Guid, int> battery) =>
    [
        .. devices
            // A device without a container can't be matched with anything: it stays on its own.
            .GroupBy(device => device.ContainerId == Guid.Empty ? Guid.NewGuid() : device.ContainerId)
            .Select(group =>
            {
                PairedDevice device = group.OrderBy(each => each.IsLowEnergy).First();
                var own = endpoints.Where(endpoint => endpoint.ContainerId == group.Key).ToList();
                return device with
                {
                    IsConnected = group.Any(each => each.IsConnected),
                    IsAudio = own.Count > 0,
                    Audio = BluetoothAudio.Connected(own),
                    Battery = battery.TryGetValue(group.Key, out int level) ? level : null,
                };
            })
            .OrderByDescending(device => device.IsConnected)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase),
    ];

    /// <summary>Connects a paired audio device (<see cref="PairedDevice.IsAudio"/>). Throws when Windows refuses.</summary>
    public static Task ConnectAsync(PairedDevice device) =>
        Task.Run(() => Marshal.ThrowExceptionForHR(BluetoothAudio.Request(device.ContainerId, connect: true)));

    /// <summary>Disconnects a paired audio device: its profiles, then its link. Throws when the link stays.</summary>
    public static Task DisconnectAsync(PairedDevice device) => Task.Run(() =>
    {
        // As Windows, a profile that won't go doesn't stop the link from being dropped.
        BluetoothAudio.Request(device.ContainerId, connect: false);
        // LE Audio's link is the LE device's own, which Windows leaves be (BluetoothAudioProvider::Disconnect).
        if (device.IsLowEnergy)
            return;

        uint error = BluetoothApis.BluetoothDisconnectDevice(0, device.Address);
        if (error != 0)
            throw new Win32Exception((int)error);
    });

    private static string Name(string name, DeviceInformation info) => string.IsNullOrWhiteSpace(name) ? info.Name : name;

    private static Guid ContainerId(DeviceInformation info) =>
        info.Properties.GetValueOrDefault(ContainerIdProperty) as Guid? ?? Guid.Empty;

    private static BluetoothDeviceKind Kind(BluetoothClassOfDevice type) => type.MajorClass switch
    {
        BluetoothMajorClass.AudioVideo => BluetoothDeviceKind.Audio,
        BluetoothMajorClass.Phone => BluetoothDeviceKind.Phone,
        BluetoothMajorClass.Computer => BluetoothDeviceKind.Computer,
        BluetoothMajorClass.Peripheral when ((int)type.MinorClass & PeripheralKeyboard) != 0 => BluetoothDeviceKind.Keyboard,
        BluetoothMajorClass.Peripheral when ((int)type.MinorClass & PeripheralPointer) != 0 => BluetoothDeviceKind.Mouse,
        // The low bits: joystick, gamepad…
        BluetoothMajorClass.Peripheral when ((int)type.MinorClass & 0xF) is (int)BluetoothMinorClass.PeripheralJoystick or (int)BluetoothMinorClass.PeripheralGamepad => BluetoothDeviceKind.Gamepad,
        _ => BluetoothDeviceKind.Other,
    };

    private static BluetoothDeviceKind Kind(BluetoothLEAppearance appearance)
    {
        ushort category = appearance.Category;
        if (category == BluetoothLEAppearanceCategories.HumanInterfaceDevice)
        {
            ushort subcategory = appearance.SubCategory;
            return subcategory == BluetoothLEAppearanceSubcategories.Keyboard ? BluetoothDeviceKind.Keyboard
                : subcategory == BluetoothLEAppearanceSubcategories.Mouse ? BluetoothDeviceKind.Mouse
                : subcategory == BluetoothLEAppearanceSubcategories.Gamepad || subcategory == BluetoothLEAppearanceSubcategories.Joystick ? BluetoothDeviceKind.Gamepad
                : BluetoothDeviceKind.Other;
        }
        return category == BluetoothLEAppearanceCategories.Phone ? BluetoothDeviceKind.Phone
            : category == BluetoothLEAppearanceCategories.Computer ? BluetoothDeviceKind.Computer
            : BluetoothDeviceKind.Other;
    }
}
