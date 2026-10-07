using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Com;

// InputSwitch.dll's input switcher, undocumented: what Explorer's taskbar (through windowsudk.shellcommon) asks for the
// input method in front and to switch it. Explorer creates it as a desktop XAML client (7); showing its own flyout then
// needs a window band only Explorer may create, so NeoShell draws the flyout itself.

internal static class InputSwitch
{
    public static readonly Guid CLSID_InputSwitchControl = new("b9bc2a50-43c3-41aa-a086-5db14e184bae");

    /// <summary>The client type Explorer's taskbar uses: the input method of the app in front, switched for it.</summary>
    public const int DesktopXamlClient = 7;
}

/// <summary>The input method as the switcher reports it. Strings are the switcher's own copies (<c>CoTaskMemFree</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct InputSwitchProfile
{
    /// <summary>The keyboard layout's handle; the language in the low word.</summary>
    public int Hkl;
    /// <summary>"ENG": what the taskbar shows on the first line.</summary>
    public nint ShortLanguage;
    /// <summary>"English (United Kingdom)".</summary>
    public nint Language;
    /// <summary>"NO": what the taskbar shows on the second line.</summary>
    public nint ShortKeyboard;
    /// <summary>"Norwegian keyboard".</summary>
    public nint Keyboard;
    public int Unknown28;
    /// <summary>Non-zero for a text service (an IME); its strings then lack the short keyboard.</summary>
    public int IsTextService;
    public int Unknown30;
    public int Unknown34;
    public int Unknown38;
    /// <summary>"en-GB".</summary>
    public nint LanguageTag;
    public nint Unknown48;
    public int Unknown50;
    public int Unknown54;
    public int Unknown58;
    /// <summary>A text service's icon file.</summary>
    public nint IconFile;
    public long Unknown68;

    public static void Free(ref InputSwitchProfile profile)
    {
        foreach (nint text in (nint[])[profile.ShortLanguage, profile.Language, profile.ShortKeyboard, profile.Keyboard,
            profile.LanguageTag, profile.Unknown48, profile.IconFile])
        {
            Marshal.FreeCoTaskMem(text);
        }
        profile = default;
    }
}

/// <summary>An IME's mode as the switcher reports it. Strings and icon are the switcher's copies for the caller to free.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct InputSwitchImeMode
{
    /// <summary>"Right-click to open IME options".</summary>
    public nint ToolTip;
    public nint Icon;
    public int Unknown10;
    public int Unknown14;
    /// <summary>The mode as a Segoe Fluent Icons glyph: U+E986 "あ", U+E97E "A".</summary>
    public nint Glyph;
    public long Unknown20;
}

[GeneratedComInterface]
[Guid("b9bc2a50-43c3-41aa-a082-5db14e184bae")]
internal unsafe partial interface IInputSwitchControl
{
    [PreserveSig] int Init(int clientType);
    [PreserveSig] int SetCallback(IInputSwitchCallback? callback);
    [PreserveSig] int ShowInputSwitch(nint rect);
    [PreserveSig] int GetProfileCount(out uint count, out int imeVisible);
    [PreserveSig] int GetCurrentProfile(InputSwitchProfile* profile);
    [PreserveSig] int RegisterHotkeys();
    /// <param name="action">0 for a click (the IME switches its mode), 1 for a right-click (its menu).</param>
    /// <param name="point">The pointer, in screen pixels: x in the low half, y in the high.</param>
    /// <param name="anchor">The indicator's bounds in screen pixels, which the IME's menu opens against.</param>
    [PreserveSig] int ClickImeModeItem(int action, long point, in User32.RECT anchor);
    [PreserveSig] int ClickImeModeItemWithAnchor(int action, nint rect);
    [PreserveSig] int ForceHide();
    [PreserveSig] int ShowTouchKeyboardInputSwitch(nint rect, int a, int b, uint c, int d);
    [PreserveSig] int GetContextFlags(out uint flags);
    [PreserveSig] int SetContextOverrideMode(int mode);
    [PreserveSig] int GetCurrentImeModeItem(InputSwitchImeMode* mode);
    /// <param name="tip">"0809:00000414" (language:keyboard layout) or "0411:{clsid}{profile}" (a text service).</param>
    [PreserveSig] int ActivateInputProfile([MarshalAs(UnmanagedType.LPWStr)] string tip);
}

[GeneratedComInterface]
[Guid("b9bc2a50-43c3-41aa-a083-5db14e184bae")]
internal unsafe partial interface IInputSwitchCallback
{
    [PreserveSig] int OnUpdateProfile(InputSwitchProfile* profile);
    [PreserveSig] int OnUpdateTsfFloatingFlags(uint flags);
    [PreserveSig] int OnProfileCountChange(uint count, int imeVisible);
    [PreserveSig] int OnShowHide(int shown, int a, int b);
    [PreserveSig] int OnImeModeItemUpdate(InputSwitchImeMode* mode);
    [PreserveSig] int OnModalitySelected(int modality);
    [PreserveSig] int OnContextFlagsChange(uint flags);
    [PreserveSig] int OnTouchKeyboardManualInvoke();
}
