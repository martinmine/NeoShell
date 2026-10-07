using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Shell32's launch history (CLSID_UserAssist; not in the SDK, read from shell32's symbols). It is what Explorer's
// Start, taskbar and ShellExecuteEx's SEE_MASK_FLAG_LOG_USAGE record app starts through. The methods after the last
// called one are left out.

[GeneratedComInterface]
[Guid("49b36d57-5fd2-45a7-981b-06028d577a47")]
internal partial interface IUserAssist
{
    [PreserveSig]
    int FireEvent(in Guid group, int uaEvent, [MarshalAs(UnmanagedType.LPWStr)] string path, uint elapsedMilliseconds);
}
