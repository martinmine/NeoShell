using System.ComponentModel;
using System.Globalization;
using NeoShell.Desktop;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using Windows.Graphics;

namespace NeoShell.Capture;

/// <summary>
/// Screenshots as Windows keeps them: on the clipboard, and saved in Pictures\Screenshots under the time they were
/// taken, as "Screenshot 2026-10-05 183215.png".
/// </summary>
public static class Screenshots
{
    /// <summary>Win+PrtScn: the whole screen, every monitor.</summary>
    /// <param name="owner">A window of NeoShell's, to own the clipboard.</param>
    internal static void CaptureScreen(nint owner)
    {
        RectInt32 screen = WallpaperLayout.Union(DisplayMonitor.GetAll().Select(monitor => monitor.Bounds));
        IconBitmap image;
        try
        {
            image = ScreenCapture.Capture(screen);
        }
        catch (Win32Exception ex)
        {
            Log.Warn("Could not take a screenshot", ex);
            return;
        }
        Keep(image, owner);
    }

    /// <summary>Puts the picture on the clipboard, then saves it; failures are logged.</summary>
    internal static async void Keep(IconBitmap image, nint owner)
    {
        DateTime taken = DateTime.Now;
        try
        {
            ScreenCapture.CopyToClipboard(image, owner);
        }
        catch (Win32Exception ex)
        {
            Log.Warn("Could not copy the screenshot", ex);
        }

        try
        {
            string folder = ScreenCapture.ScreenshotsFolder();
            string path = Path.Combine(folder, FileName(taken, name => File.Exists(Path.Combine(folder, name))));
            await Task.Run(() => ScreenCapture.SavePngAsync(image, path));
            Log.Info($"Screenshot {image.Width}x{image.Height} saved as {path}");
        }
        catch (Exception ex)
        {
            Log.Warn("Could not save the screenshot", ex);
        }
    }

    /// <summary>The name for a screenshot taken at <paramref name="time"/>, numbered on when the second is taken.</summary>
    public static string FileName(DateTime time, Func<string, bool> exists)
    {
        string stem = "Screenshot " + time.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
        string name = stem + ".png";
        for (int n = 2; exists(name); n++)
            name = $"{stem} ({n}).png";
        return name;
    }
}
