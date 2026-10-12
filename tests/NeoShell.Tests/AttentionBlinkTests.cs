using NeoShell.Interop.Tray;
using NeoShell.Taskbar;

namespace NeoShell.Tests;

// Each case is one recorded on Explorer (docs/design/taskbar.md): FlashWindowEx's messages, then the blinks.
public sealed class AttentionBlinkTests
{
    [Fact]
    public void A_flash_lights_the_button_and_starts_a_blink()
    {
        var blink = new AttentionBlink(active: false);

        Assert.True(blink.Update(flashing: true, messages: 1, active: false));
        Assert.True(blink.Shows);
        Assert.True(blink.Blinking);
    }

    [Fact]
    public void Blinks_follow_while_flash_messages_come_and_a_counted_flash_leaves_the_button_lit()
    {
        // FLASHW_TRAY, 1: on, off half a second later, on again a second in; Explorer blinks twice.
        var blink = new AttentionBlink(active: false);
        blink.Update(flashing: true, messages: 1, active: false);
        blink.Update(flashing: false, messages: 2, active: false);
        blink.Update(flashing: true, messages: 3, active: false);

        Assert.True(blink.BlinkEnded());
        Assert.False(blink.BlinkEnded());
        Assert.True(blink.Shows);
        Assert.False(blink.Blinking);
    }

    [Fact]
    public void A_flash_stopped_while_blinking_ends_with_the_normal_look()
    {
        var blink = new AttentionBlink(active: false);
        blink.Update(flashing: true, messages: 1, active: false);
        blink.Update(flashing: false, messages: 2, active: false);

        Assert.True(blink.BlinkEnded());
        Assert.False(blink.BlinkEnded());
        Assert.False(blink.Shows);
    }

    [Fact]
    public void Activating_a_lit_buttons_window_plays_one_more_blink_then_ends_it()
    {
        var blink = new AttentionBlink(active: false);
        blink.Update(flashing: true, messages: 1, active: false);
        blink.BlinkEnded();

        // Activation ends the flash.
        Assert.True(blink.Update(flashing: false, messages: 1, active: true));
        Assert.False(blink.WasActive);
        Assert.False(blink.BlinkEnded());
        Assert.False(blink.Shows);
    }

    [Fact]
    public void A_flash_stopped_while_the_button_is_lit_leaves_it_lit()
    {
        // Explorer only looks again at the end of a blink.
        var blink = new AttentionBlink(active: false);
        blink.Update(flashing: true, messages: 1, active: false);
        blink.BlinkEnded();

        Assert.False(blink.Update(flashing: false, messages: 2, active: false));
        Assert.True(blink.Shows);
    }

    [Fact]
    public void Closing_the_window_that_flashed_ends_the_attention_look()
    {
        var blink = new AttentionBlink(active: false);
        blink.Update(flashing: true, messages: 1, active: false);
        blink.BlinkEnded();

        Assert.False(blink.Update(flashing: false, messages: 0, active: false));
        Assert.False(blink.Shows);
    }

    [Fact]
    public void A_window_in_front_blinks_its_plate_to_half()
    {
        var blink = new AttentionBlink(active: true);

        Assert.True(blink.Update(flashing: true, messages: 1, active: true));
        Assert.True(blink.WasActive);
    }

    [Fact]
    public void A_new_flash_while_lit_starts_blinking_again_once_the_last_one_turned_off()
    {
        var blink = new AttentionBlink(active: false);
        blink.Update(flashing: true, messages: 1, active: false);
        blink.BlinkEnded();

        Assert.False(blink.Update(flashing: true, messages: 2, active: false));
        Assert.False(blink.Update(flashing: false, messages: 3, active: false));
        Assert.True(blink.Update(flashing: true, messages: 4, active: false));
    }

    [Theory]
    [InlineData(TaskbarProgressState.Normal, 0, false)]
    [InlineData(TaskbarProgressState.Normal, 0.5, true)]
    [InlineData(TaskbarProgressState.Error, 0, true)]
    [InlineData(TaskbarProgressState.Paused, 0, true)]
    [InlineData(TaskbarProgressState.Indeterminate, 0, true)]
    public void Normal_progress_at_0_shows_the_pill_instead_of_the_bar(TaskbarProgressState state, double value, bool bar)
    {
        Assert.Equal(bar, new TaskProgress(state, value).ShowsBar);
    }
}
