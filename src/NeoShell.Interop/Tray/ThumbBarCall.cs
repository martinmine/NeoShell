using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NeoShell.Interop.Imaging;

namespace NeoShell.Interop.Tray;

/// <summary>Which parts of a <see cref="ThumbButtonUpdate"/> the app set (<c>THB_*</c>).</summary>
[Flags]
public enum ThumbButtonMask { None = 0, Bitmap = 1, Icon = 2, Tooltip = 4, Flags = 8 }

/// <summary>A thumbnail toolbar button's state (<c>THBF_*</c>); none of them means enabled.</summary>
[Flags]
public enum ThumbButtonFlags { None = 0, Disabled = 1, DismissOnClick = 2, NoBackground = 4, Hidden = 8, NonInteractive = 0x10 }

public enum ThumbBarCallKind { AddButtons, UpdateButtons, SetImageList }

/// <summary>One <c>THUMBBUTTON</c> as the app passed it: only the parts in <see cref="Mask"/> count.</summary>
/// <param name="Bitmap">The image's index in the window's image list.</param>
/// <param name="Icon">The button's own icon, copied: the app may destroy its HICON once the call returns.</param>
public sealed record ThumbButtonUpdate(
    ThumbButtonMask Mask, uint Id, int Bitmap, IconBitmap? Icon, string Tooltip, ThumbButtonFlags Flags);

/// <summary>
/// An <c>ITaskbarList3</c> call about a window's thumbnail toolbar, the buttons under its preview (a player's previous,
/// play and next): <c>ThumbBarAddButtons</c>, <c>ThumbBarUpdateButtons</c> or <c>ThumbBarSetImageList</c>.
/// </summary>
/// <param name="Images">The image list's images, for <see cref="ThumbBarCallKind.SetImageList"/>.</param>
public sealed record ThumbBarCall(
    ThumbBarCallKind Kind, nint Window, IReadOnlyList<ThumbButtonUpdate> Buttons, IReadOnlyList<IconBitmap> Images)
{
    private const uint WM_USER = 0x0400;
    // The packed THUMBBUTTON ExplorerFrame copies over: mask, id, bitmap, a 32-bit HICON, 260 characters of
    // tooltip, flags.
    private const int ButtonSize = 4 * 4 + 260 * 2 + 4;
    private const int TipOffset = 16;
    private const int MaxButtons = 7;

    /// <summary>The call a task band message carries, if it's one of these; its data is in shared memory.</summary>
    internal static ThumbBarCallKind? KindOf(uint message) => message switch
    {
        WM_USER + 76 => ThumbBarCallKind.AddButtons,
        WM_USER + 77 => ThumbBarCallKind.UpdateButtons,
        WM_USER + 78 => ThumbBarCallKind.SetImageList,
        _ => null,
    };

    /// <summary>
    /// Reads a call's data: for buttons a count and that many packed <c>THUMBBUTTON</c>s, for the image list its size
    /// and what <c>ImageList_Write</c> wrote. Null when the data doesn't hold together.
    /// </summary>
    /// <param name="icon">Copies a button's HICON.</param>
    internal static ThumbBarCall? Parse(ThumbBarCallKind kind, nint window, ReadOnlySpan<byte> data, Func<nint, IconBitmap?> icon)
    {
        if (data.Length < 4)
            return null;
        int count = BinaryPrimitives.ReadInt32LittleEndian(data);

        if (kind == ThumbBarCallKind.SetImageList)
        {
            return count >= 0 && count <= data.Length - 4 && ImageListStream.Read(data.Slice(4, count)) is { } images
                ? new ThumbBarCall(kind, window, [], images)
                : null;
        }

        if (count < 0 || count > MaxButtons || data.Length < 4 + count * ButtonSize)
            return null;
        var buttons = new List<ThumbButtonUpdate>(count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> button = data.Slice(4 + i * ButtonSize, ButtonSize);
            var mask = (ThumbButtonMask)BinaryPrimitives.ReadUInt32LittleEndian(button);
            // Icon handles are 32-bit values, sign-extended in 64-bit processes.
            nint handle = BinaryPrimitives.ReadInt32LittleEndian(button[12..]);
            string tip = MemoryMarshal.Cast<byte, char>(button.Slice(TipOffset, 520)).ToString();
            int end = tip.IndexOf('\0');
            buttons.Add(new ThumbButtonUpdate(
                mask,
                BinaryPrimitives.ReadUInt32LittleEndian(button[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(button[8..]),
                mask.HasFlag(ThumbButtonMask.Icon) && handle != 0 ? icon(handle) : null,
                mask.HasFlag(ThumbButtonMask.Tooltip) ? (end >= 0 ? tip[..end] : tip) : "",
                (ThumbButtonFlags)BinaryPrimitives.ReadUInt32LittleEndian(button[(ButtonSize - 4)..])));
        }
        return new ThumbBarCall(kind, window, buttons, []);
    }
}
