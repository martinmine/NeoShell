using NeoShell.Interop.Shell;

namespace NeoShell.Tests;

public sealed class PowerTests
{
    private static readonly PowerOptions s_usual = new() { Sleep = true, Lock = true, SignOut = true, SwitchUser = true };

    [Fact]
    public void Each_menu_has_Explorers_choices_in_its_order()
    {
        Assert.Equal([PowerChoice.Lock, PowerChoice.Sleep, PowerChoice.ShutDown, PowerChoice.Restart], s_usual.Choices(PowerMenu.Start));
        Assert.Equal([PowerChoice.SignOut, PowerChoice.Sleep, PowerChoice.ShutDown, PowerChoice.Restart], s_usual.Choices(PowerMenu.QuickLink));
        Assert.Equal(
            [PowerChoice.SwitchUser, PowerChoice.SignOut, PowerChoice.Sleep, PowerChoice.ShutDown, PowerChoice.Restart],
            s_usual.Choices(PowerMenu.ShutDownDialog));
    }

    [Fact]
    public void Hibernate_follows_sleep()
    {
        PowerOptions options = s_usual with { Sleep = false, Hibernate = true };
        Assert.Equal([PowerChoice.Lock, PowerChoice.Hibernate, PowerChoice.ShutDown, PowerChoice.Restart], options.Choices(PowerMenu.Start));
        Assert.Equal(
            [PowerChoice.Lock, PowerChoice.Sleep, PowerChoice.Hibernate, PowerChoice.ShutDown, PowerChoice.Restart],
            (options with { Sleep = true }).Choices(PowerMenu.Start));
    }

    [Theory]
    [InlineData(0x8, new[] { PowerChoice.UpdateAndShutDown, PowerChoice.Restart })]
    [InlineData(0x2, new[] { PowerChoice.ShutDown, PowerChoice.UpdateAndRestart })]
    [InlineData(0xA, new[] { PowerChoice.UpdateAndShutDown, PowerChoice.UpdateAndRestart })]
    [InlineData(0xF, new[] { PowerChoice.UpdateAndShutDown, PowerChoice.ShutDown, PowerChoice.UpdateAndRestart, PowerChoice.Restart })]
    [InlineData(0x5, new[] { PowerChoice.ShutDown, PowerChoice.Restart })]
    [InlineData(0x1A, new[] { PowerChoice.ShutDown, PowerChoice.Restart })]
    public void Update_choices_replace_or_join_shut_down_and_restart(int flags, PowerChoice[] expected)
    {
        PowerOptions options = new() { UpdateFlags = flags };
        Assert.Equal(expected, options.Choices(PowerMenu.Start));
    }

    [Fact]
    public void Policies_hide_the_power_states_but_not_signing_out()
    {
        PowerOptions hidden = s_usual with { PowerHidden = true, Hibernate = true, UpdateFlags = 0xA };
        Assert.Equal([PowerChoice.Lock], hidden.Choices(PowerMenu.Start));
        Assert.Equal([PowerChoice.SwitchUser, PowerChoice.SignOut], hidden.Choices(PowerMenu.ShutDownDialog));

        PowerOptions some = s_usual with { ShutDownHidden = true, Lock = false, SignOut = false, SwitchUser = false };
        Assert.Equal([PowerChoice.Sleep, PowerChoice.Restart], some.Choices(PowerMenu.ShutDownDialog));
        Assert.Equal([PowerChoice.Sleep, PowerChoice.ShutDown], (some with { ShutDownHidden = false, RestartHidden = true }).Choices(PowerMenu.Start));
    }

    [Fact]
    public void The_dialog_chooses_updating_then_the_power_button_action_then_shutting_down()
    {
        PowerOptions updates = s_usual with { UpdateFlags = 0xA, PreferredChoice = PowerChoice.Sleep };
        Assert.Equal(PowerChoice.UpdateAndShutDown, updates.DefaultChoice(updates.Choices(PowerMenu.ShutDownDialog)));

        PowerOptions sleep = s_usual with { PreferredChoice = PowerChoice.Sleep };
        Assert.Equal(PowerChoice.Sleep, sleep.DefaultChoice(sleep.Choices(PowerMenu.ShutDownDialog)));

        PowerOptions unavailable = s_usual with { PreferredChoice = PowerChoice.Hibernate };
        Assert.Equal(PowerChoice.ShutDown, unavailable.DefaultChoice(unavailable.Choices(PowerMenu.ShutDownDialog)));

        PowerOptions noShutDown = s_usual with { ShutDownHidden = true, SwitchUser = false };
        Assert.Equal(PowerChoice.SignOut, noShutDown.DefaultChoice(noShutDown.Choices(PowerMenu.ShutDownDialog)));
    }

    [Theory]
    [InlineData(0x1, PowerChoice.SignOut)]
    [InlineData(0x2, PowerChoice.ShutDown)]
    [InlineData(0x4, PowerChoice.Restart)]
    [InlineData(0x10, PowerChoice.Sleep)]
    [InlineData(0x40, PowerChoice.Hibernate)]
    [InlineData(0x100, PowerChoice.SwitchUser)]
    [InlineData(0x200, PowerChoice.Lock)]
    [InlineData(0x8, null)]
    [InlineData(null, null)]
    public void Power_button_action_codes_map_to_choices(int? code, PowerChoice? expected)
    {
        Assert.Equal(expected, PowerOptions.ChoiceFromCode(code));
    }

    [Theory]
    // Shut down is hybrid (Fast Startup) unless it updates or Shift is held.
    [InlineData(false, false, false, false, false, 0x0208)]
    [InlineData(false, false, true, false, false, 0x0008)]
    [InlineData(false, true, false, false, false, 0x0048)]
    [InlineData(false, true, true, false, false, 0x0048)]
    // Shift restarts into the boot options, and doesn't update.
    [InlineData(true, false, false, false, false, 0x0004)]
    [InlineData(true, false, true, false, false, 0x0404)]
    [InlineData(true, true, false, false, false, 0x0044)]
    [InlineData(true, true, true, false, false, 0x0404)]
    // ARSO signs back in after an update, and after a plain restart or shut down unless the PC is joined.
    [InlineData(true, false, false, true, false, 0x2004)]
    [InlineData(true, false, false, true, true, 0x0004)]
    [InlineData(true, true, false, true, true, 0x2044)]
    [InlineData(false, false, false, true, false, 0x2208)]
    [InlineData(false, true, false, true, true, 0x2048)]
    public void Shutdown_flags_are_shutdownuxs(bool restart, bool installUpdates, bool shift, bool arso, bool joined, uint expected)
    {
        Assert.Equal(expected, Power.ShutdownFlags(restart, installUpdates, shift, arso, joined));
    }

    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(0, 20, " (estimate: up to 20 min)")]
    [InlineData(0, 60, " (estimate: up to 1 hr)")]
    [InlineData(15, 0, " (estimate: more than 15 min)")]
    [InlineData(60, 0, " (estimate: more than 1 hr)")]
    [InlineData(10, 10, " (estimate: 10 min)")]
    [InlineData(60, 60, " (estimate: 1 hr)")]
    [InlineData(5, 15, " (estimate: 5 - 15 min)")]
    [InlineData(20, 60, " (estimate: 20 min - 1 hr)")]
    public void Update_estimates_are_worded_as_shutdownux_words_them(int low, int high, string expected)
    {
        Assert.Equal(expected, PowerItems.Estimate(low, high));
    }
}
