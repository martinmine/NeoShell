using NeoShell.Interop.Notifications;

namespace NeoShell.Notifications;

/// <summary>
/// How a toast's content is laid out and timed, as Explorer's toasts and notification center do it (measured on
/// Windows 11 25H2 with toasts of a test app; see design/notifications.md).
/// </summary>
public static class ToastLayout
{
    /// <summary>How long a <c>duration="long"</c> toast stays.</summary>
    public static readonly TimeSpan LongDuration = TimeSpan.FromSeconds(25);

    /// <summary>The <c>appLogoOverride</c> beside the texts, in effective pixels: bigger when cropped to a circle.</summary>
    public static double LogoSize(ToastImage logo) => logo.Circle ? 60 : 48;

    /// <summary>
    /// How long the toast stays on screen: null until the user acts on it (a reminder, alarm or incoming call with a
    /// button), 25 seconds for a long one, else the system's time ("Dismiss notifications after").
    /// </summary>
    public static TimeSpan? Duration(ToastContent? content, TimeSpan system) =>
        content is null ? system
        : content.StaysUntilDismissed ? null
        : content.Long ? LongDuration
        : system;

    /// <summary>
    /// The buttons in rows of equal-width buttons: all in one row, except that an incoming call (without
    /// <c>useButtonStyle</c>) has its last button alone in a row of its own under the others, in the accent colour.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ToastAction>> ButtonRows(ToastContent content)
    {
        ToastAction[] buttons = [.. content.Buttons];
        if (buttons.Length == 0)
            return [];
        if (content.IsIncomingCall && !content.UseButtonStyle && buttons.Length > 1)
            return [buttons[..^1], [buttons[^1]]];
        return [buttons];
    }

    /// <summary>Whether this is the incoming call's answer-row button, drawn in the accent colour.</summary>
    public static bool IsAccent(ToastContent content, ToastAction button) =>
        content.IsIncomingCall && !content.UseButtonStyle && content.Buttons.LastOrDefault() == button;

    /// <summary>
    /// Whether the buttons show their icons above their text (and are taller): when any has one and the toast doesn't
    /// use button styles; with <c>useButtonStyle</c> an icon goes beside the text.
    /// </summary>
    public static bool IconsAbove(ToastContent content) => !content.UseButtonStyle && content.Buttons.Any(b => b.ImageUri is not null);

    /// <summary>A button's colour: <c>Success</c> or <c>Critical</c>, which count only with <c>useButtonStyle</c>.</summary>
    public static string? Style(ToastContent content, ToastAction button) =>
        content.UseButtonStyle && button.Style is { } style && (Is(style, "Success") || Is(style, "Critical")) ? style : null;

    /// <summary>
    /// The percentage beside a progress bar, unless the toast gives its own text; none while indeterminate.
    /// </summary>
    public static string ProgressValueText(ToastProgress progress) =>
        progress.ValueText ?? (progress.Value is { } value ? $"{Math.Round(value * 100)}%" : "");

    /// <summary>
    /// Whether a notification center card has more to show when expanded than collapsed (Explorer's chevron by its
    /// time): buttons, inputs, pictures (other than the logo) or a progress bar.
    /// </summary>
    public static bool HasMore(ToastContent? content) =>
        content is not null
        && (content.Hero is not null || content.Images.Count > 0 || content.Progress is not null || content.Inputs.Count > 0 || content.Buttons.Any());

    private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
