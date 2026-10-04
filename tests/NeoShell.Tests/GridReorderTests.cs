using NeoShell.StartMenu;

namespace NeoShell.Tests;

public sealed class GridReorderTests
{
    // Two rows of three 92 by 84 cells.
    private static readonly (double X, double Y)[] Slots = [(0, 0), (92, 0), (184, 0), (0, 84), (92, 84), (184, 84)];

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(40, 10, 0)] // less than half a cell over
    [InlineData(50, 10, 1)] // more than half
    [InlineData(180, 90, 5)]
    [InlineData(500, 500, 5)] // beyond the grid: the nearest cell
    public void Target_is_the_slot_nearest_to_the_dragged_icon(double x, double y, int expected)
    {
        Assert.Equal(expected, GridReorder.TargetIndex(Slots, (x, y)));
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
