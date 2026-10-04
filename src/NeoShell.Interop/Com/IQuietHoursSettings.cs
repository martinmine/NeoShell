using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Quiet hours, the notification platform's profiles behind Do not disturb (Focus assist in Windows 10). Undocumented;
// it's what Explorer's own Do not disturb button switches. Only the first two methods are declared.

internal static class QuietHours
{
    public static readonly Guid CLSID_QuietHoursSettings = new("f53321fa-34f8-4b7f-b9a3-361877cb94cf");

    /// <summary>Every notification shows: Do not disturb is off.</summary>
    public const string Unrestricted = "Microsoft.QuietHoursProfile.Unrestricted";

    /// <summary>Only priority notifications show: what Windows 11's Do not disturb turns on.</summary>
    public const string PriorityOnly = "Microsoft.QuietHoursProfile.PriorityOnly";
}

[GeneratedComInterface]
[Guid("6bff4732-81ec-4ffb-ae67-b6c1bc29631f")]
internal partial interface IQuietHoursSettings
{
    /// <param name="profileId">A string the caller frees with <c>CoTaskMemFree</c>.</param>
    [PreserveSig] int GetUserSelectedProfile(out nint profileId);
    [PreserveSig] int SetUserSelectedProfile([MarshalAs(UnmanagedType.LPWStr)] string profileId);
}
