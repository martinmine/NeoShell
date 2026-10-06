using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

/// <summary>An AutoPlay handler registered by CLSID (<c>Handlers\&lt;name&gt;\CLSID</c>).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("c1fb73d0-ec3a-4ba2-b512-8cdb9187b6d1")]
internal partial interface IHWEventHandler
{
    [PreserveSig] int Initialize(string parameters);
    [PreserveSig] int HandleEvent(string deviceId, string altDeviceId, string eventType);
    [PreserveSig] int HandleEventWithContent(string deviceId, string altDeviceId, string eventType, string contentTypeHandler, nint dataObject);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("cfcc809f-295d-42e8-9ffc-424b33c487e6")]
internal partial interface IHWEventHandler2 : IHWEventHandler
{
    [PreserveSig] int HandleEventWithHWND(string deviceId, string altDeviceId, string eventType, nint owner);
}

/// <summary>What an app registers in the running object table to stop AutoPlay while it runs (a disc burner).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("ddefe873-6997-4e68-be26-39b633adbe12")]
internal partial interface IQueryCancelAutoPlay
{
    /// <summary>S_FALSE cancels AutoPlay.</summary>
    [PreserveSig] int AllowAutoPlay(string path, uint contentType, string label, uint serialNumber);
}

/// <summary>The start of <c>IRunningObjectTable</c>, up to the one call used.</summary>
[GeneratedComInterface]
[Guid("00000010-0000-0000-c000-000000000046")]
internal partial interface IRunningObjectTable
{
    [PreserveSig] int Register(uint flags, nint unknown, nint moniker, out uint cookie);
    [PreserveSig] int Revoke(uint cookie);
    [PreserveSig] int IsRunning(nint moniker);
    [PreserveSig] int GetObject(nint moniker, out nint unknown);
}
