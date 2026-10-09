using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Com;

// Core Audio (mmdeviceapi.h, endpointvolume.h, audiopolicy.h). Methods NeoShell never calls are declared without
// parameters: only their place in the vtable matters. Interfaces NeoShell implements (callbacks) declare every
// method in full, as Windows calls them.

internal enum EDataFlow { Render = 0, Capture = 1 }

internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

internal static class CoreAudio
{
    public static readonly Guid CLSID_MMDeviceEnumerator = new("bcde0395-e52f-467c-8e3d-c4579291692e");
    public static readonly Guid CLSID_PolicyConfigClient = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");
    public const int AudioSessionStateExpired = 2;
    public const uint DEVICE_STATE_ACTIVE = 0x1;
    public const uint CLSCTX_ALL = 0x17;
    public const int S_OK = 0;

    // PKEY_Device_FriendlyName
    public static readonly Ole32.PROPERTYKEY FriendlyNameKey = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
}

[GeneratedComInterface]
[Guid("a95664d2-9614-4f35-a746-de8db63617e6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[GeneratedComInterface]
[Guid("0bd7a1be-7a1a-44db-8397-cc5392387b5e")]
internal partial interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}

[GeneratedComInterface]
[Guid("d666063f-1587-4e43-81f1-b948e807363f")]
internal partial interface IMMDevice
{
    [PreserveSig] int Activate(in Guid iid, uint context, nint activationParams, out nint instance);
    [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
    /// <summary>The endpoint's ID string, to be freed with <c>CoTaskMemFree</c>.</summary>
    [PreserveSig] int GetId(out nint id);
    [PreserveSig] int GetState(out uint state);
}

[GeneratedComInterface]
[Guid("7991eec9-7e89-4d85-8390-6c703cec60c0")]
internal partial interface IMMNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged(nint deviceId, uint newState);
    [PreserveSig] int OnDeviceAdded(nint deviceId);
    [PreserveSig] int OnDeviceRemoved(nint deviceId);
    [PreserveSig] int OnDefaultDeviceChanged(EDataFlow flow, ERole role, nint defaultDeviceId);
    [PreserveSig] int OnPropertyValueChanged(nint deviceId, Ole32.PROPERTYKEY key);
}

[GeneratedComInterface]
[Guid("5cdf2c82-841e-4546-9722-0cf74078229a")]
internal partial interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int GetChannelCount();
    [PreserveSig] int SetMasterVolumeLevel();
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, nint eventContext);
    [PreserveSig] int GetMasterVolumeLevel();
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel();
    [PreserveSig] int SetChannelVolumeLevelScalar();
    [PreserveSig] int GetChannelVolumeLevel();
    [PreserveSig] int GetChannelVolumeLevelScalar();
    [PreserveSig] int SetMute(int mute, nint eventContext);
    [PreserveSig] int GetMute(out int mute);
}

[GeneratedComInterface]
[Guid("657804fa-d6ad-4496-8a60-352752af4f89")]
internal partial interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(nint notificationData);
}

[GeneratedComInterface]
[Guid("77aa99a0-1bd6-484f-8bc7-2c654c9a9b6f")]
internal partial interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl();
    [PreserveSig] int GetSimpleAudioVolume();
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    [PreserveSig] int RegisterSessionNotification(IAudioSessionNotification notification);
    [PreserveSig] int UnregisterSessionNotification(IAudioSessionNotification notification);
}

[GeneratedComInterface]
[Guid("e2f5bb11-0570-40ca-acdd-3aa01277dee8")]
internal partial interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
}

/// <summary>IAudioSessionControl and its IAudioSessionControl2 additions; sessions implement both.</summary>
[GeneratedComInterface]
[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
internal partial interface IAudioSessionControl2
{
    [PreserveSig] int GetState(out int state);
    /// <summary>The name the app gave the session (often none), to be freed with <c>CoTaskMemFree</c>.</summary>
    [PreserveSig] int GetDisplayName(out nint name);
    [PreserveSig] int SetDisplayName();
    [PreserveSig] int GetIconPath();
    [PreserveSig] int SetIconPath();
    [PreserveSig] int GetGroupingParam();
    [PreserveSig] int SetGroupingParam();
    [PreserveSig] int RegisterAudioSessionNotification(IAudioSessionEvents events);
    [PreserveSig] int UnregisterAudioSessionNotification(IAudioSessionEvents events);
    [PreserveSig] int GetSessionIdentifier();
    [PreserveSig] int GetSessionInstanceIdentifier();
    [PreserveSig] int GetProcessId(out uint processId);
    /// <summary>S_OK for the system sounds session, S_FALSE otherwise.</summary>
    [PreserveSig] int IsSystemSoundsSession();
}

[GeneratedComInterface]
[Guid("87ce5498-68d6-44e5-9215-6da47ef883d8")]
internal unsafe partial interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float level, Guid* eventContext);
    [PreserveSig] int GetMasterVolume(out float level);
    [PreserveSig] int SetMute(int mute, Guid* eventContext);
    [PreserveSig] int GetMute(out int mute);
}

/// <summary>
/// The undocumented interface the Sound control panel sets the default device with (there's no public API for it);
/// the same since Windows 7, and what volume tools use.
/// </summary>
[GeneratedComInterface]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
internal partial interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
}

[GeneratedComInterface]
[Guid("641dd20b-4d41-49cc-aba3-174b9477bb08")]
internal partial interface IAudioSessionNotification
{
    [PreserveSig] int OnSessionCreated(IAudioSessionControl2 session);
}

[GeneratedComInterface]
[Guid("24918acc-64b3-37c1-8ca9-74a66e9957a8")]
internal partial interface IAudioSessionEvents
{
    [PreserveSig] int OnDisplayNameChanged(nint newDisplayName, nint eventContext);
    [PreserveSig] int OnIconPathChanged(nint newIconPath, nint eventContext);
    [PreserveSig] int OnSimpleVolumeChanged(float newVolume, int newMute, nint eventContext);
    [PreserveSig] int OnChannelVolumeChanged(uint channelCount, nint newChannelVolumes, uint changedChannel, nint eventContext);
    [PreserveSig] int OnGroupingParamChanged(nint newGroupingParam, nint eventContext);
    [PreserveSig] int OnStateChanged(int newState);
    [PreserveSig] int OnSessionDisconnected(int reason);
}
