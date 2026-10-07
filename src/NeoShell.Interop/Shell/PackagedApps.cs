using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Xml;
using System.Xml.Linq;
using NeoShell.Interop.Com;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Native;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Management.Deployment;
using Windows.Storage;
using Windows.Storage.Streams;

namespace NeoShell.Interop.Shell;

/// <summary>
/// Starts packaged (MSIX/Store) apps and finds their logos. Opening <c>shell:AppsFolder\&lt;AUMID&gt;</c> does the same through a handler
/// that lives in Explorer, so without Explorer only this works.
/// </summary>
public static partial class PackagedApps
{
    private static readonly Guid CLSID_ApplicationActivationManager = new("45ba127d-10a8-46ea-8ab7-56ea9078943c");
    private static readonly Guid CLSID_PackageDebugSettings = new("b1aec16f-2383-4852-b0e9-8f0b1dc66b4d");
    private const string SettingsAppId = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

    /// <summary>A packaged app's AppUserModelID is <c>&lt;package family name&gt;!&lt;app id&gt;</c>.</summary>
    public static bool IsPackagedAppId(string appUserModelId) => appUserModelId.Contains('!');

    /// <summary>
    /// Starts the app and returns its process ID, or throws. Blocks until the app has started, which for a UWP app
    /// without Explorer's view management never quite happens, so call it off the UI thread.
    /// </summary>
    /// <param name="logUsage">
    /// Records a packaged desktop app's start under its executable in <see cref="UserAssist"/>, as starts from
    /// Explorer's Start and taskbar are. The app's own entry is <see cref="UserAssist.RecordLaunch"/>'s.
    /// </param>
    public static uint Activate(string appUserModelId, string? arguments = null, bool logUsage = false)
    {
        var manager = Ole32.Create<IApplicationActivationManager>(CLSID_ApplicationActivationManager, Ole32.CLSCTX_LOCAL_SERVER | Ole32.CLSCTX_INPROC_SERVER);
        if (logUsage)
            SetSite((IObjectWithSite)manager, new LogUsageSite());
        Marshal.ThrowExceptionForHR(manager.ActivateApplication(appUserModelId, arguments, 0, out uint processId));
        return processId;
    }

    private static unsafe void SetSite(IObjectWithSite target, IOleServiceProvider site)
    {
        void* pointer = ComInterfaceMarshaller<IOleServiceProvider>.ConvertToUnmanaged(site);
        try
        {
            Marshal.ThrowExceptionForHR(target.SetSite((nint)pointer));
        }
        finally
        {
            ComInterfaceMarshaller<IOleServiceProvider>.Free(pointer);
        }
    }

    /// <summary>
    /// The site Explorer's broker and ShellExecuteEx's <c>SEE_MASK_FLAG_LOG_USAGE</c> give the activation: asked for
    /// <c>SID_ExecuteLogUsage</c> (twinui.appcore's <c>DesktopAppXActivator::GetLogUsageFromSite</c>), it answers,
    /// and the activator then starts a packaged desktop app with usage logging. UWP activation doesn't ask.
    /// </summary>
    [GeneratedComClass]
    private sealed unsafe partial class LogUsageSite : IOleServiceProvider
    {
        private static readonly Guid SID_ExecuteLogUsage = new("582b888f-80d5-4bc4-9a6d-5d7a58efd60a");
        private const int E_NOINTERFACE = unchecked((int)0x80004002);

        int IOleServiceProvider.QueryService(Guid* service, Guid* iid, nint* result)
        {
            *result = 0;
            if (*service != SID_ExecuteLogUsage)
                return E_NOINTERFACE;

            // Only the answer counts; the object handed back is the site itself.
            void* site = ComInterfaceMarshaller<IOleServiceProvider>.ConvertToUnmanaged(this);
            int hr = Marshal.QueryInterface((nint)site, *iid, out *result);
            ComInterfaceMarshaller<IOleServiceProvider>.Free(site);
            return hr;
        }
    }

    /// <summary>
    /// Ends every process of the app's package at once, as Explorer's "End task" ends a UWP app (its windows belong
    /// to ApplicationFrameHost, which hosts other apps' too). Throws if that fails; call it off the UI thread.
    /// </summary>
    public static void EndAll(string appUserModelId)
    {
        string package = AppInfo.GetFromAppUserModelId(appUserModelId).Package.Id.FullName;
        var settings = Ole32.Create<IPackageDebugSettings>(CLSID_PackageDebugSettings, Ole32.CLSCTX_INPROC_SERVER);
        Marshal.ThrowExceptionForHR(settings.TerminateAllProcesses(package));
    }

    /// <summary>
    /// Opens the app's page in Settings (Installed apps › the app's advanced options), as Start's App settings does:
    /// Settings started with the arguments Explorer passes it, a system app's page under System components. Throws
    /// if Settings can't start; call it off the UI thread.
    /// </summary>
    public static void OpenAppSettings(string appUserModelId)
    {
        Package package = AppInfo.GetFromAppUserModelId(appUserModelId).Package;
        string page = package.SignatureKind == PackageSignatureKind.System
            ? "page=SettingsPageSystemComponents&target=SystemSettings_StorageSense_HiddenSystemComponentsAdvancedPageLink&invoke=true&parameter="
            : "page=SettingsPageInstalledApps&target=SystemSettings_StorageSense_HiddenAppAdvancedPageLink&invoke=true&parameter=";
        Activate(SettingsAppId, page + package.Id.FamilyName);
    }

    /// <summary>
    /// Opens Settings' Installed apps with its search box ready, where Start's Uninstall takes a desktop app. Throws
    /// if Settings can't start; call it off the UI thread.
    /// </summary>
    public static void OpenInstalledApps() =>
        Activate(SettingsAppId, "page=SettingsPageInstalledApps&target=SystemSettings_StorageSense_AppSizesListFilter");

    /// <summary>Removes the app's package for the current user, as Start's Uninstall does. Throws if that fails.</summary>
    public static async Task UninstallAsync(string appUserModelId)
    {
        string package = AppInfo.GetFromAppUserModelId(appUserModelId).Package.Id.FullName;
        DeploymentResult result = await new PackageManager().RemovePackageAsync(package);
        if (result.ExtendedErrorCode is { } error)
            throw new COMException(result.ErrorText, error.HResult);
    }

    /// <summary>
    /// The app's logo as its package draws it for <paramref name="size"/> pixels, the image Explorer's taskbar shows;
    /// null if the package has none for that size. The shell's icon for the size is scaled from a bigger image
    /// instead, which comes out a pixel off. Reads files, so call it off the UI thread.
    /// </summary>
    public static IconBitmap? GetLogo(string appUserModelId, int size)
    {
        if (!IsPackagedAppId(appUserModelId))
            return null;

        try
        {
            AppInfo app = AppInfo.GetFromAppUserModelId(appUserModelId);
            string folder = app.Package.InstalledLocation.Path;
            string? logo = ManifestLogo(XDocument.Load(Path.Combine(folder, "AppxManifest.xml")), app.Id);
            string? file = logo is null
                ? null
                : LogoCandidates(logo, size).Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
            return file is null ? null : Decode(file);
        }
        catch (Exception ex) when (ex is ArgumentException or COMException or IOException or UnauthorizedAccessException or XmlException)
        {
            // Not a packaged app after all, or one being updated or removed.
            return null;
        }
    }

    /// <summary>The app's <c>Square44x44Logo</c>, the base name of its taskbar and Start icons.</summary>
    internal static string? ManifestLogo(XDocument manifest, string appId) =>
        manifest.Descendants()
            .Where(e => e.Name.LocalName == "Application" && (string?)e.Attribute("Id") == appId)
            .Elements()
            .Where(e => e.Name.LocalName == "VisualElements")
            .Select(e => (string?)e.Attribute("Square44x44Logo"))
            .FirstOrDefault();

    /// <summary>
    /// Where the logo for a size is, by the resource naming convention: the plain ("unplated") variant the taskbar
    /// uses, else the plated one.
    /// </summary>
    internal static IEnumerable<string> LogoCandidates(string logo, int size)
    {
        string stem = Path.ChangeExtension(logo, null);
        string extension = Path.GetExtension(logo);
        yield return $"{stem}.targetsize-{size}_altform-unplated{extension}";
        yield return $"{stem}.targetsize-{size}{extension}";
    }

    private static IconBitmap Decode(string path)
    {
        StorageFile file = StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
        using IRandomAccessStreamWithContentType stream = file.OpenReadAsync().AsTask().GetAwaiter().GetResult();
        BitmapDecoder decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
        PixelDataProvider pixels = decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask().GetAwaiter().GetResult();
        return new IconBitmap((int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData());
    }
}
