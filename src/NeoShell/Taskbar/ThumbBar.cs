using NeoShell.Interop.Imaging;
using NeoShell.Interop.Tray;

namespace NeoShell.Taskbar;

/// <summary>A thumbnail toolbar button; its image is its own icon, or else one from the window's image list.</summary>
/// <param name="Bitmap">The image's index in the image list; -1 for none.</param>
public sealed record ThumbButton(uint Id, int Bitmap, IconBitmap? Icon, string Tooltip, ThumbButtonFlags Flags)
{
    public bool IsHidden => Flags.HasFlag(ThumbButtonFlags.Hidden);

    public IconBitmap? Image(IReadOnlyList<IconBitmap> images) =>
        Icon ?? (Bitmap >= 0 && Bitmap < images.Count ? images[Bitmap] : null);
}

/// <summary>
/// A window's thumbnail toolbar, the buttons its app puts under its preview through <c>ITaskbarList3</c> (a player's
/// previous, play and next), in the order they were added, and the image list they take their images from.
/// </summary>
public sealed record ThumbBar(IReadOnlyList<ThumbButton> Buttons, IReadOnlyList<IconBitmap> Images)
{
    public static readonly ThumbBar Empty = new([], []);

    /// <summary>
    /// The toolbar after an app's call: adding sets the buttons (Windows takes them once), updating changes the given
    /// parts of the buttons with the same IDs, and a new image list replaces the old.
    /// </summary>
    public ThumbBar Apply(ThumbBarCall call) => call.Kind switch
    {
        ThumbBarCallKind.SetImageList => this with { Images = call.Images },
        ThumbBarCallKind.AddButtons => this with
        {
            Buttons = [.. call.Buttons.Select(added => Update(new ThumbButton(added.Id, -1, null, "", ThumbButtonFlags.None), added))],
        },
        _ => this with
        {
            Buttons = [.. Buttons.Select(button => call.Buttons.LastOrDefault(update => update.Id == button.Id) is { } update ? Update(button, update) : button)],
        },
    };

    private static ThumbButton Update(ThumbButton button, ThumbButtonUpdate update) => button with
    {
        Bitmap = update.Mask.HasFlag(ThumbButtonMask.Bitmap) ? update.Bitmap : button.Bitmap,
        Icon = update.Mask.HasFlag(ThumbButtonMask.Icon) ? update.Icon : button.Icon,
        Tooltip = update.Mask.HasFlag(ThumbButtonMask.Tooltip) ? update.Tooltip : button.Tooltip,
        Flags = update.Mask.HasFlag(ThumbButtonMask.Flags) ? update.Flags : button.Flags,
    };
}
