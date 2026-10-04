using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Audio;

// COM objects Core Audio calls back into, on its own threads. They only pass the news on: calling back into Core
// Audio from inside these callbacks can deadlock, so the work happens later on the owner's thread.

[GeneratedComClass]
internal sealed partial class DeviceNotifications(Action<EDataFlow?> onChange) : IMMNotificationClient
{
    public int OnDeviceStateChanged(nint deviceId, uint newState) => Raise(null);
    public int OnDeviceAdded(nint deviceId) => Raise(null);
    public int OnDeviceRemoved(nint deviceId) => Raise(null);
    public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, nint defaultDeviceId) => role == ERole.Console ? Raise(flow) : 0;
    public int OnPropertyValueChanged(nint deviceId, Ole32.PROPERTYKEY key) => 0;

    private int Raise(EDataFlow? flow)
    {
        try
        {
            onChange(flow);
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class VolumeNotifications(Action onChange) : IAudioEndpointVolumeCallback
{
    public int OnNotify(nint notificationData)
    {
        try
        {
            onChange();
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 0;
    }
}

/// <param name="ownChanges">
/// With volume changes: the event context NeoShell's own changes carry, which aren't passed on (whoever made them
/// already knows); without, volume changes aren't passed on at all.
/// </param>
[GeneratedComClass]
internal sealed unsafe partial class SessionNotifications(Action onChange, Guid? ownChanges = null) : IAudioSessionNotification, IAudioSessionEvents
{
    public int OnSessionCreated(IAudioSessionControl2 session) => Raise();
    public int OnDisplayNameChanged(nint newDisplayName, nint eventContext) => 0;
    public int OnIconPathChanged(nint newIconPath, nint eventContext) => 0;

    public int OnSimpleVolumeChanged(float newVolume, int newMute, nint eventContext) =>
        ownChanges is { } own && (eventContext == 0 || *(Guid*)eventContext != own) ? Raise() : 0;

    public int OnChannelVolumeChanged(uint channelCount, nint newChannelVolumes, uint changedChannel, nint eventContext) => 0;
    public int OnGroupingParamChanged(nint newGroupingParam, nint eventContext) => 0;
    public int OnStateChanged(int newState) => Raise();
    public int OnSessionDisconnected(int reason) => Raise();

    private int Raise()
    {
        try
        {
            onChange();
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 0;
    }
}
