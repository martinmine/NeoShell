using System.Security.Principal;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using NeoShell.Interop.Shell;
using NeoShell.Logging;

namespace NeoShell;

/// <summary>The signed-in user's name and picture, as Start and the profile widget show them.</summary>
internal static class UserAccount
{
    public static string DisplayName => CurrentUser.DisplayName() ?? Environment.UserName;

    /// <summary>
    /// The account picture at <paramref name="size"/> pixels (Windows keeps 32, 40, 48, 96, 192, 208, 240, 424, 448
    /// and 1080), or null when the user has none.
    /// </summary>
    public static async Task<BitmapImage?> LoadPictureAsync(int size)
    {
        try
        {
            // Windows keeps the account picture's files per user SID.
            string? sid = WindowsIdentity.GetCurrent().User?.Value;
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\{sid}");
            if (key?.GetValue($"Image{size}") is not string path || !File.Exists(path))
                return null;

            byte[] bytes = await File.ReadAllBytesAsync(path);
            var picture = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            await picture.SetSourceAsync(stream.AsRandomAccessStream());
            return picture;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not load the account picture", ex);
            return null;
        }
    }
}
