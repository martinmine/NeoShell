using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Input;

/// <summary>An input method the user has enabled: a keyboard layout or a text service (an IME), for a language.</summary>
/// <param name="Id">What switching to it takes: "0809:00000414" or "0411:{clsid}{profile}", as in Windows' language list.</param>
/// <param name="LanguageTag">"en-GB".</param>
/// <param name="LanguageCode">"ENG": the language's three letters, as the flyout shows them.</param>
/// <param name="Language">"English (United Kingdom)".</param>
/// <param name="Keyboard">"Norwegian", "US", "Microsoft IME".</param>
/// <param name="Hkl">The keyboard layout's handle; 0 for a text service.</param>
/// <param name="TextServiceProfile">The text service's profile; empty for a keyboard layout.</param>
public sealed record InputMethod(
    string Id, string LanguageTag, string LanguageCode, string Language, string Keyboard, nint Hkl, Guid TextServiceProfile)
{
    public bool IsTextService => TextServiceProfile != Guid.Empty;
}

/// <summary>The input method of the app in front, as the switcher reports it.</summary>
/// <param name="LanguageCode">"ENG": the indicator's first line ("日本" for the Japanese IME).</param>
/// <param name="KeyboardCode">"NO": the indicator's second line; empty for a text service.</param>
/// <param name="Language">"English (United Kingdom)".</param>
/// <param name="Keyboard">"Norwegian keyboard", "Microsoft IME".</param>
/// <param name="LanguageTag">"en-GB", "ja".</param>
/// <param name="Hkl">The keyboard layout's handle; 0 for a text service.</param>
public sealed record CurrentInputMethod(
    string LanguageCode, string KeyboardCode, string Language, string Keyboard, string LanguageTag, nint Hkl, bool IsTextService);

/// <summary>An IME's mode: its glyph in Segoe Fluent Icons ("あ" U+E986, "A" U+E97E) and tooltip.</summary>
public sealed record ImeMode(string Glyph, string ToolTip);

/// <summary>
/// The input methods and the one in use in the app in front, through Windows' input switcher (InputSwitch.dll) as
/// Explorer's taskbar asks for them: it follows the foreground window, and switches the input method for it.
/// Create it on the UI thread; <see cref="Changed"/> comes on that thread.
/// </summary>
public sealed unsafe class InputMethods : IDisposable
{
    private const uint CLSCTX_INPROC_SERVER = 1;

    private readonly IInputSwitchControl _control;

    public InputMethods()
    {
        _control = Ole32.Create<IInputSwitchControl>(InputSwitch.CLSID_InputSwitchControl, CLSCTX_INPROC_SERVER);
        Marshal.ThrowExceptionForHR(_control.Init(InputSwitch.DesktopXamlClient));
        Marshal.ThrowExceptionForHR(_control.SetCallback(new InputSwitchNotifications(() => Changed?.Invoke())));
    }

    /// <summary>Raised when the input method in front, an IME's mode, or the enabled input methods change.</summary>
    public event Action? Changed;

    /// <summary>How many input methods are enabled: the taskbar shows the indicator for more than one.</summary>
    public int Count => _control.GetProfileCount(out uint count, out _) == 0 ? (int)count : 0;

    /// <summary>The input method in front; null when the switcher can't tell.</summary>
    public CurrentInputMethod? Current
    {
        get
        {
            InputSwitchProfile profile;
            if (_control.GetCurrentProfile(&profile) != 0)
                return null;
            try
            {
                return new CurrentInputMethod(
                    Text(profile.ShortLanguage), Text(profile.ShortKeyboard), Text(profile.Language), Text(profile.Keyboard),
                    Text(profile.LanguageTag), profile.Hkl, profile.IsTextService != 0);
            }
            finally
            {
                InputSwitchProfile.Free(ref profile);
            }
        }
    }

    /// <summary>The mode of the IME in front; null when it has none to show (a keyboard layout).</summary>
    public ImeMode? ImeMode
    {
        get
        {
            InputSwitchImeMode mode;
            if (_control.GetCurrentImeModeItem(&mode) != 0)
                return null;
            try
            {
                return mode.Glyph != 0 && Text(mode.Glyph) is { Length: > 0 } glyph ? new ImeMode(glyph, Text(mode.ToolTip)) : null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(mode.ToolTip);
                Marshal.FreeCoTaskMem(mode.Glyph);
                if (mode.Icon != 0)
                    User32.DestroyIcon(mode.Icon);
            }
        }
    }

    /// <summary>Switches the app in front (or every app, as Windows is set) to the input method.</summary>
    public void Activate(InputMethod method) => Marshal.ThrowExceptionForHR(_control.ActivateInputProfile(method.Id));

    /// <summary>
    /// A click on the IME's mode indicator, in screen pixels: the IME switches its mode. (A right-click, action 1,
    /// opens the IME's menu, which the switcher draws in a window band only Explorer may create.)
    /// </summary>
    public void ClickImeMode(PointInt32 pointer, RectInt32 anchor) =>
        Marshal.ThrowExceptionForHR(_control.ClickImeModeItem(0, (long)pointer.Y << 32 | (uint)pointer.X, User32.RECT.From(anchor)));

    /// <summary>The enabled input methods, in the order of the user's language list.</summary>
    /// <remarks>
    /// Read on a thread-pool thread, never the UI thread: there, TSF's profile objects share the thread's TSF state
    /// with WinUI's text boxes, and a text box's teardown as WinUI shuts down then corrupts the heap.
    /// </remarks>
    public static IReadOnlyList<InputMethod> Enabled() => Task.Run(ReadEnabled).GetAwaiter().GetResult();

    private static List<InputMethod> ReadEnabled()
    {
        var manager = Ole32.Create<ITfInputProcessorProfileMgr>(TextServices.CLSID_TF_InputProcessorProfiles, CLSCTX_INPROC_SERVER);
        Marshal.ThrowExceptionForHR(manager.EnumProfiles(0, out IEnumTfInputProcessorProfiles all));

        var methods = new List<InputMethod>();
        TF_INPUTPROCESSORPROFILE profile;
        while (all.Next(1, &profile, out uint fetched) == 0 && fetched == 1)
        {
            // A profile for no language in particular (langid 0) has no culture to show, as one Windows doesn't know.
            if ((profile.dwFlags & TextServices.TF_IPP_FLAG_ENABLED) == 0 || profile.langid == 0)
                continue;

            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(profile.langid);
            }
            catch (CultureNotFoundException)
            {
                continue;
            }
            string language = new Windows.Globalization.Language(culture.Name).DisplayName;
            string code = culture.ThreeLetterISOLanguageName.ToUpperInvariant();
            if (profile.dwProfileType == TextServices.TF_PROFILETYPE_KEYBOARDLAYOUT)
            {
                string layout = LayoutId(profile.hkl);
                methods.Add(new InputMethod(
                    $"{profile.langid:X4}:{layout}", culture.Name, code, language, LayoutName(layout), profile.hkl, Guid.Empty));
            }
            else
            {
                string id = $"{profile.langid:X4}:{profile.clsid.ToString("B").ToUpperInvariant()}{profile.guidProfile.ToString("B").ToUpperInvariant()}";
                methods.Add(new InputMethod(
                    id, culture.Name, code, language, Description((ITfInputProcessorProfiles)manager, profile), 0, profile.guidProfile));
            }
        }
        return methods;
    }

    public void Dispose() => _control.SetCallback(null);

    /// <summary>
    /// The keyboard layout's ID ("00000414") from its handle: the device word is the layout's language, or for a
    /// layout that isn't a language's main one (0xFnnn), the "Layout Id" the registry gives it.
    /// </summary>
    internal static string LayoutId(nint hkl, Func<string, string?>? layoutWithId = null)
    {
        int device = (int)((ulong)hkl >> 16) & 0xFFFF;
        if ((device & 0xF000) != 0xF000)
            return $"{device:X8}";

        return (layoutWithId ?? LayoutWithId)($"{device & 0x0FFF:X4}") ?? $"{(int)hkl & 0xFFFF:X8}";
    }

    private static string? LayoutWithId(string layoutId)
    {
        using RegistryKey? layouts = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts");
        foreach (string id in layouts?.GetSubKeyNames() ?? [])
        {
            using RegistryKey? layout = layouts!.OpenSubKey(id);
            if (string.Equals(layout?.GetValue("Layout Id") as string, layoutId, StringComparison.OrdinalIgnoreCase))
                return id.ToUpperInvariant();
        }
        return null;
    }

    // "Norwegian", "US": the layout's display name, as the flyout shows it.
    private static string LayoutName(string layoutId)
    {
        using RegistryKey? layout = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\{layoutId}");
        if (layout?.GetValue("Layout Display Name") is string display)
        {
            char* buffer = stackalloc char[256];
            if (Shlwapi.SHLoadIndirectString(Environment.ExpandEnvironmentVariables(display), buffer, 256, 0) == 0)
                return new string(buffer);
        }
        return layout?.GetValue("Layout Text") as string ?? layoutId;
    }

    private static string Description(ITfInputProcessorProfiles profiles, TF_INPUTPROCESSORPROFILE profile)
    {
        if (profiles.GetLanguageProfileDescription(profile.clsid, profile.langid, profile.guidProfile, out nint description) != 0)
            return "";
        try
        {
            return Marshal.PtrToStringBSTR(description);
        }
        finally
        {
            Marshal.FreeBSTR(description);
        }
    }

    private static string Text(nint text) => text == 0 ? "" : Marshal.PtrToStringUni(text) ?? "";
}

// Passes the switcher's news on; the owner reads the state again.
[GeneratedComClass]
internal sealed unsafe partial class InputSwitchNotifications(Action onChange) : IInputSwitchCallback
{
    public int OnUpdateProfile(InputSwitchProfile* profile) => Raise();
    public int OnUpdateTsfFloatingFlags(uint flags) => 0;
    public int OnProfileCountChange(uint count, int imeVisible) => Raise();
    public int OnShowHide(int shown, int a, int b) => 0;
    public int OnImeModeItemUpdate(InputSwitchImeMode* mode) => Raise();
    public int OnModalitySelected(int modality) => 0;
    public int OnContextFlagsChange(uint flags) => Raise();
    public int OnTouchKeyboardManualInvoke() => 0;

    private int Raise()
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
