using NeoShell.Desktop;

namespace NeoShell.Tests;

public sealed class SlideshowTests
{
    private static readonly string[] Pictures = [@"C:\P\a.jpg", @"C:\P\b.jpg", @"C:\P\c.jpg", @"C:\P\d.jpg"];
    private static readonly DateTime Midnight = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void In_turn_the_pictures_follow_the_current_one_and_wrap_to_the_start()
    {
        var queue = new SlideshowQueue(new Random(1));

        Assert.Equal(@"C:\P\c.jpg", queue.Next(Pictures, @"C:\P\b.jpg", shuffle: false));
        Assert.Equal(@"C:\P\d.jpg", queue.Next(Pictures, @"C:\P\c.jpg", shuffle: false));
        Assert.Equal(@"C:\P\a.jpg", queue.Next(Pictures, @"C:\P\d.jpg", shuffle: false));
        Assert.Equal(@"C:\P\b.jpg", queue.Next(Pictures, @"C:\P\a.jpg", shuffle: false));
    }

    [Fact]
    public void A_current_picture_from_elsewhere_starts_with_the_first()
    {
        var queue = new SlideshowQueue(new Random(1));

        Assert.Equal(@"C:\P\a.jpg", queue.Next(Pictures, @"C:\Other\x.jpg", shuffle: false));
        Assert.Equal(@"C:\P\b.jpg", queue.Next(Pictures, @"C:\P\a.jpg", shuffle: false));
    }

    [Fact]
    public void A_shuffled_round_shows_every_picture_once_and_never_the_current_one_first()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var queue = new SlideshowQueue(new Random(seed));
            string current = @"C:\P\b.jpg";
            var round = new List<string>();
            for (int i = 0; i < Pictures.Length; i++)
            {
                current = queue.Next(Pictures, current, shuffle: true)!;
                round.Add(current);
            }

            Assert.NotEqual(@"C:\P\b.jpg", round[0]);
            Assert.Equal(Pictures.Order(), round.Order());
        }
    }

    [Fact]
    public void A_lone_picture_that_is_already_shown_gives_nothing_new()
    {
        Assert.Null(new SlideshowQueue(new Random(1)).Next([@"C:\P\a.jpg"], @"C:\P\a.jpg", shuffle: false));
        Assert.Null(new SlideshowQueue(new Random(1)).Next([], null, shuffle: true));
    }

    [Fact]
    public void Pictures_that_went_away_are_dropped_from_the_round()
    {
        var queue = new SlideshowQueue(new Random(1));
        Assert.Equal(@"C:\P\b.jpg", queue.Next(Pictures, @"C:\P\a.jpg", shuffle: false));

        Assert.Equal(@"C:\P\d.jpg", queue.Next([@"C:\P\a.jpg", @"C:\P\b.jpg", @"C:\P\d.jpg"], @"C:\P\b.jpg", shuffle: false));
    }

    [Theory]
    // One minute, at 00:10:20.000: the next whole minute since midnight is 40 s away.
    [InlineData(60_000, 620_000, 40_000)]
    // At 00:10:57.000 it's only 3 s away, so Explorer waits for the one after.
    [InlineData(60_000, 657_000, 63_000)]
    // Exactly on a boundary: a whole interval.
    [InlineData(60_000, 600_000, 60_000)]
    // Below the 10 s minimum, the interval is 10 s.
    [InlineData(1_000, 605_000, 5_000)]
    public void Short_intervals_fall_on_whole_intervals_since_midnight(uint interval, int sinceMidnight, uint expected)
    {
        Assert.Equal(expected, SlideshowSettings.NextChange(interval, Midnight.AddMilliseconds(sinceMidnight), null, alignToMidnight: true));
    }

    [Fact]
    public void Without_alignment_the_whole_interval_is_waited()
    {
        Assert.Equal(60_000u, SlideshowSettings.NextChange(60_000, Midnight.AddMilliseconds(620_000), null, alignToMidnight: false));
    }

    [Fact]
    public void Long_intervals_count_from_the_last_change()
    {
        const uint thirtyMinutes = 1_800_000;
        DateTime now = Midnight.AddMinutes(50);

        // Changed 20 minutes ago: 10 minutes left, which land on the 01:00 boundary.
        Assert.Equal(600_000u, SlideshowSettings.NextChange(thirtyMinutes, now, now.AddMinutes(-20), alignToMidnight: true));
        // Overdue, or never changed: straight away.
        Assert.Equal(10u, SlideshowSettings.NextChange(thirtyMinutes, now, now.AddMinutes(-31), alignToMidnight: true));
        Assert.Equal(10u, SlideshowSettings.NextChange(thirtyMinutes, now, null, alignToMidnight: true));
    }

    [Theory]
    [InlineData(null, 1000)]
    [InlineData(400, 400)]
    [InlineData(249, 250)]
    [InlineData(0, 250)]
    public void Animation_duration_defaults_to_a_second_and_is_at_least_250_ms(int? value, int expected)
    {
        Assert.Equal(expected, SlideshowSettings.ClampAnimationDuration(value));
    }
}
