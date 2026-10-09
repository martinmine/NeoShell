using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Device topology (devicetopology.h) and kernel streaming control (ks.h, ksproxy.h): from an audio endpoint to the
// driver's KS filter behind it. As in CoreAudio.cs, methods NeoShell never calls are declared without parameters.

[GeneratedComInterface]
[Guid("2a07407e-6497-4a18-9787-32f79bd0d98f")]
internal partial interface IDeviceTopology
{
    [PreserveSig] int GetConnectorCount();
    [PreserveSig] int GetConnector(uint index, out IConnector connector);
    [PreserveSig] int GetSubunitCount();
    [PreserveSig] int GetSubunit();
    [PreserveSig] int GetPartById();
    /// <summary>The device's ID string, to be freed with <c>CoTaskMemFree</c>.</summary>
    [PreserveSig] int GetDeviceId(out nint id);
}

[GeneratedComInterface]
[Guid("9c2c4058-23f5-41de-877a-df3af236a09e")]
internal partial interface IConnector
{
    [PreserveSig] int GetConnectorType();
    [PreserveSig] int GetDataFlow();
    [PreserveSig] int ConnectTo();
    [PreserveSig] int Disconnect();
    [PreserveSig] int IsConnected();
    [PreserveSig] int GetConnectedTo(out IConnector connector);
}

[GeneratedComInterface]
[Guid("ae2de0e4-5bca-4f2d-aa46-5d13f8fdb3a9")]
internal partial interface IPart
{
    [PreserveSig] int GetName();
    [PreserveSig] int GetLocalId();
    [PreserveSig] int GetGlobalId();
    [PreserveSig] int GetPartType();
    [PreserveSig] int GetSubType();
    [PreserveSig] int GetControlInterfaceCount();
    [PreserveSig] int GetControlInterface();
    [PreserveSig] int EnumPartsIncoming();
    [PreserveSig] int EnumPartsOutgoing();
    [PreserveSig] int GetTopologyObject(out IDeviceTopology topology);
}

[StructLayout(LayoutKind.Sequential)]
internal struct KSPROPERTY
{
    public Guid Set;
    public uint Id;
    public uint Flags;
}

[GeneratedComInterface]
[Guid("28f54685-06fd-11d2-b27a-00a0c9223196")]
internal unsafe partial interface IKsControl
{
    [PreserveSig] int KsProperty(KSPROPERTY* property, uint propertyLength, void* data, uint dataLength, out uint bytesReturned);
}
