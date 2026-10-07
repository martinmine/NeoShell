using NeoShell.Taskbar;

namespace NeoShell.Tests;

public sealed class TaskbarSearchTests
{
    [Theory]
    [InlineData(0, TaskbarSearchMode.Hidden)]
    [InlineData(1, TaskbarSearchMode.Icon)]
    [InlineData(2, TaskbarSearchMode.Box)]
    [InlineData(3, TaskbarSearchMode.IconAndLabel)]
    // Explorer shows the box for anything else.
    [InlineData(7, TaskbarSearchMode.Box)]
    [InlineData(-1, TaskbarSearchMode.Box)]
    public void Setting_is_read_as_Explorer_reads_it(int value, TaskbarSearchMode expected)
    {
        Assert.Equal(expected, TaskbarSearch.FromValue(value));
    }

    [Fact]
    public void Missing_or_mistyped_setting_means_the_box()
    {
        Assert.Equal(TaskbarSearchMode.Box, TaskbarSearch.FromValue(null));
        Assert.Equal(TaskbarSearchMode.Box, TaskbarSearch.FromValue("1"));
    }

    [Fact]
    public void Each_look_takes_Explorers_room()
    {
        Assert.Equal(0, TaskbarSearch.Width(TaskbarSearchMode.Hidden));
        Assert.Equal(44, TaskbarSearch.Width(TaskbarSearchMode.Icon));
        Assert.Equal(224, TaskbarSearch.Width(TaskbarSearchMode.Box));
        Assert.Equal(106, TaskbarSearch.Width(TaskbarSearchMode.IconAndLabel));
    }

    [Theory]
    [InlineData(TaskbarSearchMode.Box)]
    [InlineData(TaskbarSearchMode.IconAndLabel)]
    public void Box_and_label_give_way_to_the_icon_when_the_buttons_dont_fit(TaskbarSearchMode mode)
    {
        Assert.Equal(mode, TaskbarSearch.Shown(mode, taskWidth: 1000, available: 1000));
        Assert.Equal(TaskbarSearchMode.Icon, TaskbarSearch.Shown(mode, taskWidth: 1001, available: 1000));
    }

    [Theory]
    [InlineData(TaskbarSearchMode.Hidden)]
    [InlineData(TaskbarSearchMode.Icon)]
    public void Icon_and_hidden_stay_as_they_are(TaskbarSearchMode mode)
    {
        Assert.Equal(mode, TaskbarSearch.Shown(mode, taskWidth: 2000, available: 1000));
    }
}
