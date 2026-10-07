using System.Security.Principal;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using NeoShell.Interop.Shell;
using NeoShell.Logging;

namespace NeoShell;

/// <summary>The signed-in user's name and picture, as Start and the profile widget show them, and other accounts' pictures.</summary>
internal static class UserAccount
{
    public static string DisplayName => CurrentUser.DisplayName() ?? Environment.UserName;

    /// <summary>
    /// The account picture at <paramref name="size"/> pixels (Windows keeps 32, 40, 48, 96, 192, 208, 240, 424, 448
    /// and 1080), or null when the user has none.
    /// </summary>
    /// <param name="sid">Another account's SID; the signed-in user's when null.</param>
    public static async Task<BitmapImage?> LoadPictureAsync(int size, string? sid = null) =>
        PicturePath(size, sid ?? WindowsIdentity.GetCurrent().User?.Value) is { } path ? await LoadAsync(path) : null;

    /// <summary>
    /// The account picture, or Windows' grey silhouette for an account without one, as Start shows them. The
    /// silhouette comes at 32, 40, 48 and 192 pixels, and 448 for anything bigger.
    /// </summary>
    public static async Task<BitmapImage?> LoadPictureOrDefaultAsync(int size, string? sid = null)
    {
        if (await LoadPictureAsync(size, sid) is { } picture)
            return picture;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "User Account Pictures");
        int fit = Array.Find([32, 40, 48, 192], s => s >= size);
        return await LoadAsync(Path.Combine(folder, fit > 0 ? $"user-{fit}.png" : "user.png"));
    }

    private static string? PicturePath(int size, string? sid)
    {
        try
        {
            // Windows keeps the account picture's files per user SID.
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\{sid}");
            return key?.GetValue($"Image{size}") is string path && File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn("Could not read where the account picture is", ex);
            return null;
        }
    }

    private static async Task<BitmapImage?> LoadAsync(string path)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            var picture = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            await picture.SetSourceAsync(stream.AsRandomAccessStream());
            return picture;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not load the account picture {path}", ex);
            return null;
        }
    }
}
