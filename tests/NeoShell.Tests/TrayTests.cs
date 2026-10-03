using System.Buffers.Binary;
using System.Text;
using NeoShell.Interop.Tray;
using NeoShell.Tray;
using Windows.Graphics;

namespace NeoShell.Tests;

public sealed class TrayTests
{
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_STATE = 8, NIF_GUID = 0x20, NIF_SHOWTIP = 0x80;
    private static readonly Guid IconGuid = new("0d2d1e44-5e0a-4a65-9b3c-6c7c8f2b7a10");

    /// <summary>Builds the WM_COPYDATA payload Shell_NotifyIcon sends: SHELLTRAYDATA with a NOTIFYICONDATA of the given size.</summary>
    private static byte[] TrayData(
        uint command, int size = 956, uint window = 0x00010ABC, uint id = 7, uint flags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uint callback = 0x8001, uint icon = 0x00020DEF, string tip = "Hello", uint state = 0, uint stateMask = 0,
        uint version = 0, Guid? guid = null)
    {
        var buffer = new byte[8 + size];
        Span<byte> nid = buffer.AsSpan(8);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 0x34753423);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), command);
        BinaryPrimitives.WriteUInt32LittleEndian(nid, (uint)size);
        BinaryPrimitives.WriteUInt32LittleEndian(nid[4..], window);
        BinaryPrimitives.WriteUInt32LittleEndian(nid[8..], id);
        BinaryPrimitives.WriteUInt32LittleEndian(nid[12..], flags);
        BinaryPrimitives.WriteUInt32LittleEndian(nid[16..], callback);
        BinaryPrimitives.WriteUInt32LittleEndian(nid[20..], icon);
        Encoding.Unicode.GetBytes(tip).CopyTo(nid[24..]);
        if (size >= 936)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(nid[280..], state);
            BinaryPrimitives.WriteUInt32LittleEndian(nid[284..], stateMask);
            BinaryPrimitives.WriteUInt32LittleEndian(nid[800..], version);
        }
        if (size >= 952 && guid is { } g)
            g.TryWriteBytes(nid[936..]);
        return buffer;
    }

    [Fact]
    public void Current_notifyicondata_is_parsed()
    {
        NotifyIconData data = NotifyIconData.Parse(TrayData(NIM_ADD))!;

        Assert.Equal(NotifyIconCommand.Add, data.Command);
        Assert.Equal((nint)0x10ABC, data.Window);
        Assert.Equal(7u, data.Id);
        Assert.Equal(0x8001u, data.CallbackMessage);
        Assert.Equal((nint)0x20DEF, data.Icon);
        Assert.Equal("Hello", data.Tip);
        Assert.Null(data.Hidden);
        Assert.Null(data.Guid);
    }

    [Fact]
    public void Handles_with_the_high_bit_set_are_sign_extended_like_window_handles()
    {
        NotifyIconData data = NotifyIconData.Parse(TrayData(NIM_ADD, window: 0x8001_0002))!;

        Assert.Equal(unchecked((nint)(int)0x8001_0002), data.Window);
    }

    [Fact]
    public void Version_1_size_with_a_64_character_tip_is_parsed()
    {
        // Old apps send NOTIFYICONDATA_V1 (152 bytes): no state, version or GUID.
        NotifyIconData data = NotifyIconData.Parse(TrayData(NIM_ADD, size: 152, tip: new string('x', 63)))!;

        Assert.Equal(new string('x', 63), data.Tip);
        Assert.Equal(0u, data.Version);
    }

    [Fact]
    public void Guid_is_read_only_when_flagged()
    {
        Assert.Equal(IconGuid, NotifyIconData.Parse(TrayData(NIM_ADD, flags: NIF_GUID, guid: IconGuid))!.Guid);
        Assert.Null(NotifyIconData.Parse(TrayData(NIM_ADD, flags: NIF_ICON, guid: IconGuid))!.Guid);
        // Version 2 data has no room for one.
        Assert.Null(NotifyIconData.Parse(TrayData(NIM_ADD, size: 936, flags: NIF_GUID))!.Guid);
    }

    [Theory]
    [InlineData(NIF_STATE, 1u, 1u, true)]
    [InlineData(NIF_STATE, 0u, 1u, false)]
    [InlineData(NIF_STATE, 1u, 0u, null)]   // NIS_HIDDEN not in the mask: untouched
    [InlineData(NIF_ICON, 1u, 1u, null)]    // no NIF_STATE: untouched
    public void Hidden_state_follows_the_state_mask(uint flags, uint state, uint mask, bool? expected)
    {
        Assert.Equal(expected, NotifyIconData.Parse(TrayData(NIM_MODIFY, flags: flags, state: state, stateMask: mask))!.Hidden);
    }

    [Fact]
    public void Set_version_carries_the_version()
    {
        Assert.Equal(4u, NotifyIconData.Parse(TrayData(NIM_SETVERSION, flags: 0, version: 4))!.Version);
    }

    [Fact]
    public void Invalid_tray_data_is_rejected()
    {
        byte[] wrongSignature = TrayData(NIM_ADD);
        wrongSignature[0] = 0;

        Assert.Null(NotifyIconData.Parse(wrongSignature));
        Assert.Null(NotifyIconData.Parse(TrayData(NIM_ADD)[..100]));
        Assert.Null(NotifyIconData.Parse(TrayData(NIM_ADD, size: 100)));
    }

    [Fact]
    public void Icon_rect_request_is_parsed_and_answered_with_the_corner_or_the_size()
    {
        var request = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(request, 0x34753423);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), 2);           // size
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(16), 0x10ABC);    // hWnd
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(20), 7);          // uID

        NotifyIconRectRequest parsed = NotifyIconRectRequest.Parse(request)!;

        Assert.Equal(new NotifyIconRectRequest(0x10ABC, 7, null, WantsSize: true), parsed);
        Assert.Equal((nint)((40 << 16) | 28), parsed.Reply(new RectInt32(1200, 1020, 28, 40)));
        Assert.Equal((nint)((1020 << 16) | 1200), (parsed with { WantsSize = false }).Reply(new RectInt32(1200, 1020, 28, 40)));
    }

    [Fact]
    public void Icon_rect_reply_keeps_negative_coordinates_as_16_bit_values()
    {
        var request = new NotifyIconRectRequest(1, 1, null, WantsSize: false);

        nint reply = request.Reply(new RectInt32(-1900, 1020, 40, 40));

        Assert.Equal(-1900, (short)(reply & 0xFFFF));
        Assert.Equal(1020, (short)((reply >> 16) & 0xFFFF));
    }

    [Theory]
    [InlineData(0u, TrayMouseEvent.LeftUp, new[] { 0x0202u })]
    [InlineData(3u, TrayMouseEvent.LeftUp, new[] { 0x0202u, 0x0400u })]           // + NIN_SELECT
    [InlineData(4u, TrayMouseEvent.RightUp, new[] { 0x0205u, 0x007Bu })]          // + WM_CONTEXTMENU
    [InlineData(0u, TrayMouseEvent.RightUp, new[] { 0x0205u })]
    [InlineData(4u, TrayMouseEvent.HoverStart, new[] { 0x0406u })]                // NIN_POPUPOPEN
    [InlineData(0u, TrayMouseEvent.HoverStart, new uint[0])]
    [InlineData(0u, TrayMouseEvent.LeftDoubleClick, new[] { 0x0203u })]
    public void Mouse_events_become_the_messages_the_icon_version_expects(uint version, TrayMouseEvent mouseEvent, uint[] expected)
    {
        IReadOnlyList<(nint WParam, nint LParam)> messages = NotifyIconInput.Messages(7, version, mouseEvent, new PointInt32(10, 20));

        Assert.Equal(expected, messages.Select(m => version >= 4 ? (uint)(m.LParam & 0xFFFF) : (uint)m.LParam));
    }

    [Fact]
    public void Version_4_messages_carry_the_anchor_and_the_id()
    {
        (nint wParam, nint lParam) = NotifyIconInput.Messages(7, 4, TrayMouseEvent.LeftDown, new PointInt32(1200, 1030))[0];

        Assert.Equal((nint)((1030 << 16) | 1200), wParam);
        Assert.Equal((nint)((7 << 16) | 0x0201), lParam);
    }

    [Fact]
    public void Legacy_messages_carry_the_id_and_the_mouse_message()
    {
        (nint wParam, nint lParam) = NotifyIconInput.Messages(7, 0, TrayMouseEvent.LeftDown, new PointInt32(1200, 1030))[0];

        Assert.Equal((nint)7, wParam);
        Assert.Equal((nint)0x0201, lParam);
    }

    private static NotifyIconData Data(NotifyIconCommand command, nint window = 1, uint id = 1, Guid? guid = null,
        NotifyIconFlags flags = NotifyIconFlags.None, nint icon = 0, string tip = "", bool? hidden = null, uint version = 0, uint callback = 0) =>
        new(command, window, id, guid, flags, callback, icon, tip, hidden, version);

    [Fact]
    public void Store_adds_modifies_and_deletes_by_window_and_id()
    {
        var store = new TrayIconStore();

        Assert.True(store.Apply(Data(NotifyIconCommand.Add, flags: NotifyIconFlags.Icon | NotifyIconFlags.Tip | NotifyIconFlags.Message, icon: 10, tip: "A", callback: 0x8001)));
        Assert.False(store.Apply(Data(NotifyIconCommand.Add)));                     // already there
        Assert.True(store.Apply(Data(NotifyIconCommand.Modify, flags: NotifyIconFlags.Tip, tip: "B")));
        Assert.False(store.Apply(Data(NotifyIconCommand.Modify, id: 2)));          // no such icon

        TrayIconState icon = Assert.Single(store.Icons);
        Assert.Equal(("B", (nint)10, 0x8001u), (icon.Tip, icon.IconHandle, icon.CallbackMessage));

        Assert.True(store.Apply(Data(NotifyIconCommand.Delete)));
        Assert.Empty(store.Icons);
        Assert.False(store.Apply(Data(NotifyIconCommand.Delete)));
    }

    [Fact]
    public void Store_identifies_guid_icons_by_guid_even_from_another_window()
    {
        var store = new TrayIconStore();
        store.Apply(Data(NotifyIconCommand.Add, window: 1, guid: IconGuid, flags: NotifyIconFlags.Guid));

        Assert.True(store.Apply(Data(NotifyIconCommand.Modify, window: 2, id: 99, guid: IconGuid, flags: NotifyIconFlags.Guid | NotifyIconFlags.Tip, tip: "moved")));

        TrayIconState icon = Assert.Single(store.Icons);
        Assert.Equal(((nint)2, "moved"), (icon.Window, icon.Tip));
        // Window and ID alone don't find a GUID icon.
        Assert.False(store.Apply(Data(NotifyIconCommand.Delete, window: 2, id: 99)));
    }

    [Fact]
    public void Store_tracks_hidden_state_and_version()
    {
        var store = new TrayIconStore();
        store.Apply(Data(NotifyIconCommand.Add, hidden: true));
        Assert.True(store.Icons[0].IsHidden);

        store.Apply(Data(NotifyIconCommand.Modify, flags: NotifyIconFlags.Tip, tip: "x"));
        Assert.True(store.Icons[0].IsHidden);   // untouched by a call without NIS_HIDDEN

        store.Apply(Data(NotifyIconCommand.Modify, hidden: false));
        store.Apply(Data(NotifyIconCommand.SetVersion, version: 4));
        Assert.Equal((false, 4u), (store.Icons[0].IsHidden, store.Icons[0].Version));
    }

    [Fact]
    public void Store_removes_icons_whose_window_is_gone()
    {
        var store = new TrayIconStore();
        store.Apply(Data(NotifyIconCommand.Add, window: 1));
        store.Apply(Data(NotifyIconCommand.Add, window: 2));

        Assert.True(store.RemoveDeadOwners(window => window == 2));
        Assert.Equal((nint)2, Assert.Single(store.Icons).Window);
        Assert.False(store.RemoveDeadOwners(_ => true));
    }
}
