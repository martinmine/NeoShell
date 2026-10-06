using System.Buffers.Binary;
using System.Text;
using NeoShell.Interop.Tray;
using NeoShell.Tray;
using Windows.Graphics;

namespace NeoShell.Tests;

public sealed class TrayTests
{
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_STATE = 8, NIF_INFO = 0x10, NIF_GUID = 0x20, NIF_SHOWTIP = 0x80;
    private static readonly Guid IconGuid = new("0d2d1e44-5e0a-4a65-9b3c-6c7c8f2b7a10");

    /// <summary>Builds the WM_COPYDATA payload Shell_NotifyIcon sends: SHELLTRAYDATA with a NOTIFYICONDATA of the given size.</summary>
    private static byte[] TrayData(
        uint command, int size = 956, uint window = 0x00010ABC, uint id = 7, uint flags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uint callback = 0x8001, uint icon = 0x00020DEF, string tip = "Hello", uint state = 0, uint stateMask = 0,
        uint version = 0, Guid? guid = null, string info = "", string infoTitle = "", uint infoFlags = 0, uint balloonIcon = 0)
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
            Encoding.Unicode.GetBytes(info).CopyTo(nid[288..]);
            Encoding.Unicode.GetBytes(infoTitle).CopyTo(nid[804..]);
            BinaryPrimitives.WriteUInt32LittleEndian(nid[932..], infoFlags);
        }
        if (size >= 956)
            BinaryPrimitives.WriteUInt32LittleEndian(nid[952..], balloonIcon);
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

    [Fact]
    public void Parses_balloon_text_title_flags_and_icon()
    {
        NotifyIconData data = NotifyIconData.Parse(TrayData(NIM_MODIFY, flags: NIF_INFO, info: "Backup finished",
            infoTitle: "Backup", infoFlags: 0x24, balloonIcon: 0x8003_0004))!;

        Assert.Equal(("Backup finished", "Backup"), (data.Info, data.InfoTitle));
        Assert.Equal(BalloonFlags.User | BalloonFlags.LargeIcon, data.InfoFlags);
        Assert.Equal(unchecked((nint)(int)0x8003_0004), data.BalloonIcon);
    }

    [Fact]
    public void Ignores_balloon_fields_without_NIF_INFO_and_the_icon_in_older_sizes()
    {
        Assert.Equal("", NotifyIconData.Parse(TrayData(NIM_MODIFY, flags: NIF_TIP, info: "x"))!.Info);

        NotifyIconData v2 = NotifyIconData.Parse(TrayData(NIM_MODIFY, size: 936, flags: NIF_INFO, info: "x", infoFlags: 4))!;
        Assert.Equal(("x", BalloonFlags.User, (nint)0), (v2.Info, v2.InfoFlags, v2.BalloonIcon));
    }

    [Fact]
    public void Balloon_messages_have_no_anchor_for_version_4_icons()
    {
        Assert.Equal(((nint)0, (nint)((7 << 16) | 0x0405)), NotifyIconInput.Message(7, 4, BalloonEvent.Clicked));
        Assert.Equal(((nint)7, (nint)0x0402), NotifyIconInput.Message(7, 0, BalloonEvent.Shown));
    }

    [Fact]
    public void Balloon_without_a_title_shows_its_text_as_the_title()
    {
        Assert.Equal(("Disk full", "Only 2 GB left."), TrayBalloon.Texts("Disk full", "Only 2 GB left."));
        Assert.Equal(("Only 2 GB left.", ""), TrayBalloon.Texts("", "Only 2 GB left."));
    }

    [Fact]
    public void Balloon_app_is_the_apps_own_else_explorers_generated_id_else_the_implicit_one()
    {
        Assert.Equal("Contoso.App", TrayBalloon.AppIdFor("Contoso.App", "123", @"C:pp.exe"));
        Assert.Equal("NotifyIconGeneratedAumid_123", TrayBalloon.AppIdFor(null, "123", @"C:pp.exe"));
        Assert.Equal(@"C:pp.exe", TrayBalloon.AppIdFor(null, null, @"C:pp.exe"));
    }

    [Fact]
    public void NotifyIconSettings_match_the_executable_and_the_guid_or_id()
    {
        Guid programFiles = new("6D809377-6AF0-444B-8957-A3773F02200E");
        string? Folder(Guid folder) => folder == programFiles ? @"C:\Program Files" : null;
        const string Vlc = @"C:\Program Files\VideoLAN\VLClc.exe";

        Assert.True(NotifyIconSettings.Matches(@"{6D809377-6AF0-444B-8957-A3773F02200E}\VideoLAN\VLClc.exe", 0, null, Vlc, 0, null, Folder));
        Assert.True(NotifyIconSettings.Matches(Vlc.ToUpperInvariant(), 0, null, Vlc, 0, null, Folder));
        Assert.False(NotifyIconSettings.Matches(Vlc, 1, null, Vlc, 0, null, Folder));
        Assert.False(NotifyIconSettings.Matches(@"C:\other.exe", 0, null, Vlc, 0, null, Folder));

        Assert.True(NotifyIconSettings.Matches(Vlc, null, IconGuid, Vlc, 5, IconGuid, Folder));
        Assert.False(NotifyIconSettings.Matches(Vlc, null, Guid.NewGuid(), Vlc, 5, IconGuid, Folder));
        Assert.False(NotifyIconSettings.Matches(Vlc, null, IconGuid, Vlc, 0, null, Folder));
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

    [Fact]
    public void Store_accepts_windows_own_system_icons_but_marks_them_not_to_show()
    {
        var store = new TrayIconStore();
        var volume = new Guid("7820ae73-23e3-4229-82c1-e41cb67d5b9c");
        var hotPlug = new Guid("7820ae78-23e3-4229-82c1-e41cb67d5b9c");

        Assert.True(store.Apply(Data(NotifyIconCommand.Add, id: 1, guid: volume, flags: NotifyIconFlags.Guid)));
        Assert.True(store.Apply(Data(NotifyIconCommand.Add, id: 2, guid: hotPlug, flags: NotifyIconFlags.Guid)));
        Assert.True(store.Apply(Data(NotifyIconCommand.Add, id: 3)));

        Assert.Equal([true, false, false], store.Icons.Select(icon => icon.IsSystemIcon));
    }

    [Fact]
    public void Taskbar_list_progress_state_names_the_window()
    {
        TaskbarListCall call = TaskbarListCall.Parse(0x441, 42, (nint)TaskbarProgressState.Error)!;

        Assert.Equal(new TaskbarListCall(TaskbarListCallKind.ProgressState, 42, (int)TaskbarProgressState.Error), call);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0x7FFF, 0x7FFF / (double)0xFFFE)]
    [InlineData(0xFFFE, 1)]
    [InlineData(0xFFFF, 1)] // past the end
    public void Taskbar_list_progress_value_is_a_fraction(long value, double expected)
    {
        TaskbarListCall call = TaskbarListCall.Parse(0x440, 42, (nint)value)!;

        Assert.Equal(TaskbarListCallKind.ProgressValue, call.Kind);
        Assert.Equal(expected, call.Value, 6);
    }

    [Fact]
    public void Taskbar_list_overlay_carries_the_icon_and_full_screen_swaps_its_arguments()
    {
        Assert.Equal(new TaskbarListCall(TaskbarListCallKind.OverlayIcon, 42, 0x1234), TaskbarListCall.Parse(0x44F, 42, 0x1234));
        // MarkFullscreenWindow sends the flag in wParam and the window in lParam.
        Assert.Equal(new TaskbarListCall(TaskbarListCallKind.FullScreen, 42, 1), TaskbarListCall.Parse(0x43C, 1, 42));
        Assert.Equal(new TaskbarListCall(TaskbarListCallKind.FullScreen, 42, 0), TaskbarListCall.Parse(0x43C, 0, 42));
        Assert.Null(TaskbarListCall.Parse(0x455, 42, 7));
    }
}
