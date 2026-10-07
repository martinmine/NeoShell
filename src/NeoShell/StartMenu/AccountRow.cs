using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;

namespace NeoShell.StartMenu;

/// <summary>
/// A row in the account menu's "…": another account (its picture, and "Signed in" while it is), or the plain Switch
/// user with its glyph.
/// </summary>
internal sealed record AccountRow(string Name, OtherUser? User, ImageSource? Picture)
{
    public string Subtitle => User?.SignedIn == true ? "Signed in" : "";
    public Visibility SubtitleVisibility => Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PictureVisibility => User is null ? Visibility.Collapsed : Visibility.Visible;
    public string Glyph => User is null ? "" : "";
    public string AutomationId => User is null ? "SwitchUserButton" : $"SwitchTo{User.Sid}";
}
