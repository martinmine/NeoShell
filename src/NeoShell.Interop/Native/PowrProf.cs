using System.Runtime.InteropServices;

namespace NeoShell.Interop.Native;

internal static unsafe partial class PowrProf
{
    public const uint DEVICE_NOTIFY_CALLBACK = 2;
    public const uint PBT_POWERSETTINGCHANGE = 0x8013;

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
    {
        public delegate* unmanaged<void*, uint, void*, uint> Callback;
        public void* Context;
    }

    /// <summary>POWERBROADCAST_SETTING's header; the setting's value follows it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
    }

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool force,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerSettingRegisterNotification(in Guid setting, uint flags, DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS* recipient, out nint registration);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerSettingUnregisterNotification(nint registration);
}
