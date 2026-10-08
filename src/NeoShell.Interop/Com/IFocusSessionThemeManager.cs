using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Windows' focus sessions behind the public FocusSessionManager (Windows.UI.Accessibility.dll, in-process, base trust):
// the Windows Runtime class Windows.Internal.Shell.FocusSessionThemeManager keeps the sessions in the cloud store and
// applies the focus "theme" (Settings → System → Focus) as one starts and ends. Explorer's immersive shell hosts the
// one that runs (twinui.pcshell's FocusSessionComponent, a service of CLSID_ImmersiveShell); FocusSessionManager reaches
// it through that service. Undocumented and without metadata: the IIDs (from GetIids) and method order (from the
// producers' vtables) are from Windows 11 25H2.

internal static class FocusSessionService
{
    public static readonly Guid CLSID_ImmersiveShell = new("c2f03a33-21f5-47fa-b4bb-156362a2f239");

    /// <summary>The service ID Windows.UI.Accessibility's ImmersiveShellFocusSingletonConnector asks for.</summary>
    public static readonly Guid SID_FocusSessionComponent = new("7cefd1e5-502a-428c-a8c2-6d31dae7008f");
}

[GeneratedComInterface]
[Guid("8c333f42-ffc0-53e2-856d-7f1b7b2bc363")]
internal partial interface IFocusSessionThemeManagerStatics : IInspectable
{
    /// <summary>This process's own manager.</summary>
    [PreserveSig] int GetDefault(out IFocusSessionThemeManager manager);
}

[GeneratedComInterface]
[Guid("ae042191-5097-53c8-91a6-f4c39155ebb0")]
internal partial interface IFocusSessionThemeManager : IInspectable
{
    /// <summary>The theme in force: <see cref="GetOffThemeId"/> unless a session runs.</summary>
    [PreserveSig] int GetCurrentThemeId(out Guid id);
    [PreserveSig] int GetOffThemeId(out Guid id);
    /// <summary>The theme Settings' Focus page edits, which a session puts in force.</summary>
    [PreserveSig] int GetDefaultThemeId(out Guid id);
    /// <summary>Ends each session on time from now on (the immersive shell's component calls it once).</summary>
    [PreserveSig] int InitializeTimer();
    /// <param name="end">A <c>Windows.Foundation.DateTime</c>: a FILETIME in UTC.</param>
    [PreserveSig] int AddSession(Guid themeId, long end, out Guid sessionId);
    [PreserveSig] int RemoveSession(Guid sessionId);
    [PreserveSig] int RemoveAllSessions();
}

[GeneratedComInterface]
[Guid("b4c18645-20cf-5aa6-98aa-cda3e700ff51")]
internal partial interface IFocusSessionComponent : IInspectable
{
    [PreserveSig] int GetThemeManager(out IFocusSessionThemeManager manager);
}

[GeneratedComInterface]
[Guid("15138294-96dc-5c76-be82-79359feab71f")]
internal partial interface IFocusSessionActiveThemeFactory : IInspectable
{
    /// <summary>The theme of that ID, as Settings' Focus page last saved it.</summary>
    [PreserveSig] int CreateInstance(Guid id, out IFocusSessionTheme theme);
}

/// <summary>Settings → System → Focus: what a session does. Booleans are bytes.</summary>
[GeneratedComInterface]
[Guid("a65b6a64-e9b2-5435-8b78-2372f3ef4c41")]
internal partial interface IFocusSessionTheme : IInspectable
{
    [PreserveSig] int GetId(out Guid id);
    /// <summary>"Show the timer in the Clock app".</summary>
    [PreserveSig] int GetIsStartFocusTimerEnabled(out byte value);
    [PreserveSig] int PutIsStartFocusTimerEnabled(byte value);
    [PreserveSig] int GetIsHideTaskbarBadgesEnabled(out byte value);
    [PreserveSig] int PutIsHideTaskbarBadgesEnabled(byte value);
    [PreserveSig] int GetIsHideTaskbarFlashesEnabled(out byte value);
    [PreserveSig] int PutIsHideTaskbarFlashesEnabled(byte value);
    /// <summary>"Turn on do not disturb".</summary>
    [PreserveSig] int GetIsMuteNotificationsEnabled(out byte value);
}
