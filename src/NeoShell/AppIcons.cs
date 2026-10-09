using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Shell;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell;

/// <summary>
/// Icons of apps and files from the shell, shared by the taskbar and Start. They load in the background:
/// <see cref="Get"/> returns null at first and <see cref="Loaded"/> is raised when an icon arrives.
/// </summary>
internal sealed class AppIcons(int size)
{
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Loaded;

    /// <summary>Pixel size the icons are loaded at.</summary>
    public int Size { get; } = size;

    public ImageSource? Get(PinnedApp app)
    {
        string item = ShellItemFor(app);
        if (_icons.TryGetValue(item, out ImageSource? icon))
            return icon;

        _icons[item] = null;
        Load(() => Read(app, Size), loaded =>
        {
            _icons[item] = loaded;
            Loaded?.Invoke();
        });
        return null;
    }

    /// <summary>
    /// Reads an icon off the UI thread (it may wait on a hung window or the disk) and makes the bitmap back on it.
    /// </summary>
    public static async void Load(Func<IconBitmap?> read, Action<ImageSource> store)
    {
        try
        {
            IconBitmap? icon = await Task.Run(read);
            if (icon is not null)
                store(ToImageSource(icon));
        }
        catch (Exception ex)
        {
            Log.Warn("Loading an icon failed", ex);
        }
    }

    /// <summary>Makes a bitmap XAML can show from icon pixels. UI thread only.</summary>
    public static ImageSource ToImageSource(IconBitmap icon)
    {
        var bitmap = new WriteableBitmap(icon.Width, icon.Height);
        using (Stream stream = bitmap.PixelBuffer.AsStream())
            stream.Write(icon.Pixels);
        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>The app's icon at <paramref name="size"/> pixels: a packaged app's logo, else the shell's. Off the UI thread.</summary>
    public static IconBitmap? Read(PinnedApp app, int size) =>
        (app.AppUserModelId is { } appId ? PackagedApps.GetLogo(appId, size) : null) ?? ShellItems.GetIcon(ShellItemFor(app), size);

    private static string ShellItemFor(PinnedApp app) =>
        app.AppUserModelId is { } appId ? ShellItems.AppsFolderPath(appId) : app.Path ?? "";
}
