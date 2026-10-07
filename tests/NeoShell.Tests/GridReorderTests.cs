using NeoShell.StartMenu;

namespace NeoShell.Tests;

public sealed class GridReorderTests
{
    // Two rows of three 92 by 84 cells.
    private static readonly (double X, double Y)[] Slots = [(0, 0), (92, 0), (184, 0), (0, 84), (92, 84), (184, 84)];

    // The same cells' centres.
    private static readonly (double X, double Y)[] Centres = [(46, 42), (138, 42), (230, 42), (46, 126), (138, 126), (230, 126)];

    [Theory]
    [InlineData(46, 42, 0)] // its own cell
    [InlineData(85, 42, 0)] // the right of its own cell
    [InlineData(115, 42, 0)] // the left of the next cell: before it, which is where it is
    [InlineData(170, 42, 1)] // the right of the next cell: after it
    [InlineData(200, 50, 1)] // the left of the third
    [InlineData(260, 130, 5)] // the right of the last
    [InlineData(500, 500, 5)] // beyond the grid: by the nearest cell
    public void Dragging_forward_drops_before_or_after_the_cell_under_the_pointer(double x, double y, int expected)
    {
        Assert.Equal((expected, false), GridReorder.DropTarget(Centres, (x, y), 0, _ => false));
    }

    [Theory]
    [InlineData(10, 42, 0)] // the left of the first
    [InlineData(80, 42, 1)] // the right of the first
    [InlineData(105, 126, 4)] // its own cell
    public void Dragging_back_drops_before_or_after_the_cell_under_the_pointer(double x, double y, int expected)
    {
        Assert.Equal((expected, false), GridReorder.DropTarget(Centres, (x, y), 4, _ => false));
    }

    [Theory]
    [InlineData(138, 42, 1)]
    [InlineData(138 + GridReorder.GroupZone, 42 - GridReorder.GroupZone, 1)]
    [InlineData(46, 126, 3)]
    public void Near_the_middle_of_another_cell_the_drop_groups(double x, double y, int expected)
    {
        Assert.Equal((expected, true), GridReorder.DropTarget(Centres, (x, y), 0, _ => true));
    }

    [Fact]
    public void A_cell_that_cant_take_the_app_is_dropped_beside_instead()
    {
        Assert.Equal((1, false), GridReorder.DropTarget(Centres, (140, 42), 0, i => i != 1));
        Assert.Equal((0, false), GridReorder.DropTarget(Centres, (46, 42), 0, _ => true));
    }

    [Fact]
    public void An_app_from_outside_goes_before_or_after_the_nearest_cell()
    {
        Assert.Equal((1, false), GridReorder.DropTarget(Centres, (100, 42), -1, _ => false));
        Assert.Equal((6, false), GridReorder.DropTarget(Centres, (300, 126), -1, _ => false));
        Assert.Equal((3, false), GridReorder.DropTarget(Centres, (10, 140), -1, _ => false));
    }

    [Fact]
    public void Icons_between_move_back_one_slot_when_dragging_forward()
    {
        // Dragging the first icon onto the fifth: the second to the fifth move back, wrapping to the row above.
        Assert.Equal((-92.0, 0.0), GridReorder.MakeWayOffset(Slots, 0, 4, 1));
        Assert.Equal((184.0, -84.0), GridReorder.MakeWayOffset(Slots, 0, 4, 3));
        Assert.Equal((0.0, 0.0), GridReorder.MakeWayOffset(Slots, 0, 4, 5));
    }

    [Fact]
    public void Icons_between_move_on_one_slot_when_dragging_back()
    {
        Assert.Equal((-184.0, 84.0), GridReorder.MakeWayOffset(Slots, 4, 2, 2));
        Assert.Equal((92.0, 0.0), GridReorder.MakeWayOffset(Slots, 4, 2, 3));
        Assert.Equal((0.0, 0.0), GridReorder.MakeWayOffset(Slots, 4, 2, 1));
    }
}
