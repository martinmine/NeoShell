using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// The battery icon as Explorer's taskbar draws it: the Windows Runtime class WindowsUdk.UI.Shell.PowerUX.BatteryIcon
// in windowsudk.shellcommon.dll, in-process. Undocumented; the IIDs and method order are from windowsudk.winmd
// (SystemApps\MicrosoftWindows.UndockedDevKit, Windows 11 25H2).

[GeneratedComInterface]
[Guid("ac6f3681-370a-5cb7-9299-7035a61b3cc2")]
internal partial interface IBatteryIconStatics : IInspectable
{
    /// <summary>The icon of the PC's batteries together.</summary>
    [PreserveSig] int GetLocalCompositeBatteryIcon(out IBatteryIcon icon);
}

[GeneratedComInterface]
[Guid("bcec5e4e-9004-5b9a-9a80-bdeffede9e23")]
internal partial interface IBatteryIcon : IInspectable
{
    /// <summary>Glyphs of an older icon font (U+E686…), which Windows 11's taskbar doesn't use.</summary>
    [PreserveSig] int GetIconData(out IBatteryIconData data);

    /// <summary>The glyphs of SysBatt Fluent Icons (U+F8D0…), the font of Windows 11's taskbar.</summary>
    [PreserveSig] int GetMobileIconData(out IBatteryIconData data);

    [PreserveSig] int AddIconDataChanged(IBatteryIconChangedHandler handler, out long token);

    [PreserveSig] int RemoveIconDataChanged(long token);
}

[GeneratedComInterface]
[Guid("0ba19e33-7a22-5799-9dfb-93a47d6c525b")]
internal partial interface IBatteryIconData : IInspectable
{
    /// <param name="glyph">An HSTRING the caller deletes.</param>
    [PreserveSig] int GetOutlineGlyph(out nint glyph);

    /// <param name="glyph">An HSTRING the caller deletes.</param>
    [PreserveSig] int GetFillGlyph(out nint glyph);

    /// <summary><c>BatteryIconFillColor</c>: Default, Adequate, Warning, Critical.</summary>
    [PreserveSig] int GetFillColor(out int color);

    [PreserveSig] int GetBatteryPercentage(out uint percent);

    /// <param name="name">An HSTRING the caller deletes.</param>
    [PreserveSig] int GetAccessibleName(out nint name);

    [PreserveSig] int GetIsBatteryPresent([MarshalAs(UnmanagedType.U1)] out bool present);
}

/// <summary><c>TypedEventHandler&lt;BatteryIcon, Object&gt;</c>.</summary>
[GeneratedComInterface]
[Guid("606a0150-9cae-5c8e-825b-cdb6a01e4a2e")]
internal partial interface IBatteryIconChangedHandler
{
    [PreserveSig] int Invoke(nint sender, nint args);
}
