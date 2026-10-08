using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;
using WinRT;

namespace NeoShell.Interop.Shell;

/// <summary>
/// One of Windows' own settings, through the handler Settings and Quick Settings use for it (night light, nearby
/// sharing, mobile hotspot): the same state, the same rules for whether the PC has it, and the same way of changing
/// it, in Explorer's absence too. <see cref="Changed"/> comes on Windows' threads.
/// </summary>
/// <remarks>
/// The handlers are Windows' in-process DLLs (see <see cref="ISettingItem"/>), free-threaded; some block for a moment
/// (the hotspot's asks the network service), so call them off the UI thread. Dispose before exiting: a change reported
/// while .NET shuts down would crash the process, and a handler still held then can keep it from ending.
/// </remarks>
public sealed class SystemSetting : IDisposable
{
    private readonly ISettingItem _item;
    private readonly long _token;
    private bool _disposed;

    private SystemSetting(ISettingItem item)
    {
        _item = item;
        if (item.AddSettingChanged(new SettingChangedHandler(() => Changed?.Invoke()), out long token) >= 0)
            _token = token;
    }

    /// <summary>Raised when anything about it changed: its value, whether it's applicable or enabled.</summary>
    public event Action? Changed;

    /// <summary>Whether the PC has it. Some settings only know a moment after they're opened, and say so with <see cref="Changed"/>.</summary>
    public bool IsApplicable => _item.GetIsApplicable(out byte value) >= 0 && value != 0;

    /// <summary>Whether it can be changed now.</summary>
    public bool IsEnabled => _item.GetIsEnabled(out byte value) >= 0 && value != 0;

    /// <summary>Opens a setting by its ID ("SystemSettings_Display_BlueLight_ManualToggleQuickAction"); null when this Windows has none.</summary>
    public static unsafe SystemSetting? Open(string id)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\SystemSettings\SettingId\{id}");
        // Only Windows' own handlers.
        if (key?.GetValue("DllPath") is not string path
            || !string.Equals(Path.GetDirectoryName(path), Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase)
            || !NativeLibrary.TryLoad(path, out nint library)
            || !NativeLibrary.TryGetExport(library, "GetSetting", out nint export))
        {
            return null;
        }

        nint name = String(id);
        try
        {
            nint item;
            if (((delegate* unmanaged<nint, nint*, int>)export)(name, &item) < 0 || item == 0)
                return null;
            // A wrapper of its own: Dispose releases it early.
            return new SystemSetting(ComPointer.TakeOwnershipUnique<ISettingItem>(item));
        }
        finally
        {
            Combase.WindowsDeleteString(name);
        }
    }

    /// <summary>Its value ("Value"), or another named one: a bool, number or string; null when it has none.</summary>
    public object? GetValue(string name = "Value") => Read(_item.GetValue, name);

    /// <summary>Sets its value. Throws when the handler refuses.</summary>
    public void SetValue(object value, string name = "Value") => Write(_item.SetValue, name, value);

    /// <summary>A property of a quick action ("QuickActionIsActive", "QuickActionStatus"); null when it has none.</summary>
    public object? GetProperty(string name) => Read(_item.GetProperty, name);

    /// <summary>Sets a property of a quick action ("Value" switches the mobile hotspot). Throws when the handler refuses.</summary>
    public void SetProperty(string name, object value) => Write(_item.SetProperty, name, value);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_token != 0)
            _item.RemoveSettingChanged(_token);
        Changed = null;
        // Let go of the handler's object now, not when .NET exits: the display brightness handler, still held then,
        // leaves the exiting process waiting on a call to Windows that never returns.
        ((ComObject)(object)_item).FinalRelease();
    }

    private delegate int Getter(nint name, out nint value);

    private static object? Read(Getter get, string name)
    {
        nint hstring = String(name);
        try
        {
            if (get(hstring, out nint value) < 0 || value == 0)
                return null;
            try
            {
                return MarshalInspectable<object>.FromAbi(value);
            }
            finally
            {
                Marshal.Release(value);
            }
        }
        finally
        {
            Combase.WindowsDeleteString(hstring);
        }
    }

    private static void Write(Func<nint, nint, int> set, string name, object value)
    {
        nint hstring = String(name);
        nint boxed = MarshalInspectable<object>.FromManaged(value);
        try
        {
            Marshal.ThrowExceptionForHR(set(hstring, boxed));
        }
        finally
        {
            MarshalInspectable<object>.DisposeAbi(boxed);
            Combase.WindowsDeleteString(hstring);
        }
    }

    private static nint String(string text)
    {
        Marshal.ThrowExceptionForHR(Combase.WindowsCreateString(text, (uint)text.Length, out nint hstring));
        return hstring;
    }
}

[GeneratedComClass]
internal sealed partial class SettingChangedHandler(Action onChange) : ISettingChangedHandler
{
    public int Invoke(nint sender, nint name)
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
