using NeoShell.Switcher;

namespace NeoShell.Tests;

public sealed class SwitcherTests
{
    private const int Alt = 0xA4;
    private const int Shift = 0xA0;
    private const int Control = 0xA2;
    private const int Tab = 0x09;
    private const int Escape = 0x1B;
    private const int Left = 0x25;
    private const int Down = 0x28;
    private const int Delete = 0x2E;
    private const int Enter = 0x0D;

    [Fact]
    public void Alt_Tab_opens_the_switcher_and_letting_go_of_Alt_switches()
    {
        var keys = new AltTabKeys();

        Assert.False(keys.OnKey(Alt, true, out _));
        Assert.True(keys.OnKey(Tab, true, out SwitcherCommand? command));
        Assert.Equal(SwitcherCommand.Open, command);
        Assert.True(keys.OnKey(Tab, false, out command));
        Assert.Null(command);
        Assert.True(keys.OnKey(Tab, true, out command));
        Assert.Equal(SwitcherCommand.Next, command);
        Assert.True(keys.OnKey(Tab, false, out _));

        // Alt itself always reaches Windows.
        Assert.False(keys.OnKey(Alt, false, out command));
        Assert.Equal(SwitcherCommand.Switch, command);
        Assert.False(keys.IsOpen);
    }

    [Fact]
    public void Shift_goes_backwards()
    {
        var keys = new AltTabKeys();
        keys.OnKey(Alt, true, out _);
        keys.OnKey(Shift, true, out _);

        keys.OnKey(Tab, true, out SwitcherCommand? command);
        Assert.Equal(SwitcherCommand.OpenBackwards, command);
        keys.OnKey(Tab, true, out command);
        Assert.Equal(SwitcherCommand.Previous, command);
    }

    [Fact]
    public void Tab_without_Alt_or_with_Ctrl_is_left_alone()
    {
        var keys = new AltTabKeys();
        Assert.False(keys.OnKey(Tab, true, out SwitcherCommand? command));
        Assert.Null(command);
        Assert.False(keys.OnKey(Tab, false, out _));

        keys.OnKey(Control, true, out _);
        keys.OnKey(Alt, true, out _);
        Assert.False(keys.OnKey(Tab, true, out command));
        Assert.Null(command);
    }

    [Fact]
    public void While_open_the_arrows_Delete_and_Escape_are_the_switchers()
    {
        var keys = new AltTabKeys();
        keys.OnKey(Alt, true, out _);
        keys.OnKey(Tab, true, out _);

        Assert.True(keys.OnKey(Left, true, out SwitcherCommand? command));
        Assert.Equal(SwitcherCommand.Previous, command);
        Assert.True(keys.OnKey(Down, true, out command));
        Assert.Equal(SwitcherCommand.Down, command);
        Assert.True(keys.OnKey(Delete, true, out command));
        Assert.Equal(SwitcherCommand.CloseWindow, command);
        Assert.True(keys.OnKey(Escape, true, out command));
        Assert.Equal(SwitcherCommand.Cancel, command);
        Assert.False(keys.IsOpen);

        // Its release is still swallowed; letting go of Alt then switches to nothing.
        Assert.True(keys.OnKey(Escape, false, out _));
        Assert.False(keys.OnKey(Alt, false, out command));
        Assert.Null(command);
    }

    [Fact]
    public void Enter_switches_while_Alt_is_still_held_and_arrows_pass_through_when_closed()
    {
        var keys = new AltTabKeys();
        keys.OnKey(Alt, true, out _);
        keys.OnKey(Tab, true, out _);

        Assert.True(keys.OnKey(Enter, true, out SwitcherCommand? command));
        Assert.Equal(SwitcherCommand.Switch, command);
        Assert.False(keys.OnKey(Left, true, out command));
        Assert.Null(command);
    }

    [Fact]
    public void Order_is_the_window_in_front_then_the_stack()
    {
        nint[] windows = [1, 2, 3, 4];
        nint[] zOrder = [100, 3, 1, 4]; // 100 is someone else's topmost window; 2 isn't listed

        Assert.Equal(new nint[] { 4, 3, 1, 2 }, AltTabLayout.Order(windows, zOrder, foreground: 4));
    }

    [Theory]
    [InlineData(0, false, -1)]
    [InlineData(1, false, 0)]
    [InlineData(5, false, 1)]
    [InlineData(5, true, 4)]
    public void First_selection_is_the_previous_window(int count, bool backwards, int expected)
    {
        Assert.Equal(expected, AltTabLayout.FirstSelection(count, backwards));
    }

    [Fact]
    public void Rows_wrap_and_are_centred_in_the_widest()
    {
        (IReadOnlyList<SwitcherSlot> slots, double width) = AltTabLayout.Arrange([100, 100, 100, 50], maxWidth: 330, spacing: 10);

        Assert.Equal(320, width);
        Assert.Equal(new SwitcherSlot(0, 0, 100), slots[0]);
        Assert.Equal(new SwitcherSlot(0, 220, 100), slots[2]);
        Assert.Equal(new SwitcherSlot(1, 135, 50), slots[3]);
    }

    [Fact]
    public void An_item_wider_than_the_grid_gets_a_row_of_its_own()
    {
        (IReadOnlyList<SwitcherSlot> slots, double width) = AltTabLayout.Arrange([400, 100], maxWidth: 300, spacing: 10);

        Assert.Equal(400, width);
        Assert.Equal(0, slots[0].Row);
        Assert.Equal(1, slots[1].Row);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(3, 1, 0)]
    [InlineData(0, -1, 3)]
    public void Steps_go_round_the_end(int index, int delta, int expected)
    {
        Assert.Equal(expected, AltTabLayout.Step(index, 4, delta));
    }

    [Fact]
    public void Up_and_down_take_the_nearest_item_in_the_next_row()
    {
        (IReadOnlyList<SwitcherSlot> slots, _) = AltTabLayout.Arrange([100, 100, 100, 50], maxWidth: 330, spacing: 10);

        Assert.Equal(3, AltTabLayout.Vertical(slots, 1, 1));
        Assert.Equal(1, AltTabLayout.Vertical(slots, 3, -1));
        Assert.Equal(1, AltTabLayout.Vertical(slots, 1, -1)); // already at the top
    }
}
