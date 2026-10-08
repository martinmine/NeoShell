using NeoShell.Interop.Native;

namespace NeoShell.Interop.Display;

/// <summary>
/// Whether the screen turns with the PC, as Quick Settings' rotation lock reads it (SettingsHandlers_PCDisplay's
/// GetRotationLockState, from <c>GetAutoRotationState</c>).
/// </summary>
/// <param name="IsSupported">The PC has an orientation sensor that can turn the screen.</param>
/// <param name="CanChange">The lock can be switched now: not docked, not a laptop's lid mode, one screen, a local session.</param>
/// <param name="IsLocked">The screen stays as it is.</param>
public readonly record struct AutoRotation(bool IsSupported, bool CanChange, bool IsLocked)
{
    private const int AR_DISABLED = 0x1;
    private const int AR_REMOTESESSION = 0x4;
    private const int AR_MULTIMON = 0x8;
    private const int AR_NOSENSOR = 0x10;
    private const int AR_NOT_SUPPORTED = 0x20;
    private const int AR_DOCKED = 0x40;
    private const int AR_LAPTOP = 0x80;

    public static AutoRotation Current() =>
        User32.GetAutoRotationState(out int state) ? From(state) : new AutoRotation(false, false, true);

    /// <summary>The AR_STATE flags read as Windows' handler reads them: locked unless it's free to turn.</summary>
    internal static AutoRotation From(int state)
    {
        if ((state & (AR_NOSENSOR | AR_NOT_SUPPORTED)) != 0)
            return new AutoRotation(false, false, true);

        const int Blocked = AR_REMOTESESSION | AR_MULTIMON | AR_DOCKED | AR_LAPTOP;
        return new AutoRotation(true, (state & Blocked) == 0, (state & (Blocked | AR_DISABLED)) != 0);
    }
}
