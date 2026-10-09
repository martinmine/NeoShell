using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Bluetooth;

/// <summary>What of a Bluetooth audio device is connected: calls (hands-free), other sound (stereo), or both.</summary>
[Flags]
public enum BluetoothAudioProfiles { None = 0, Voice = 1, Music = 2 }

/// <summary>An audio output or input Windows made for a device, with the device's container.</summary>
internal sealed record AudioEndpointInfo(string Id, Guid ContainerId, bool IsActive, bool IsInput, bool IsHeadset);

/// <summary>
/// Connects and disconnects Bluetooth audio devices as Windows' own Quick Settings does (DevicesFlowBroker's
/// BluetoothAudioProvider, behind ControlCenter's Bluetooth page): each of the device's audio endpoints is followed to
/// the Bluetooth audio driver's KS filter, which is asked to bring its profile's connection up or down.
/// </summary>
internal static unsafe class BluetoothAudio
{
    // ksmedia.h: one-shot requests to a Bluetooth audio filter (hands-free or stereo).
    private static readonly Guid KSPROPSETID_BtAudio = new("7fa06c40-b8f6-4c7e-8556-e8c33a12e54d");
    private const uint KSPROPERTY_ONESHOT_RECONNECT = 0;
    private const uint KSPROPERTY_ONESHOT_DISCONNECT = 1;
    private const uint KSPROPERTY_TYPE_GET = 1;

    // A disconnected Bluetooth device keeps its endpoints, unplugged.
    private const uint DEVICE_STATE_UNPLUGGED = 0x8;
    private const ushort VT_UI4 = 19;
    private const ushort VT_CLSID = 72;
    private const uint HeadsetFormFactor = 5;
    private const int E_NOTFOUND = unchecked((int)0x80070490);

    private static readonly Ole32.PROPERTYKEY ContainerIdKey = new() { fmtid = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), pid = 2 };
    private static readonly Ole32.PROPERTYKEY FormFactorKey = new() { fmtid = new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), pid = 0 };

    /// <summary>The audio endpoints of every device, plugged in or not; empty without the audio service.</summary>
    public static List<AudioEndpointInfo> Endpoints()
    {
        var endpoints = new List<AudioEndpointInfo>();
        IMMDeviceEnumerator enumerator = CreateEnumerator();
        foreach (EDataFlow flow in (EDataFlow[])[EDataFlow.Render, EDataFlow.Capture])
        {
            if (enumerator.EnumAudioEndpoints(flow, CoreAudio.DEVICE_STATE_ACTIVE | DEVICE_STATE_UNPLUGGED, out IMMDeviceCollection devices) != 0
                || devices.GetCount(out uint count) != 0)
            {
                continue;
            }

            for (uint i = 0; i < count; i++)
            {
                if (devices.Item(i, out IMMDevice device) != 0 || device.GetId(out nint id) != 0)
                    continue;

                string endpointId = TakeString(id);
                if (device.OpenPropertyStore(0, out IPropertyStore store) == 0 && ReadGuid(store, ContainerIdKey) is Guid containerId
                    && device.GetState(out uint state) == 0)
                {
                    endpoints.Add(new AudioEndpointInfo(endpointId, containerId, state == CoreAudio.DEVICE_STATE_ACTIVE,
                        flow == EDataFlow.Capture, ReadUInt(store, FormFactorKey) == HeadsetFormFactor));
                }
            }
        }
        return endpoints;
    }

    /// <summary>
    /// What of a device is connected, from its endpoints: a headset's (the hands-free profile makes one for each
    /// direction) or any input mean calls, any other output means stereo sound.
    /// </summary>
    public static BluetoothAudioProfiles Connected(IEnumerable<AudioEndpointInfo> endpoints) =>
        endpoints.Where(endpoint => endpoint.IsActive).Aggregate(BluetoothAudioProfiles.None, (profiles, endpoint) =>
            profiles | (endpoint.IsHeadset || endpoint.IsInput ? BluetoothAudioProfiles.Voice : BluetoothAudioProfiles.Music));

    /// <summary>
    /// Asks each audio endpoint of the device to connect (or disconnect) its profile. Returns S_OK when one did, else
    /// the last failure (E_NOTFOUND without endpoints): as Windows, a device connects when any of its profiles does.
    /// </summary>
    public static int Request(Guid containerId, bool connect)
    {
        IMMDeviceEnumerator enumerator = CreateEnumerator();
        int result = E_NOTFOUND;
        foreach (AudioEndpointInfo endpoint in Endpoints().Where(endpoint => endpoint.ContainerId == containerId))
        {
            try
            {
                Send(enumerator, endpoint.Id, connect);
                return 0;
            }
            catch (Exception ex)
            {
                result = ex.HResult;
            }
        }
        return result;
    }

    // Endpoint → its topology's connector → the connector it's plugged into, on the driver's filter → that filter as a
    // device, whose IKsControl takes the request (BluetoothAudioProvider::ConnectEndpoint).
    private static void Send(IMMDeviceEnumerator enumerator, string endpointId, bool connect)
    {
        Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out IMMDevice endpoint));
        Marshal.ThrowExceptionForHR(endpoint.Activate(typeof(IDeviceTopology).GUID, Ole32.CLSCTX_INPROC_SERVER, 0, out nint topology));
        Marshal.ThrowExceptionForHR(ComPointer.TakeOwnership<IDeviceTopology>(topology).GetConnector(0, out IConnector connector));
        Marshal.ThrowExceptionForHR(connector.GetConnectedTo(out IConnector filterConnector));
        Marshal.ThrowExceptionForHR(((IPart)filterConnector).GetTopologyObject(out IDeviceTopology filterTopology));
        Marshal.ThrowExceptionForHR(filterTopology.GetDeviceId(out nint filterId));
        Marshal.ThrowExceptionForHR(enumerator.GetDevice(TakeString(filterId), out IMMDevice filter));
        Marshal.ThrowExceptionForHR(filter.Activate(typeof(IKsControl).GUID, Ole32.CLSCTX_INPROC_SERVER, 0, out nint control));

        var property = new KSPROPERTY
        {
            Set = KSPROPSETID_BtAudio,
            Id = connect ? KSPROPERTY_ONESHOT_RECONNECT : KSPROPERTY_ONESHOT_DISCONNECT,
            Flags = KSPROPERTY_TYPE_GET,
        };
        Marshal.ThrowExceptionForHR(ComPointer.TakeOwnership<IKsControl>(control)
            .KsProperty(&property, (uint)sizeof(KSPROPERTY), null, 0, out _));
    }

    private static IMMDeviceEnumerator CreateEnumerator() =>
        Ole32.Create<IMMDeviceEnumerator>(CoreAudio.CLSID_MMDeviceEnumerator, CoreAudio.CLSCTX_ALL);

    private static Guid? ReadGuid(IPropertyStore store, Ole32.PROPERTYKEY key)
    {
        Ole32.PROPVARIANT value = default;
        try
        {
            return store.GetValue(&key, &value) == 0 && value.vt == VT_CLSID && value.pointer != 0 ? *(Guid*)value.pointer : null;
        }
        finally
        {
            Ole32.PropVariantClear(&value);
        }
    }

    private static uint? ReadUInt(IPropertyStore store, Ole32.PROPERTYKEY key)
    {
        Ole32.PROPVARIANT value = default;
        try
        {
            return store.GetValue(&key, &value) == 0 && value.vt == VT_UI4 ? (uint)value.pointer : null;
        }
        finally
        {
            Ole32.PropVariantClear(&value);
        }
    }

    private static string TakeString(nint text)
    {
        try
        {
            return Marshal.PtrToStringUni(text) ?? "";
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
        }
    }
}
