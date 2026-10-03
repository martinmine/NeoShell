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
}
