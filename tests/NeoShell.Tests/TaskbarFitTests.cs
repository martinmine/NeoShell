using NeoShell.Taskbar;

namespace NeoShell.Tests;

public sealed class TaskbarFitTests
{
    [Theory]
    [InlineData(38, 90)] // "Claude"
    [InlineData(43.5, 96)] // "T39 Test"
    [InlineData(500, 180)] // a long title stops at Explorer's widest
    public void A_labelled_button_is_as_wide_as_its_label_needs(double labelWidth, double expected)
    {
        Assert.Equal(expected, TaskbarFit.NaturalWidth(labelWidth));
    }

    [Fact]
    public void Buttons_that_fit_keep_their_natural_widths()
    {
        Assert.Equal([44, 90, 180], TaskbarFit.Widths([44, 90, 180], room: 314));
    }

    [Fact]
    public void A_full_taskbar_narrows_labelled_buttons_as_Explorers()
    {
        // Measured on Explorer: two pinned apps, then eight labelled buttons in 1179 px; their widths read 44, 44,
        // 90, 174, 174, 174, 95, 99, 113, 174 (UI Automation, whole pixels).
        double[] widths = TaskbarFit.Widths([44, 44, 90, 180, 180, 180, 96, 100, 115, 180], room: 1179);

        double[] expected = [44, 44, 90, 174, 174, 174, 95, 99, 113, 174];
        Assert.All(widths.Zip(expected), pair => Assert.InRange(pair.First, pair.Second - 1, pair.Second + 1));
        Assert.Equal(1179, widths.Sum(), precision: 6);
    }

    [Fact]
    public void Icons_and_short_labels_never_narrow_below_their_own_width()
    {
        double[] widths = TaskbarFit.Widths([44, 70, 180], room: 200);

        Assert.Equal(44, widths[0]);
        Assert.Equal(70, widths[1]);
        Assert.Equal(86, widths[2], precision: 6);
    }

    [Fact]
    public void Buttons_that_dont_fit_even_at_their_narrowest_stay_at_it()
    {
        Assert.Equal([44, TaskbarFit.LabelledMinWidth, TaskbarFit.LabelledMinWidth], TaskbarFit.Widths([44, 180, 120], room: 100));
    }
}
