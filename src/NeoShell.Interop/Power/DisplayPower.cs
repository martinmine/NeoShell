using System.Runtime.InteropServices;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Power;

/// <summary>
/// Whether the display of this session is on (dimmed counts as on), from the documented
/// <c>GUID_SESSION_DISPLAY_STATUS</c> notification. <see cref="Changed"/> comes from a power-management thread.
/// </summary>
public sealed unsafe class DisplayPower : IDisposable
{
    private static readonly Guid s_sessionDisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    private const uint DisplayOff = 0;

    private readonly PowrProf.DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS* _parameters;
    private GCHandle _self;
    private nint _registration;
    private volatile bool _isOn = true;

    public DisplayPower()
    {
        _self = GCHandle.Alloc(this);
        // Windows keeps the pointer for as long as the registration lasts.
        _parameters = (PowrProf.DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS*)NativeMemory.Alloc((nuint)sizeof(PowrProf.DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS));
        _parameters->Callback = &OnNotification;
        _parameters->Context = (void*)GCHandle.ToIntPtr(_self);
        if (PowrProf.PowerSettingRegisterNotification(s_sessionDisplayStatus, PowrProf.DEVICE_NOTIFY_CALLBACK, _parameters, out _registration) != 0)
            _registration = 0;
    }

    /// <summary>Raised when the display turns off or on.</summary>
    public event Action? Changed;

    /// <summary>On, or not known.</summary>
    public bool IsOn => _isOn;

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
            if (type == PowrProf.PBT_POWERSETTINGCHANGE && GCHandle.FromIntPtr((nint)context).Target is DisplayPower self)
            {
                var header = (PowrProf.POWERBROADCAST_SETTING*)setting;
                if (header->DataLength >= sizeof(uint))
                {
                    // Off, on or dimmed.
                    bool on = *(uint*)(header + 1) != DisplayOff;
                    if (on != self._isOn)
                    {
                        self._isOn = on;
                        self.Changed?.Invoke();
                    }
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
