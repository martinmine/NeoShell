using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace NeoShell.Interop.Tray;

/// <summary>The <c>NIM_*</c> commands of <c>Shell_NotifyIcon</c>.</summary>
public enum NotifyIconCommand
{
    Add = 0,
    Modify = 1,
    Delete = 2,
    SetFocus = 3,
    SetVersion = 4,
}

/// <summary>The <c>NIF_*</c> flags: which fields of the data are valid.</summary>
[Flags]
public enum NotifyIconFlags : uint
{
    None = 0,
    Message = 0x01,
    Icon = 0x02,
    Tip = 0x04,
    State = 0x08,
    Info = 0x10,
    Guid = 0x20,
    RealTime = 0x40,
    /// <summary>Version 4 icons that want the standard tooltip rather than their own popup.</summary>
    ShowTip = 0x80,
}

/// <summary>The <c>NIIF_*</c> flags of a balloon notification (<c>dwInfoFlags</c>).</summary>
[Flags]
public enum BalloonFlags : uint
{
    None = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    /// <summary>The balloon's own icon (<c>hBalloonIcon</c>), or the tray icon when it has none.</summary>
    User = 4,
    /// <summary>The low bits: which of the icons above.</summary>
    IconMask = 0xF,
    NoSound = 0x10,
    LargeIcon = 0x20,
    RespectQuietTime = 0x80,
}

/// <summary>
/// A <c>Shell_NotifyIcon</c> call as the tray receives it. Fields not covered by <see cref="Flags"/> are left at
/// their defaults.
/// </summary>
/// <param name="Window">Window that receives the icon's callback messages; with <see cref="Id"/>, the icon's identity.</param>
/// <param name="Guid">The icon's identity instead of window and ID, when the caller uses one.</param>
/// <param name="Hidden">Whether <c>NIS_HIDDEN</c> was set or cleared; null when the call doesn't touch it.</param>
/// <param name="Version">For <see cref="NotifyIconCommand.SetVersion"/>: 0, 3 (Windows 2000) or 4 (Vista and later).</param>
/// <param name="Info">With <see cref="NotifyIconFlags.Info"/>: the balloon's text; empty takes the balloon away.</param>
/// <param name="BalloonIcon">The balloon's own icon (<c>hBalloonIcon</c>), used with <see cref="BalloonFlags.User"/>.</param>
public sealed record NotifyIconData(
    NotifyIconCommand Command,
    nint Window,
    uint Id,
    Guid? Guid,
    NotifyIconFlags Flags,
    uint CallbackMessage,
    nint Icon,
    string Tip,
    bool? Hidden,
    uint Version,
    string Info = "",
    string InfoTitle = "",
    BalloonFlags InfoFlags = BalloonFlags.None,
    nint BalloonIcon = 0)
{
    // SHELLTRAYDATA: a signature, the NIM_ command, then the NOTIFYICONDATA. The tray sees the same layout from 32-
    // and 64-bit callers: window and icon handles travel as 32-bit values (handles are 32-bit significant).
    internal const uint Signature = 0x34753423;

    private const int DataOffset = 8;
    private const int V1Size = 152;         // NOTIFYICONDATAW_V1_SIZE: tooltip of 64 characters
    private const int V2Size = 936;         // adds state, balloon, version; tooltip of 128 characters
    private const int V3Size = 952;         // adds guidItem
    private const int V4Size = 956;         // adds hBalloonIcon
    private const uint NIS_HIDDEN = 0x1;

    /// <summary>Reads the data of a <c>WM_COPYDATA</c> with <c>dwData == 1</c>; null if it isn't valid tray data.</summary>
    internal static NotifyIconData? Parse(ReadOnlySpan<byte> copyData)
    {
        if (copyData.Length < DataOffset + V1Size || ReadUInt32(copyData, 0) != Signature)
            return null;

        ReadOnlySpan<byte> data = copyData[DataOffset..];
        int size = (int)Math.Min(ReadUInt32(data, 0), (uint)data.Length);
        if (size < V1Size)
            return null;

        var flags = (NotifyIconFlags)ReadUInt32(data, 12);
        bool? hidden = null;
        uint version = 0;
        Guid? guid = null;
        string info = "", infoTitle = "";
        var infoFlags = BalloonFlags.None;
        nint balloonIcon = 0;
        if (size >= V2Size)
        {
            uint state = ReadUInt32(data, 280);
            uint stateMask = ReadUInt32(data, 284);
            if (flags.HasFlag(NotifyIconFlags.State) && (stateMask & NIS_HIDDEN) != 0)
                hidden = (state & NIS_HIDDEN) != 0;
            // The balloon's timeout shares this field; Windows ignores it since Vista.
            version = ReadUInt32(data, 800);
            if (flags.HasFlag(NotifyIconFlags.Info))
            {
                info = ReadString(data.Slice(288, 512));
                infoTitle = ReadString(data.Slice(804, 128));
                infoFlags = (BalloonFlags)ReadUInt32(data, 932);
                if (size >= V4Size)
                    balloonIcon = Handle(ReadUInt32(data, 952));
            }
        }
        if (size >= V3Size && flags.HasFlag(NotifyIconFlags.Guid))
            guid = new System.Guid(data.Slice(936, 16));

        return new NotifyIconData(
            (NotifyIconCommand)ReadUInt32(copyData, 4),
            Handle(ReadUInt32(data, 4)),
            ReadUInt32(data, 8),
            guid,
            flags,
            ReadUInt32(data, 16),
            Handle(ReadUInt32(data, 20)),
            flags.HasFlag(NotifyIconFlags.Tip) ? ReadString(data.Slice(24, size >= V2Size ? 256 : 128)) : "",
            hidden,
            version,
            info,
            infoTitle,
            infoFlags,
            balloonIcon);
    }

    /// <summary>A 32-bit window or icon handle as a pointer-sized one: sign-extended, as Windows does.</summary>
    internal static nint Handle(uint value) => (int)value;

    internal static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    private static string ReadString(ReadOnlySpan<byte> field)
    {
        ReadOnlySpan<char> text = MemoryMarshal.Cast<byte, char>(field);
        int end = text.IndexOf('\0');
        return new string(end >= 0 ? text[..end] : text);
    }
}

/// <summary>
/// <c>Shell_NotifyIconGetRect</c> as the tray receives it (<c>WM_COPYDATA</c> with <c>dwData == 3</c>): the caller
/// asks once for the icon's top-left corner and once for its size.
/// </summary>
public sealed record NotifyIconRectRequest(nint Window, uint Id, Guid? Guid, bool WantsSize)
{
    private const int Size = 40;

    internal static NotifyIconRectRequest? Parse(ReadOnlySpan<byte> copyData)
    {
        if (copyData.Length < Size || NotifyIconData.ReadUInt32(copyData, 0) != NotifyIconData.Signature)
            return null;

        var guid = new System.Guid(copyData.Slice(24, 16));
        return new NotifyIconRectRequest(
            NotifyIconData.Handle(NotifyIconData.ReadUInt32(copyData, 16)),
            NotifyIconData.ReadUInt32(copyData, 20),
            guid == System.Guid.Empty ? null : guid,
            NotifyIconData.ReadUInt32(copyData, 4) == 2);
    }

    /// <summary>The reply: the top-left corner or the size of <paramref name="iconBounds"/>, as MAKELONG(x, y).</summary>
    public nint Reply(Windows.Graphics.RectInt32 iconBounds)
    {
        int x = WantsSize ? iconBounds.Width : iconBounds.X;
        int y = WantsSize ? iconBounds.Height : iconBounds.Y;
        return (nint)(uint)(((ushort)(short)y << 16) | (ushort)(short)x);
    }
}
