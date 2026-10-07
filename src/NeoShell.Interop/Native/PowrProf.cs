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

    /// <summary>The start of SYSTEM_POWER_CAPABILITIES (76 bytes in all), up to what NeoShell reads.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 76)]
    public struct SYSTEM_POWER_CAPABILITIES
    {
        [FieldOffset(3)] public byte SystemS1;
        [FieldOffset(4)] public byte SystemS2;
        [FieldOffset(5)] public byte SystemS3;
        [FieldOffset(6)] public byte SystemS4;
        [FieldOffset(8)] public byte HiberFilePresent;
        [FieldOffset(20)] public byte AoAc;
        [FieldOffset(22)] public byte HiberFileType;
    }

    /// <summary>A full hibernation file; a reduced one only serves Fast Startup.</summary>
    public const byte PowerHiberFileTypeFull = 2;

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GetPwrCapabilities(out SYSTEM_POWER_CAPABILITIES capabilities);

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

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerGetActiveScheme(nint rootPowerKey, out Guid* activeScheme);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadACValueIndex(nint rootPowerKey, Guid* scheme, in Guid subgroup, in Guid setting, out uint index);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadDCValueIndex(nint rootPowerKey, Guid* scheme, in Guid subgroup, in Guid setting, out uint index);
}
