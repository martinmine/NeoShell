using NeoShell.Taskbar;

namespace NeoShell.Tests;

public sealed class TaskReorderTests
{
    // Five 44 px buttons, as on Explorer's taskbar.
    private static readonly (double Left, double Width)[] Slots = [(0, 44), (44, 44), (88, 44), (132, 44), (176, 44)];

    [Theory]
    [InlineData(4, 0)] // within the threshold
    [InlineData(10, 4)] // moves on from where it was, without jumping to the pointer
    [InlineData(-10, -4)]
    [InlineData(500, 88)] // stops at the end of the list
    [InlineData(-500, -88)] // and at the start
    public void Dragged_button_follows_the_pointer_within_the_list(double travel, double expected)
    {
        Assert.Equal(expected, TaskReorder.Offset(Slots, 2, travel));
    }

    [Theory]
    [InlineData(22, 2)] // centre on the neighbour's edge
    [InlineData(23, 3)] // centre inside the neighbour's slot
    [InlineData(66, 3)]
    [InlineData(67, 4)]
    [InlineData(-22, 2)]
    [InlineData(-23, 1)]
    [InlineData(-67, 0)]
    public void Neighbour_makes_way_once_the_buttons_centre_enters_its_slot(double offset, int expected)
    {
        Assert.Equal(expected, TaskReorder.TargetIndex(Slots, 2, offset));
    }

    [Fact]
    public void Wider_neighbour_makes_way_once_the_leading_edge_passes_its_middle()
    {
        // A 44 px button dragged right over a 160 px labelled one, whose middle is at 124.
        (double, double)[] slots = [(0, 44), (44, 160), (204, 44)];

        Assert.Equal(0, TaskReorder.TargetIndex(slots, 0, 80));
        Assert.Equal(1, TaskReorder.TargetIndex(slots, 0, 81));
    }

    [Fact]
    public void Buttons_between_the_slots_slide_by_the_dragged_buttons_width()
    {
        (double, double)[] slots = [(0, 44), (44, 160), (204, 44), (248, 44)];

        // Dragged right from 1 to 3: buttons 2 and 3 slide left by its 160 px.
        Assert.Equal([0, 0, -160, -160], Enumerable.Range(0, 4).Select(i => TaskReorder.MakeWayOffset(slots, 1, 3, i)));
        // Dragged left from 3 to 1: buttons 1 and 2 slide right by its 44 px.
        Assert.Equal([0, 44, 44, 0], Enumerable.Range(0, 4).Select(i => TaskReorder.MakeWayOffset(slots, 3, 1, i)));
    }
}
