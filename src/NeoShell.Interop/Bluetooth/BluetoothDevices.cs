using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace NeoShell.Interop.Bluetooth;

public enum BluetoothDeviceKind { Other, Audio, Keyboard, Mouse, Phone, Computer, Gamepad }

public sealed record PairedDevice(string Name, BluetoothDeviceKind Kind, bool IsConnected);

/// <summary>The Bluetooth devices paired with this PC, classic and Low Energy (WinRT).</summary>
/// <remarks>
/// Windows has no public API to connect or disconnect a paired device (its own Bluetooth page uses private ones),
/// so they are only listed with their state.
/// </remarks>
public static class BluetoothDevices
{
    // ClassOfDevice's minor class bits for a peripheral (Bluetooth assigned numbers).
    private const int PeripheralKeyboard = 0x10;
    private const int PeripheralPointer = 0x20;

    public static async Task<IReadOnlyList<PairedDevice>> PairedAsync()
    {
        var devices = new List<PairedDevice>();
        foreach (DeviceInformation info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)))
        {
            using BluetoothDevice? device = await BluetoothDevice.FromIdAsync(info.Id);
            if (device is not null)
                devices.Add(new PairedDevice(Name(device.Name, info), Kind(device.ClassOfDevice), device.ConnectionStatus == BluetoothConnectionStatus.Connected));
        }
        foreach (DeviceInformation info in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)))
        {
            using BluetoothLEDevice? device = await BluetoothLEDevice.FromIdAsync(info.Id);
            if (device is not null)
                devices.Add(new PairedDevice(Name(device.Name, info), Kind(device.Appearance), device.ConnectionStatus == BluetoothConnectionStatus.Connected));
        }
        return [.. devices.OrderByDescending(device => device.IsConnected).ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static string Name(string name, DeviceInformation info) => string.IsNullOrWhiteSpace(name) ? info.Name : name;

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
