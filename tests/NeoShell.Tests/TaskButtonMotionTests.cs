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
    public void Each_indicator_has_Explorers_length()
    {
        // GetRunningIndicatorExtent: 32 for a flashing button with a 24 px icon; the progress bar is the icon's width.
        Assert.Equal(6, TaskButtonMotion.PillWidth(TaskPill.Running), 6);
        Assert.Equal(16, TaskButtonMotion.PillWidth(TaskPill.Active), 6);
        Assert.Equal(32, TaskButtonMotion.PillWidth(TaskPill.Attention), 6);
        Assert.Equal(24, TaskButtonMotion.PillWidth(TaskPill.Progress), 6);
    }

    [Fact]
    public void A_blink_fades_the_plate_out_in_a_third_holds_and_brings_it_back_by_two_thirds()
    {
        // StartRequestingAttentionAnimation's key frames: 0.33, 0.56, 0.67 and 1 (linear), from where the plate is.
        Assert.Equal([(0.33f, 0f), (0.56f, 0f), (0.67f, 1f), (1f, 1f)], TaskButtonMotion.BlinkFrames(active: false));
        Assert.Equal([(0.33f, 0.5f), (0.56f, 0.5f), (0.67f, 1f), (1f, 1f)], TaskButtonMotion.BlinkFrames(active: true));
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
        // 15,000,000 ticks a blink; recorded: one every ~1.68 s.
        Assert.Equal(TimeSpan.FromMilliseconds(1500), TaskButtonMotion.BlinkDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(180), TaskButtonMotion.BlinkGap);
    }
}
