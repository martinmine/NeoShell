using System.ComponentModel;
using System.Globalization;
using NeoShell.Desktop;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Notifications;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Notifications;
using NeoShell.Settings;
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

    /// <summary>
    /// Keeps a snip as Snipping Tool's overlay does: on the clipboard as a bitmap and as a PNG (a freeform snip's
    /// bitmap white outside the path, its PNG transparent), saved in Pictures\Screenshots when Snipping Tool's
    /// "Automatically save screenshots" is on (otherwise in Snipping Tool's temporary folder), and with Snipping Tool's
    /// toast, which opens the snip in Snipping Tool's editor. Failures are logged.
    /// </summary>
    /// <param name="transparent">A freeform snip, transparent outside its path.</param>
    /// <param name="owner">A window of NeoShell's, to own the clipboard.</param>
    internal static async void KeepSnip(IconBitmap snip, bool transparent, nint owner, Func<ToastPopups?> toasts)
    {
        DateTime taken = DateTime.Now;
        try
        {
            byte[] png = await Task.Run(() => ScreenCapture.EncodePngAsync(snip, transparent));
            try
            {
                ScreenCapture.CopyToClipboard(transparent ? FreeformPath.OnWhite(snip) : snip, owner, png);
            }
            catch (Win32Exception ex)
            {
                Log.Warn("Could not copy the snip", ex);
            }

            bool saved = SnippingToolSettings.ReadAutoSave();
            string folder = saved ? ScreenCapture.ScreenshotsFolder() : SnippingToolSettings.TemporaryFolder();
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, FileName(taken, name => File.Exists(Path.Combine(folder, name))));
            await File.WriteAllBytesAsync(path, png);
            Log.Info($"Snip {snip.Width}x{snip.Height} saved as {path}");

            var toast = new ToastInfo(
                0, SnippingToolSettings.AppId, "Snipping Tool", DateTimeOffset.Now, "Screenshot copied to clipboard",
                saved ? "Automatically saved to screenshots folder." : "Automatic save is turned off.");
            toasts()?.ShowAppBanner("Snip", toast, AppIcons.ToImageSource(snip), "Mark-up and share", button =>
                Launcher.Launch(new PinnedApp("Snipping Tool", Path: EditorUri(path, saved, button ? "MarkUpButton" : "Toast"))));
        }
        catch (Exception ex)
        {
            Log.Warn("Could not save the snip", ex);
        }
    }

    /// <summary>
    /// Opens a snip in Snipping Tool's editor, as its toast does: <paramref name="saved"/> when it's in the screenshots
    /// folder, otherwise temporary; <paramref name="source"/> is "Toast", or "MarkUpButton" for the toast's button.
    /// </summary>
    public static string EditorUri(string path, bool saved, string source) =>
        $"ms-screensketch:edit?&filePath={Uri.EscapeDataString(path)}&isTemporary={(saved ? "false" : "true")}&saved={(saved ? "true" : "false")}&source={source}";

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
