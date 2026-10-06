using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

[GeneratedComInterface]
[Guid("b722bccb-4e68-101b-a2bc-00aa00404770")]
internal unsafe partial interface IOleCommandTarget
{
    [PreserveSig] int QueryStatus(Guid* commandGroup, uint commandCount, nint commands, nint commandText);
    [PreserveSig] int Exec(Guid* commandGroup, uint commandId, uint options, nint input, nint output);
}
