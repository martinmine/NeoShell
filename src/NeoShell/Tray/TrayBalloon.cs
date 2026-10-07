using NeoShell.Interop.Imaging;

namespace NeoShell.Tray;

/// <summary>
/// A tray icon's balloon notification (<c>NIF_INFO</c>), shown as a toast as Explorer shows it. Explorer posts it to
/// Windows' notification platform as a banner-only toast (it never stays in the notification center) of the app below;
/// NeoShell shows it itself, as nothing else would show it without Explorer.
/// </summary>
/// <param name="IconKey">The icon it belongs to (<see cref="TrayIconState.Key"/>); an icon has one balloon at a time.</param>
/// <param name="AppId">The app it's notified as, whose notification settings apply (see <see cref="AppIdFor"/>).</param>
/// <param name="Picture">The information, warning, error or app's own icon beside the text, or null.</param>
/// <param name="Logo">The icon in the header beside the app's name; null to use the app's own logo.</param>
/// <param name="Silent">Without a sound (<c>NIIF_NOSOUND</c>); otherwise the default notification sound.</param>
public sealed record TrayBalloon(
    string IconKey, string AppId, string AppName, string Title, string Body, IconBitmap? Picture, IconBitmap? Logo, bool Silent)
{
    public const string GeneratedAppIdPrefix = "NotifyIconGeneratedAumid_";

    /// <summary>The toast's texts as Explorer fills them in: without a title, the text takes the title's place.</summary>
    public static (string Title, string Body) Texts(string title, string text) => title.Length == 0 ? (text, "") : (title, text);

    /// <summary>
    /// The app a balloon is notified as, as Explorer picks it: the app's own AppUserModelID when its window or package
    /// has one; else Explorer's ID for the icon, <c>NotifyIconGeneratedAumid_</c> and the icon's
    /// <c>NotifyIconSettings</c> key; else, for an icon Explorer has never seen, the executable's implicit AppID.
    /// </summary>
    public static string AppIdFor(string? explicitAppId, string? settingsId, string implicitAppId) =>
        explicitAppId ?? (settingsId is null ? implicitAppId : GeneratedAppIdPrefix + settingsId);
}
