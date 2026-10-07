using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

public static unsafe class CurrentUser
{
    /// <summary>The user's full name (e.g. from their Microsoft account), or null if Windows has none.</summary>
    public static string? DisplayName()
    {
        char* buffer = stackalloc char[256];
        uint size = 256;
        return Secur32.GetUserNameEx(Secur32.NameDisplay, buffer, ref size) && size > 0 ? new string(buffer, 0, (int)size) : null;
    }

    /// <summary>The email of the Microsoft account the user signs in with, or null for a local account.</summary>
    public static string? MicrosoftAccount()
    {
        if (Netapi32.NetUserGetInfo(null, Environment.UserName, 24, out nint buffer) != Netapi32.NERR_Success)
            return null;
        try
        {
            var info = (Netapi32.USER_INFO_24*)buffer;
            return info->InternetIdentity != 0 && info->InternetPrincipalName != null ? new string(info->InternetPrincipalName) : null;
        }
        finally
        {
            Netapi32.NetApiBufferFree(buffer);
        }
    }
}
