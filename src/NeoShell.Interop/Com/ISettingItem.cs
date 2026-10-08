using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// A setting of Windows' Settings handlers (SystemSettings.DataModel): what Settings and Quick Settings (ShellHost's
// QuickActionsDataModel) read and change. Each handler DLL, listed under HKLM\SOFTWARE\Microsoft\SystemSettings\
// SettingId\<id> (DllPath), exports GetSetting(HSTRING id, ISettingItem** item). Undocumented and without metadata:
// the IID and method order are from the DLLs' symbols (Windows 11 25H2). Strings are HSTRINGs, values IInspectables
// (boxed by Windows.Foundation.PropertyValue).

[GeneratedComInterface]
[Guid("40c037cc-d8bf-489e-8697-d66baa3221bf")]
internal partial interface ISettingItem : IInspectable
{
    [PreserveSig] int GetId(out nint id);
    [PreserveSig] int GetSettingType(out int type);
    [PreserveSig] int GetIsSetByGroupPolicy(out byte value);
    /// <summary>False while it can't be changed (rotation lock without an orientation sensor).</summary>
    [PreserveSig] int GetIsEnabled(out byte value);
    /// <summary>Whether the PC has it at all; some settings learn it a moment after they're created.</summary>
    [PreserveSig] int GetIsApplicable(out byte value);
    [PreserveSig] int GetDescription(out nint description);
    [PreserveSig] int GetIsUpdating(out byte value);
    /// <param name="name">"Value" for the setting's own value.</param>
    [PreserveSig] int GetValue(nint name, out nint value);
    [PreserveSig] int SetValue(nint name, nint value);
    /// <summary>The quick actions written as C++/CX classes (mobile hotspot, VPN) keep their state in properties.</summary>
    [PreserveSig] int GetProperty(nint name, out nint value);
    [PreserveSig] int SetProperty(nint name, nint value);
    [PreserveSig] int Invoke();
    [PreserveSig] int AddSettingChanged(ISettingChangedHandler handler, out long token);
    [PreserveSig] int RemoveSettingChanged(long token);
}

/// <summary><c>TypedEventHandler&lt;Object, String&gt;</c>: the name of what changed ("Value", "IsApplicable").</summary>
[GeneratedComInterface]
[Guid("dc471c97-550a-573c-9a01-f94a67aa3850")]
internal partial interface ISettingChangedHandler
{
    [PreserveSig] int Invoke(nint sender, nint name);
}
