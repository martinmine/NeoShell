using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// The object behind PowerShell's Export-StartLayout (StartTileData.dll), declared as the StartLayout module declares
// it. Methods NeoShell never calls are declared without parameters: only their place in the vtable matters.

[GeneratedComInterface]
[Guid("0BAC4102-61E9-48A5-93DD-D295ABA65369")]
internal partial interface IStartLayoutCmdlet
{
    [PreserveSig] int ExportStartLayout([MarshalAs(UnmanagedType.BStr)] string path);
    [PreserveSig] int ExportStartLayoutWithDesktopApplicationIDs();
    [PreserveSig] int ValidateLayoutFile();
    [PreserveSig] int ExportEdgeAssets();
}
