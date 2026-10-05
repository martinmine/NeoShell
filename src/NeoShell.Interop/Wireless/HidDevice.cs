using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Wireless;

/// <summary>One HID top-level collection (one device interface path).</summary>
internal sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength);

/// <summary>
/// An opened HID collection. Kept open only for one read: the devices' own apps (Razer Synapse, Audeze HQ) talk to
/// them too.
/// </summary>
internal sealed unsafe class HidDevice : IDisposable
{
    private readonly SafeFileHandle _handle;

    private HidDevice(HidDeviceInfo info, SafeFileHandle handle)
    {
        Info = info;
        _handle = handle;
    }

    public HidDeviceInfo Info { get; }

    public static List<HidDeviceInfo> Enumerate(ushort vendorId)
    {
        var result = new List<HidDeviceInfo>();
        foreach (string path in InterfacePaths())
        {
            // No access is enough to read attributes, even of collections Windows holds exclusively (mice, keyboards).
            using SafeFileHandle handle = Open(path, 0);
            if (handle.IsInvalid)
                continue;

            var attributes = new Hid.HIDD_ATTRIBUTES { Size = (uint)sizeof(Hid.HIDD_ATTRIBUTES) };
            if (!Hid.HidD_GetAttributes(handle, ref attributes) || attributes.VendorID != vendorId)
                continue;

            Hid.HIDP_CAPS caps = default;
            if (Hid.HidD_GetPreparsedData(handle, out nint preparsed))
            {
                if (Hid.HidP_GetCaps(preparsed, out caps) != Hid.HIDP_STATUS_SUCCESS)
                    caps = default;
                Hid.HidD_FreePreparsedData(preparsed);
            }
            result.Add(new HidDeviceInfo(path, attributes.VendorID, attributes.ProductID, caps.UsagePage,
                caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength));
        }
        return result;
    }

    /// <summary>
    /// Opens the collection to read and write. Windows holds mouse and keyboard collections exclusively; those get a
    /// handle without access, which still takes feature reports. Null when it can't be opened at all.
    /// </summary>
    public static HidDevice? TryOpen(HidDeviceInfo info)
    {
        SafeFileHandle handle = Open(info.Path, Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = Open(info.Path, 0);
        }
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return new HidDevice(info, handle);
    }

    /// <param name="report">The report id in byte 0; as long as the feature report.</param>
    public void SetFeature(ReadOnlySpan<byte> report)
    {
        fixed (byte* p = report)
        {
            if (!Hid.HidD_SetFeature(_handle, p, (uint)report.Length))
                throw LastError(nameof(Hid.HidD_SetFeature));
        }
    }

    /// <param name="buffer">The report id to read in byte 0.</param>
    public void GetFeature(Span<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            if (!Hid.HidD_GetFeature(_handle, p, (uint)buffer.Length))
                throw LastError(nameof(Hid.HidD_GetFeature));
        }
    }

    /// <param name="buffer">The report id to read in byte 0.</param>
    public void GetInputReport(Span<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            if (!Hid.HidD_GetInputReport(_handle, p, (uint)buffer.Length))
                throw LastError(nameof(Hid.HidD_GetInputReport));
        }
    }

    /// <summary>Writes an output report on the interrupt pipe, padded with zeros to the output report length.</summary>
    public void Write(ReadOnlySpan<byte> report)
    {
        Span<byte> buffer = stackalloc byte[Math.Max(Info.OutputReportLength, report.Length)];
        report.CopyTo(buffer);
        fixed (byte* p = buffer)
        {
            if (!Kernel32.WriteFile(_handle, p, (uint)buffer.Length, out _, 0))
                throw LastError(nameof(Kernel32.WriteFile));
        }
    }

    public void Dispose() => _handle.Dispose();

    private static SafeFileHandle Open(string path, uint access) =>
        Kernel32.CreateFile(path, access, Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, 0, Kernel32.OPEN_EXISTING, 0, 0);

    private static List<string> InterfacePaths()
    {
        Hid.HidD_GetHidGuid(out Guid hidGuid);
        while (true)
        {
            if (Hid.CM_Get_Device_Interface_List_Size(out uint length, ref hidGuid, 0, Hid.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != Hid.CR_SUCCESS)
                return [];

            var buffer = new char[length];
            fixed (char* p = buffer)
            {
                uint status = Hid.CM_Get_Device_Interface_List(ref hidGuid, 0, p, length, Hid.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
                // A device arrived between the two calls.
                if (status == Hid.CR_BUFFER_SMALL)
                    continue;
                if (status != Hid.CR_SUCCESS)
                    return [];
            }
            return [.. new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)];
        }
    }

    private static Win32Exception LastError(string function)
    {
        int error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"{function} failed: {new Win32Exception(error).Message} (0x{error:X})");
    }
}
