using NeoShell.Taskbar;

namespace NeoShell.Tests;

public sealed class TaskOrderTests
{
    private static TaskButtonModel[] Buttons(params string[] keys) => [.. keys.Select(key => new TaskButtonModel(key, null, []))];

    private static string[] Keys(IReadOnlyList<TaskButtonModel> buttons) => [.. buttons.Select(button => button.Key)];

    [Fact]
    public void Without_an_order_the_natural_order_stays()
    {
        Assert.Equal(["a", "b", "c"], Keys(TaskOrder.Arrange(Buttons("a", "b", "c"), [])));
    }

    [Fact]
    public void A_dropped_running_app_keeps_its_place()
    {
        // "c" isn't pinned, so the builder puts it after the pinned ones; the user dropped it first.
        Assert.Equal(["c", "a", "b"], Keys(TaskOrder.Arrange(Buttons("a", "b", "c"), ["c", "a", "b"])));
    }

    [Fact]
    public void New_buttons_follow_the_button_they_follow_naturally()
    {
        // A second window of "a" ("a2") opens; "d" is a new app at the end.
        Assert.Equal(["c", "a", "a2", "b", "d"], Keys(TaskOrder.Arrange(Buttons("a", "a2", "b", "c", "d"), ["c", "a", "b"])));
    }

    [Fact]
    public void A_new_first_button_goes_first()
    {
        Assert.Equal(["z", "b", "a"], Keys(TaskOrder.Arrange(Buttons("z", "a", "b"), ["b", "a"])));
    }

    [Fact]
    public void Gone_buttons_are_dropped_from_the_order()
    {
        Assert.Equal(["b", "a"], Keys(TaskOrder.Arrange(Buttons("a", "b"), ["b", "gone", "a"])));
    }
}
