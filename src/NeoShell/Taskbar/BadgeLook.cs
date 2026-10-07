using System.Globalization;
using NeoShell.Interop.Notifications;
using Windows.UI;

namespace NeoShell.Taskbar;

/// <summary>
/// How Explorer's taskbar draws an app's badge (Taskbar.View.dll: <c>BadgeConfiguration</c>'s table of glyphs and
/// colours, <c>FormatNumericValue</c>).
/// </summary>
public static class BadgeLook
{
    /// <summary>"Show badges on taskbar apps" (<c>TaskbarBadges</c> under Explorer\Advanced): on unless it's 0.</summary>
    public static bool AreShown(object? taskbarBadges) => taskbarBadges is not int value || value != 0;

    /// <summary>
    /// The count (99+ above 99), or the glyph's character in Segoe Fluent Icons; empty for the presence glyphs, which
    /// are plain coloured dots.
    /// </summary>
    public static string Text(AppBadge badge) => badge.Glyph switch
    {
        BadgeGlyph.None => badge.Number > 99 ? "99+" : badge.Number.ToString(CultureInfo.CurrentCulture),
        BadgeGlyph.Activity => "",
        BadgeGlyph.Alarm => "",
        BadgeGlyph.Alert => "",
        BadgeGlyph.Error => "",
        BadgeGlyph.Attention => "",
        BadgeGlyph.NewMessage => "",
        BadgeGlyph.Paused => "",
        BadgeGlyph.Playing => "",
        _ => "",
    };

    /// <summary>The glyph's own fill, the same in either theme; null for the accent colour.</summary>
    public static Color? Background(BadgeGlyph glyph) => glyph switch
    {
        BadgeGlyph.Alert or BadgeGlyph.Attention or BadgeGlyph.Error => Color.FromArgb(255, 0xD7, 0x3B, 0x02),
        BadgeGlyph.Available => Color.FromArgb(255, 0x00, 0x81, 0x17),
        BadgeGlyph.Away => Color.FromArgb(255, 0xFF, 0xC2, 0x0A),
        BadgeGlyph.Busy => Color.FromArgb(255, 0xD8, 0x21, 0x28),
        BadgeGlyph.Unavailable => Color.FromArgb(255, 0x99, 0x99, 0x99),
        _ => null,
    };

    /// <summary>White on the warning colour; null for the text-on-accent colour.</summary>
    public static Color? Foreground(BadgeGlyph glyph) =>
        glyph is BadgeGlyph.Alert or BadgeGlyph.Attention or BadgeGlyph.Error ? Color.FromArgb(255, 255, 255, 255) : null;

    /// <summary>What screen readers hear after the button's name, in Explorer's words.</summary>
    public static string HelpText(AppBadge badge) => badge.Glyph switch
    {
        BadgeGlyph.None when badge.Number == 1 => "Status 1 item",
        BadgeGlyph.None => $"Status {badge.Number.ToString(CultureInfo.CurrentCulture)} items",
        BadgeGlyph.NewMessage => "Status New message",
        _ => $"Status {badge.Glyph}",
    };
}
