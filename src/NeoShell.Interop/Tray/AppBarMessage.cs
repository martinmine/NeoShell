using System.Buffers.Binary;
using NeoShell.Interop.Native;
using Windows.Graphics;

namespace NeoShell.Interop.Tray;

/// <summary>The <c>ABM_*</c> messages of <c>SHAppBarMessage</c>.</summary>
public enum AppBarCommand : uint
{
    New = 0,
    Remove = 1,
    QueryPos = 2,
    SetPos = 3,
    GetState = 4,
    GetTaskbarPos = 5,
    Activate = 6,
    GetAutoHideBar = 7,
    SetAutoHideBar = 8,
    WindowPosChanged = 9,
    SetState = 10,
    GetAutoHideBarEx = 11,
    SetAutoHideBarEx = 12,
}

/// <summary>The <c>ABE_*</c> screen edges. <see cref="None"/>: a bar that hasn't been placed yet.</summary>
public enum AppBarEdge : uint
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
    None = uint.MaxValue,
}

/// <summary>The <c>ABN_*</c> notifications, the wParam of an app bar's callback message.</summary>
public enum AppBarNotification
{
    /// <summary>The taskbar's auto-hide state changed.</summary>
    StateChange = 0,
    /// <summary>The space available may have changed: the bar should query and set its position again.</summary>
    PosChanged = 1,
    /// <summary>A full-screen app opened (lParam 1) or closed (lParam 0) on the bar's monitor.</summary>
    FullScreenApp = 2,
    /// <summary>The taskbar is about to arrange windows (lParam 1) or has (lParam 0).</summary>
    WindowArrange = 3,
}

/// <summary>
/// A <c>SHAppBarMessage</c> call as <c>Shell_TrayWnd</c> receives it (<c>WM_COPYDATA</c> with <c>dwData == 0</c>).
/// </summary>
/// <param name="Window">The app bar; with <see cref="AppBarCommand.New"/>, the window to register.</param>
/// <param name="LParam">The message's own value: auto-hide on or off, the <c>ABS_*</c> state to set.</param>
public sealed record AppBarMessage(
    AppBarCommand Command,
    nint Window,
    uint CallbackMessage,
    AppBarEdge Edge,
    RectInt32 Rect,
    long LParam,
    nint SharedMemory,
    uint ProcessId)
{
    // shell32's SHAppBarMessage (cdb on System32 and SysWOW64 shell32, 26200) sends the same 0x40 bytes from 32- and
    // 64-bit callers: APPBARDATA3264 { cbSize, hWnd as 32 bits, uCallbackMessage, uEdge, rc, lParam as 64 bits }
    // (0x28 bytes), then dwMessage at 0x28, the shared memory handle at 0x30 (64 bits) and the process that handle
    // belongs to at 0x38 (the shell's own: shell32 looks it up from Shell_TrayWnd). A 32-bit caller sign-extends
    // lParam and the handle; its padding at 0x2C and 0x3C is left uninitialized.
    private const int Size = 0x40;
    private const int SharedEdgeOffset = 0x0C;
    private const int SharedRectOffset = 0x10;

    internal static AppBarMessage? Parse(ReadOnlySpan<byte> copyData)
    {
        if (copyData.Length < Size)
            return null;

        return new AppBarMessage(
            (AppBarCommand)NotifyIconData.ReadUInt32(copyData, 0x28),
            NotifyIconData.Handle(NotifyIconData.ReadUInt32(copyData, 0x04)),
            NotifyIconData.ReadUInt32(copyData, 0x08),
            (AppBarEdge)NotifyIconData.ReadUInt32(copyData, 0x0C),
            ReadRect(copyData[0x10..]),
            BinaryPrimitives.ReadInt64LittleEndian(copyData[0x20..]),
            (nint)BinaryPrimitives.ReadInt64LittleEndian(copyData[0x30..]),
            NotifyIconData.ReadUInt32(copyData, 0x38));
    }

    /// <summary>
    /// Answers <see cref="AppBarCommand.QueryPos"/> and <see cref="AppBarCommand.SetPos"/>: the rectangle the bar may
    /// have. shell32 passes a copy of the APPBARDATA in shared memory and copies it back to the caller afterwards.
    /// </summary>
    public void ReplyRect(RectInt32 rect) => WriteShared(null, rect);

    /// <summary>Answers <see cref="AppBarCommand.GetTaskbarPos"/>: the taskbar's edge and rectangle.</summary>
    public void ReplyTaskbarPos(AppBarEdge edge, RectInt32 rect) => WriteShared(edge, rect);

    private unsafe void WriteShared(AppBarEdge? edge, RectInt32 rect)
    {
        if (SharedMemory == 0)
            return;
        var data = (byte*)Shlwapi.SHLockShared(SharedMemory, ProcessId);
        if (data == null)
            return;
        if (edge is { } value)
            *(uint*)(data + SharedEdgeOffset) = (uint)value;
        *(User32.RECT*)(data + SharedRectOffset) = User32.RECT.From(rect);
        Shlwapi.SHUnlockShared(data);
    }

    private static RectInt32 ReadRect(ReadOnlySpan<byte> data)
    {
        int left = BinaryPrimitives.ReadInt32LittleEndian(data);
        int top = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        return new RectInt32(left, top,
            BinaryPrimitives.ReadInt32LittleEndian(data[8..]) - left,
            BinaryPrimitives.ReadInt32LittleEndian(data[12..]) - top);
    }
}
