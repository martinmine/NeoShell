using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Power;

/// <summary>
/// Windows 11's energy saver (24H2 and later). <see cref="Changed"/> comes from a power-management thread.
/// </summary>
/// <remarks>
/// Its state is read with the documented <c>GUID_ENERGY_SAVER_STATUS</c> notification. There is no public way to turn
/// it on or off: Windows' own Energy saver setting publishes a WNF state, which is what <see cref="Set"/> does.
/// </remarks>
public sealed unsafe class EnergySaver : IDisposable
{
    private static readonly Guid s_energySaverStatus = new("550e8400-e29b-41d4-a716-446655440000");
    // WNF_PO_ENERGY_SAVER_OVERRIDE, as SettingsHandlers_OneCore_BatterySaver.dll publishes it: 1 on, 2 off.
    private const ulong EnergySaverOverride = 0x41C6013DA3BC3075;

    private readonly PowrProf.DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS* _parameters;
    private GCHandle _self;
    private nint _registration;
    private volatile int _status = -1;

    public EnergySaver()
    {
        _self = GCHandle.Alloc(this);
        // Windows keeps the pointer for as long as the registration lasts.
        _parameters = (PowrProf.DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS*)NativeMemory.Alloc((nuint)sizeof(PowrProf.DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS));
        _parameters->Callback = &OnNotification;
        _parameters->Context = (void*)GCHandle.ToIntPtr(_self);
        if (PowrProf.PowerSettingRegisterNotification(s_energySaverStatus, PowrProf.DEVICE_NOTIFY_CALLBACK, _parameters, out _registration) != 0)
            _registration = 0;
    }

    /// <summary>Raised when energy saver turns on or off.</summary>
    public event Action? Changed;

    /// <summary>False before Windows 11 24H2, which reports no energy saver status.</summary>
    public bool IsAvailable => _status >= 0;

    /// <summary>On in either of its levels (standard or high savings).</summary>
    public bool IsOn => _status > 0;

    /// <summary>Turns energy saver on or off until the user or Windows' own rules change it again. Throws on failure.</summary>
    public void Set(bool on)
    {
        int value = on ? 1 : 2;
        int status = Ntdll.RtlPublishWnfStateData(EnergySaverOverride, 0, &value, sizeof(int), 0);
        if (status < 0)
            throw new InvalidOperationException($"Publishing the energy saver state failed (NTSTATUS 0x{status:X8})");
    }

    public void Dispose()
    {
        if (_registration != 0)
            PowrProf.PowerSettingUnregisterNotification(_registration);
        _registration = 0;
        NativeMemory.Free(_parameters);
        _self.Free();
    }

    // Windows calls this once straight after registering, with the current state, then on each change.
    [UnmanagedCallersOnly]
    private static uint OnNotification(void* context, uint type, void* setting)
    {
        try
        {
            if (type == PowrProf.PBT_POWERSETTINGCHANGE && GCHandle.FromIntPtr((nint)context).Target is EnergySaver self)
            {
                var header = (PowrProf.POWERBROADCAST_SETTING*)setting;
                if (header->DataLength >= sizeof(uint))
                {
                    // ENERGY_SAVER_STATUS: off, standard or high savings.
                    self._status = (int)*(uint*)(header + 1);
                    self.Changed?.Invoke();
                }
            }
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 0;
    }
}
