using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;

namespace NeoShell.Interop.Shell;

/// <summary>The fits of <c>IDesktopWallpaper::SetPosition</c> (<c>DESKTOP_WALLPAPER_POSITION</c>).</summary>
public enum DesktopWallpaperPosition { Center, Tile, Stretch, Fit, Fill, Span }

/// <summary>
/// Serves <c>CLSID_DesktopWallpaper</c>, the object Settings, "Set as desktop background" and apps call to change the
/// wallpaper. It has no server registered on disk: Explorer's desktop registers its class object while it runs, so
/// without Explorer every call failed until NeoShell, as the shell, registers its own. Calls arrive on the thread that
/// registered (an STA's message loop). The subclass does the work; throwing an exception returns its HRESULT.
/// </summary>
public abstract unsafe partial class DesktopWallpaperServer : IDisposable
{
    private static readonly Guid ClassId = new("c2cf3110-460e-4fc1-b9d0-8a1c0c9cc4bd");
    private const int E_FAIL = unchecked((int)0x80004005);
    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const int E_POINTER = unchecked((int)0x80004003);
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);

    private uint _cookie;

    /// <summary>Starts serving the class to other processes; throws if COM refuses.</summary>
    public void Register()
    {
        var factory = new ClassFactory(new WallpaperObject(this));
        nint unknown = (nint)ComInterfaceMarshaller<IClassFactory>.ConvertToUnmanaged(factory);
        try
        {
            Marshal.ThrowExceptionForHR(Ole32.CoRegisterClassObject(
                ClassId, unknown, Ole32.CLSCTX_LOCAL_SERVER, Ole32.REGCLS_MULTIPLEUSE, out _cookie));
        }
        finally
        {
            Marshal.Release(unknown); // COM holds its own reference now
        }
    }

    public void Dispose()
    {
        if (_cookie != 0)
            Ole32.CoRevokeClassObject(_cookie);
        _cookie = 0;
        GC.SuppressFinalize(this);
    }

    /// <param name="monitorPath">The monitor's <see cref="DisplayMonitor.DevicePath"/>, or null for every monitor.</param>
    /// <param name="path">The image; empty for none (the background colour).</param>
    protected abstract void SetWallpaper(string? monitorPath, string path);

    /// <summary>The image on a monitor, or with null the one on every monitor ("" when they differ).</summary>
    protected abstract string GetWallpaper(string? monitorPath);

    /// <summary>The background colour as a <c>COLORREF</c> (0x00BBGGRR).</summary>
    protected abstract uint BackgroundColor { get; set; }

    protected abstract DesktopWallpaperPosition Position { get; set; }

    /// <summary>Starts a slideshow of the images in these folders or files.</summary>
    protected abstract void SetSlideshow(IReadOnlyList<string> paths);

    /// <summary>The slideshow's folders or files; empty when there is no slideshow.</summary>
    protected abstract IReadOnlyList<string> GetSlideshow();

    /// <param name="Interval">Milliseconds between pictures.</param>
    protected abstract (bool Shuffle, uint Interval) SlideshowOptions { get; set; }

    /// <summary>Shows the next pictures; throws when there is no slideshow.</summary>
    protected abstract void AdvanceSlideshow();

    /// <summary>False after <c>Enable(FALSE)</c>: the wallpaper is off and the background colour shows.</summary>
    protected abstract bool Enabled { get; set; }

    protected abstract bool SlideshowRunning { get; }

    private static string? Text(char* text) => text is null ? null : new string(text);

    private static DisplayMonitor? FindMonitor(string path) =>
        DisplayMonitor.GetAll().FirstOrDefault(monitor => string.Equals(monitor.DevicePath, path, StringComparison.OrdinalIgnoreCase));

    [GeneratedComClass]
    private sealed partial class ClassFactory(WallpaperObject instance) : IClassFactory
    {
        public int CreateInstance(nint outer, Guid* iid, nint* result)
        {
            if (result is null)
                return E_POINTER;
            *result = 0;
            if (outer != 0)
                return CLASS_E_NOAGGREGATION;

            nint unknown = (nint)ComInterfaceMarshaller<IDesktopWallpaper>.ConvertToUnmanaged(instance);
            int hr = Marshal.QueryInterface(unknown, in *iid, out *result);
            Marshal.Release(unknown);
            return hr;
        }

        public int LockServer(int doLock) => 0;
    }

    [GeneratedComClass]
    private sealed partial class EmptyItemArray : IShellItemArray
    {
        public int BindToHandler(nint bindContext, Guid* handler, Guid* iid, nint* result) => E_NOTIMPL;
        public int GetPropertyStore(int flags, Guid* iid, nint* store) => E_NOTIMPL;
        public int GetPropertyDescriptionList(nint keyType, Guid* iid, nint* list) => E_NOTIMPL;
        public int GetAttributes(int attributeFlags, uint mask, uint* attributes) => E_NOTIMPL;

        public int GetCount(uint* count)
        {
            if (count is null)
                return E_POINTER;
            *count = 0;
            return 0;
        }

        public int GetItemAt(uint index, nint* item) => E_INVALIDARG;
        public int EnumItems(nint* items) => E_NOTIMPL;
    }

    /// <summary>Turns the COM calls into the subclass's .NET calls, as Explorer's desktop answers them.</summary>
    [GeneratedComClass]
    private sealed partial class WallpaperObject(DesktopWallpaperServer server) : IDesktopWallpaper
    {
        public int SetWallpaper(char* monitorId, char* wallpaper)
        {
            if (wallpaper is null)
                return E_POINTER;
            string? monitor = Text(monitorId);
            // Explorer accepts a monitor it doesn't know and changes nothing.
            if (monitor is not null && FindMonitor(monitor) is null)
                return 0;
            string path = new(wallpaper);
            return Run(() => server.SetWallpaper(monitor, path));
        }

        public int GetWallpaper(char* monitorId, char** wallpaper) =>
            wallpaper is null ? E_POINTER : Run(() => *wallpaper = Allocate(server.GetWallpaper(Text(monitorId))));

        public int GetMonitorDevicePathAt(uint monitorIndex, char** monitorId)
        {
            if (monitorId is null)
                return E_POINTER;
            *monitorId = null;
            IReadOnlyList<DisplayMonitor> monitors = DisplayMonitor.GetAll();
            if (monitorIndex >= monitors.Count)
                return E_FAIL;
            *monitorId = Allocate(monitors[(int)monitorIndex].DevicePath);
            return 0;
        }

        public int GetMonitorDevicePathCount(uint* count)
        {
            if (count is null)
                return E_POINTER;
            *count = (uint)DisplayMonitor.GetAll().Count;
            return 0;
        }

        public int GetMonitorRECT(char* monitorId, User32.RECT* displayRect)
        {
            if (monitorId is null || displayRect is null)
                return E_POINTER;
            if (FindMonitor(new string(monitorId)) is not { } monitor)
                return E_INVALIDARG;
            *displayRect = User32.RECT.From(monitor.Bounds);
            return 0;
        }

        public int SetBackgroundColor(uint color) => Run(() => server.BackgroundColor = color);

        public int GetBackgroundColor(uint* color) => color is null ? E_POINTER : Run(() => *color = server.BackgroundColor);

        public int SetPosition(int position) =>
            Enum.IsDefined((DesktopWallpaperPosition)position)
                ? Run(() => server.Position = (DesktopWallpaperPosition)position)
                : E_INVALIDARG;

        public int GetPosition(int* position) => position is null ? E_POINTER : Run(() => *position = (int)server.Position);

        public int SetSlideshow(nint items)
        {
            // Explorer takes no items as success, changing nothing.
            if (items == 0)
                return 0;

            var paths = new List<string>();
            IShellItemArray array = ComInterfaceMarshaller<IShellItemArray>.ConvertToManaged((void*)items)!;
            uint count;
            int hr = array.GetCount(&count);
            for (uint i = 0; hr >= 0 && i < count; i++)
            {
                nint itemPointer;
                hr = array.GetItemAt(i, &itemPointer);
                if (hr < 0)
                    break;
                IShellItem item = ComPointer.TakeOwnership<IShellItem>(itemPointer);
                char* path;
                hr = item.GetDisplayName(ShellItems.SIGDN_FILESYSPATH, &path);
                if (hr >= 0)
                {
                    paths.Add(new string(path));
                    Marshal.FreeCoTaskMem((nint)path);
                }
            }
            return hr < 0 ? hr : Run(() => server.SetSlideshow(paths));
        }

        public int GetSlideshow(nint* items)
        {
            if (items is null)
                return E_POINTER;
            *items = 0;
            return Run(() =>
            {
                IReadOnlyList<string> paths = server.GetSlideshow();
                if (paths.Count == 0)
                {
                    // Explorer answers an empty array; Windows' own can't be empty.
                    *items = (nint)ComInterfaceMarshaller<IShellItemArray>.ConvertToUnmanaged(new EmptyItemArray());
                    return;
                }

                var idLists = new List<nint>();
                try
                {
                    foreach (string path in paths)
                    {
                        if (Shell32.SHParseDisplayName(path, 0, out nint idList, 0, out _) >= 0)
                            idLists.Add(idList);
                    }
                    nint[] array = [.. idLists];
                    fixed (nint* first = array)
                        Marshal.ThrowExceptionForHR(Shell32.SHCreateShellItemArrayFromIDLists((uint)array.Length, first, out *items));
                }
                finally
                {
                    foreach (nint idList in idLists)
                        Shell32.ILFree(idList);
                }
            });
        }

        public int SetSlideshowOptions(int options, uint slideshowTick) =>
            Run(() => server.SlideshowOptions = ((options & DSO_SHUFFLEIMAGES) != 0, slideshowTick));

        public int GetSlideshowOptions(int* options, uint* slideshowTick)
        {
            if (options is null || slideshowTick is null)
                return E_POINTER;
            return Run(() =>
            {
                (bool shuffle, uint interval) = server.SlideshowOptions;
                *options = shuffle ? DSO_SHUFFLEIMAGES : 0;
                *slideshowTick = interval;
            });
        }

        public int AdvanceSlideshow(char* monitorId, int direction) =>
            // Explorer only advances every monitor, and only forward.
            monitorId is not null || direction != 0 ? E_NOTIMPL : Run(server.AdvanceSlideshow);

        public int GetStatus(int* state)
        {
            if (state is null)
                return E_POINTER;
            return Run(() => *state = (server.Enabled ? DSS_ENABLED : 0) | (server.SlideshowRunning ? DSS_SLIDESHOW : 0));
        }

        public int Enable(int enable) => Run(() => server.Enabled = enable != 0);

        private const int DSO_SHUFFLEIMAGES = 0x1;
        private const int DSS_ENABLED = 0x1;
        private const int DSS_SLIDESHOW = 0x2;

        private static char* Allocate(string text) => (char*)Marshal.StringToCoTaskMemUni(text);

        private static int Run(Action call)
        {
            try
            {
                call();
                return 0;
            }
            catch (Exception ex)
            {
                // A .NET exception's HRESULT (0x8013xxxx) means nothing to the caller: report it as E_FAIL.
                if ((ex.HResult & 0xFFFF0000) == 0x80130000)
                {
                    NativeCallback.Report(ex);
                    return E_FAIL;
                }
                return ex.HResult;
            }
        }
    }
}
