using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Windows' badge store as Explorer's taskbar reads it (Taskbar.dll, CTaskBand::UpdateBadgeAsync): the Windows Runtime
// class WindowsUdk.UI.StartScreen.BadgeProvider in windowsudk.shellcommon.dll, in-process. It subscribes to the
// notification platform's badge updates for each app it's asked about. Undocumented and without metadata: the IIDs and
// method order are from the DLL's symbols (Windows 11 25H2).

[GeneratedComInterface]
[Guid("af86e2e0-b12d-4c6a-9c5a-d7aa65101e90")]
internal partial interface IInspectable
{
    [PreserveSig] int GetIids(out uint count, out nint iids);
    [PreserveSig] int GetRuntimeClassName(out nint className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);
}

[GeneratedComInterface]
[Guid("b873647c-6203-520d-a56d-8bd6eb51be66")]
internal partial interface IBadgeProviderStatics : IInspectable
{
    /// <param name="user">A <c>Windows.System.User</c>; 0 for the user the process runs as.</param>
    [PreserveSig] int GetForUser(nint user, out IBadgeProvider provider);
}

[GeneratedComInterface]
[Guid("dd6088ad-dd34-5ddb-8366-dd8b3eec9676")]
internal partial interface IBadgeProvider : IInspectable
{
    /// <summary>
    /// The app's badge, registering for its updates the first time: the value comes a moment later, with
    /// <see cref="IBadge.AddChanged"/>.
    /// </summary>
    /// <param name="appId">The app's AppUserModelID as an HSTRING.</param>
    [PreserveSig] int GetRegisteredBadge(nint appId, out IBadge badge);
}

[GeneratedComInterface]
[Guid("fa2230ad-ac27-5c67-a716-22d9d45639b4")]
internal partial interface IBadge : IInspectable
{
    /// <summary>0 none, 1 a number, 2 a glyph.</summary>
    [PreserveSig] int GetKind(out int kind);
    [PreserveSig] int GetNumber(out uint number);
    /// <summary>The glyph's <c>BadgeGlyphKind</c>, numbered as <see cref="Notifications.BadgeGlyph"/>.</summary>
    [PreserveSig] int GetGlyph(out int glyph);
    [PreserveSig] int AddChanged(IBadgeChangedHandler handler, out long token);
    [PreserveSig] int RemoveChanged(long token);
}

/// <summary><c>TypedEventHandler&lt;Badge, Object&gt;</c>.</summary>
[GeneratedComInterface]
[Guid("761a2d11-8a17-56ee-9ee2-5bcde8dce966")]
internal partial interface IBadgeChangedHandler
{
    [PreserveSig] int Invoke(nint sender, nint args);
}
