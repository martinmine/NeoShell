using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>
/// Starts packaged (MSIX/Store) apps. Opening <c>shell:AppsFolder\&lt;AUMID&gt;</c> does the same through a handler
/// that lives in Explorer, so without Explorer only this works.
/// </summary>
public static class PackagedApps
{
    private static readonly Guid CLSID_ApplicationActivationManager = new("45ba127d-10a8-46ea-8ab7-56ea9078943c");

    /// <summary>A packaged app's AppUserModelID is <c>&lt;package family name&gt;!&lt;app id&gt;</c>.</summary>
    public static bool IsPackagedAppId(string appUserModelId) => appUserModelId.Contains('!');

    /// <summary>
    /// Starts the app and returns its process ID, or throws. Blocks until the app has started, which for a UWP app
    /// without Explorer's view management never quite happens, so call it off the UI thread.
    /// </summary>
    public static uint Activate(string appUserModelId, string? arguments = null)
    {
        var manager = Ole32.Create<IApplicationActivationManager>(CLSID_ApplicationActivationManager, Ole32.CLSCTX_LOCAL_SERVER | Ole32.CLSCTX_INPROC_SERVER);
        Marshal.ThrowExceptionForHR(manager.ActivateApplication(appUserModelId, arguments, 0, out uint processId));
        return processId;
    }
}
