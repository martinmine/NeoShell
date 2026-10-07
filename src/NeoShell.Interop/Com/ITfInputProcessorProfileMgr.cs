using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// The text services framework's input profiles: the keyboard layouts and text services (IMEs) the user has enabled.
// Only the methods NeoShell calls are declared, with the ones before them as placeholders.

internal static class TextServices
{
    public static readonly Guid CLSID_TF_InputProcessorProfiles = new("33c53a50-f456-4884-b049-85fd643ecfed");

    public const uint TF_PROFILETYPE_INPUTPROCESSOR = 1;
    public const uint TF_PROFILETYPE_KEYBOARDLAYOUT = 2;
    public const uint TF_IPP_FLAG_ENABLED = 2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TF_INPUTPROCESSORPROFILE
{
    public uint dwProfileType;
    public ushort langid;
    public Guid clsid;
    public Guid guidProfile;
    public Guid catid;
    public nint hklSubstitute;
    public uint dwCaps;
    public nint hkl;
    public uint dwFlags;
}

[GeneratedComInterface]
[Guid("71c6e74c-0f28-11d8-a82a-00065b84435c")]
internal partial interface ITfInputProcessorProfileMgr
{
    [PreserveSig] int ActivateProfile();
    [PreserveSig] int DeactivateProfile();
    [PreserveSig] int GetProfile();
    /// <param name="langid">0 for every language.</param>
    [PreserveSig] int EnumProfiles(ushort langid, out IEnumTfInputProcessorProfiles profiles);
}

[GeneratedComInterface]
[Guid("71c6e74d-0f28-11d8-a82a-00065b84435c")]
internal unsafe partial interface IEnumTfInputProcessorProfiles
{
    [PreserveSig] int Clone();
    [PreserveSig] int Next(uint count, TF_INPUTPROCESSORPROFILE* profiles, out uint fetched);
}

[GeneratedComInterface]
[Guid("1f02b6c5-7842-4ee6-8a0b-9a24183a95ca")]
internal partial interface ITfInputProcessorProfiles
{
    [PreserveSig] int Register();
    [PreserveSig] int Unregister();
    [PreserveSig] int AddLanguageProfile();
    [PreserveSig] int RemoveLanguageProfile();
    [PreserveSig] int EnumInputProcessorInfo();
    [PreserveSig] int GetDefaultLanguageProfile();
    [PreserveSig] int SetDefaultLanguageProfile();
    [PreserveSig] int ActivateLanguageProfile();
    [PreserveSig] int GetActiveLanguageProfile();
    /// <param name="description">A BSTR the caller frees.</param>
    [PreserveSig] int GetLanguageProfileDescription(in Guid clsid, ushort langid, in Guid profile, out nint description);
}
