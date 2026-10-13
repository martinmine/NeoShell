using NeoShell.Settings;
using NeoShell.Themes;
using NeoShell.Themes.Concept2088;

namespace NeoShell.Tests;

public sealed class Concept2088ThemeTests
{
    [Fact]
    public void The_concept_is_a_theme_of_its_own()
    {
        Assert.Contains(ShellTheme.Concept2088, ShellThemes.All);
        Assert.Equal("2088 (concept)", ShellThemes.Name(ShellTheme.Concept2088));
    }

    [Fact]
    public void A_command_typed_whole_comes_first()
    {
        var match = Assert.Single(NexusIntents.Match("lock"));

        Assert.Equal(NexusCommand.Lock, match.Command);
        Assert.True(match.Exact);
    }

    [Theory]
    [InlineData("re")]
    [InlineData("reb")]
    [InlineData("shut")]
    public void A_half_typed_word_offers_power_commands_but_not_first(string query)
    {
        IReadOnlyList<(NexusCommand Command, string Text, bool Exact)> matches = NexusIntents.Match(query);

        Assert.NotEmpty(matches);
        Assert.All(matches, match => Assert.False(match.Exact));
    }

    [Fact]
    public void One_letter_offers_no_command()
    {
        Assert.Empty(NexusIntents.Match("s"));
        Assert.Empty(NexusIntents.Match(" "));
    }

    [Theory]
    [InlineData("再起動", NexusCommand.Restart)]
    [InlineData("SHUT DOWN", NexusCommand.ShutDown)]
    [InlineData("  sleep ", NexusCommand.Sleep)]
    [InlineData("設定", NexusCommand.Settings)]
    public void Commands_are_asked_for_in_english_or_japanese(string query, NexusCommand expected)
    {
        var match = Assert.Single(NexusIntents.Match(query), match => match.Exact);

        Assert.Equal(expected, match.Command);
    }

    [Fact]
    public void The_date_is_today_in_2088_with_its_weekday_in_japanese()
    {
        var tuesday = new DateTime(2026, 10, 13, 20, 21, 0);

        Assert.Equal("2088.10.13", Clock2088.Date(tuesday));
        Assert.Equal("火", Clock2088.Weekday(tuesday));
        Assert.Equal("日", Clock2088.Weekday(new DateTime(2026, 10, 18)));
    }

    [Fact]
    public void Uptime_reads_days_then_the_time()
    {
        Assert.Equal("2D 04:05:09", Clock2088.Uptime(new TimeSpan(2, 4, 5, 9)));
        Assert.Equal("0D 00:00:00", Clock2088.Uptime(TimeSpan.Zero));
    }
}
