using NeoShell.Taskbar;

namespace NeoShell.Tests;

public sealed class TaskButtonMotionTests
{
    [Fact]
    public void The_pills_are_Explorers_lengths_for_a_24_px_icon()
    {
        // Taskbar.View.dll's GetRunningIndicatorExtent: the icon size times 2/3 when active, 1/4 when running.
        Assert.Equal(16, TaskButtonMotion.ActivePillWidth, 6);
        Assert.Equal(6, TaskButtonMotion.RunningPillWidth, 6);
    }

    [Fact]
    public void A_pill_that_takes_over_starts_at_the_old_ones_length()
    {
        // Minimized: the 6 px running pill starts scaled to 16 px; restored: the 16 px pill starts at 6 px.
        Assert.Equal(16f / 6, TaskButtonMotion.PillStartScale(TaskButtonMotion.ActivePillWidth, TaskButtonMotion.RunningPillWidth), 4);
        Assert.Equal(6f / 16, TaskButtonMotion.PillStartScale(TaskButtonMotion.RunningPillWidth, TaskButtonMotion.ActivePillWidth), 4);
    }

    [Fact]
    public void A_minimized_windows_icon_dips_3_px_rises_1_px_past_its_place_and_settles()
    {
        Assert.Equal([(0.25f, 3f), (0.5f, -1f), (1f, 0f)], TaskButtonMotion.Bounce(minimized: true));
    }

    [Fact]
    public void A_restored_windows_icon_rises_3_px_drops_1_px_past_its_place_and_settles()
    {
        Assert.Equal([(0.25f, -3f), (0.5f, 1f), (1f, 0f)], TaskButtonMotion.Bounce(minimized: false));
    }

    [Fact]
    public void The_timings_are_Explorers()
    {
        // SharedAnimations::Initialize, in 100 ns ticks: 3,330,000; 830,000 and 6,670,000; 1,670,000 and 5,000,000.
        Assert.Equal(TimeSpan.FromMilliseconds(333), TaskButtonMotion.PillDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(83), TaskButtonMotion.BounceDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(667), TaskButtonMotion.BounceDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(167), TaskButtonMotion.PressDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(500), TaskButtonMotion.ReleaseDuration);
    }
}
