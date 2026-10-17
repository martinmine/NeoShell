using System.Globalization;
using NeoShell.Widgets;

namespace NeoShell.Tests;

/// <summary>The widgets brought over from Vista's gadgets: clock, calendar, picture puzzle, currency and stocks.</summary>
public sealed class VistaWidgetTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(3, 0, 0, 90, 0, 0)]
    [InlineData(15, 30, 0, 105, 180, 0)]       // the hour hand half way from three to four
    [InlineData(9, 45, 30, 292.75, 273, 180)]
    public void Clock_hands_point_at_the_time(int hours, int minutes, int seconds, double hour, double minute, double second)
    {
        (double Hour, double Minute, double Second) angles = ClockFace.Angles(new TimeSpan(hours, minutes, seconds));

        Assert.Equal(hour, angles.Hour, 3);
        Assert.Equal(minute, angles.Minute, 3);
        Assert.Equal(second, angles.Second, 3);
    }

    [Theory]
    [InlineData("2026-10-17 12:00", "2026-10-17 18:00", "Today, 6 hours ahead")]
    [InlineData("2026-10-17 01:00", "2026-10-16 16:00", "Yesterday, 9 hours behind")]
    [InlineData("2026-10-17 22:00", "2026-10-18 03:30", "Tomorrow, 5:30 hours ahead")]
    [InlineData("2026-10-17 12:00", "2026-10-17 13:00", "Today, 1 hour ahead")]
    [InlineData("2026-10-17 12:00", "2026-10-17 12:00", "Same time as here")]
    public void Clock_in_another_zone_says_how_it_differs_from_here(string here, string there, string expected)
    {
        Assert.Equal(expected, ClockFace.Difference(DateTime.Parse(there, CultureInfo.InvariantCulture), DateTime.Parse(here, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Clock_shows_the_time_in_its_zone_and_the_computers_without_one()
    {
        var utc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 1, 15, 21, 0, 0), ClockFace.TimeIn("Tokyo Standard Time", utc));
        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local), ClockFace.TimeIn(null, utc));
        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local), ClockFace.TimeIn("No Such Zone", utc));
    }

    [Fact]
    public void Calendar_month_is_six_weeks_from_the_first_day_of_the_week()
    {
        // October 2026 starts on a Thursday.
        CalendarDay[] days = CalendarMonth.Days(new DateOnly(2026, 10, 17), DayOfWeek.Monday, new DateOnly(2026, 10, 17));

        Assert.Equal(42, days.Length);
        Assert.Equal(new DateOnly(2026, 9, 28), days[0].Date);
        Assert.False(days[0].InMonth);
        Assert.Equal(new DateOnly(2026, 10, 1), days[3].Date);
        Assert.True(days[3].InMonth);
        Assert.Equal(new DateOnly(2026, 11, 8), days[^1].Date);
        Assert.Single(days, day => day.IsToday);
        Assert.Equal(new DateOnly(2026, 10, 17), days.Single(day => day.IsToday).Date);
    }

    [Fact]
    public void Calendar_month_starting_on_the_first_day_of_the_week_starts_with_the_1st()
    {
        // February 2026 starts on a Sunday.
        CalendarDay[] days = CalendarMonth.Days(new DateOnly(2026, 2, 1), DayOfWeek.Sunday, new DateOnly(2026, 10, 17));

        Assert.Equal(new DateOnly(2026, 2, 1), days[0].Date);
        Assert.DoesNotContain(days, day => day.IsToday);
    }

    [Fact]
    public void Calendar_weekdays_start_on_the_first_day_of_the_week()
    {
        var format = (DateTimeFormatInfo)CultureInfo.GetCultureInfo("en-US").DateTimeFormat.Clone();
        format.FirstDayOfWeek = DayOfWeek.Monday;

        Assert.Equal(["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"], CalendarMonth.WeekdayNames(format));
    }

    [Fact]
    public void Puzzle_starts_solved_with_the_gap_last()
    {
        var puzzle = new SlidingPuzzle();

        Assert.True(puzzle.IsSolved);
        Assert.Equal(15, puzzle.Gap);
        Assert.Equal(0, puzzle.PlaceOf(1));
    }

    [Fact]
    public void Puzzle_tile_beside_the_gap_slides_into_it()
    {
        var puzzle = new SlidingPuzzle();

        Assert.True(puzzle.CanMove(12));   // above the gap
        Assert.True(puzzle.CanMove(15));   // left of it
        Assert.False(puzzle.CanMove(11));  // diagonal
        Assert.False(puzzle.CanMove(1));

        Assert.True(puzzle.Move(15));
        Assert.Equal(15, puzzle.PlaceOf(15));
        Assert.Equal(14, puzzle.Gap);
        Assert.False(puzzle.IsSolved);
        Assert.False(puzzle.Move(1));
    }

    [Fact]
    public void Puzzle_tile_at_a_rows_end_does_not_slide_to_the_next_row()
    {
        // The gap at the start of the second row; tile 4 ends the first.
        var puzzle = new SlidingPuzzle([1, 2, 3, 4, 0, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]);

        Assert.False(puzzle.CanMove(4));
        Assert.True(puzzle.CanMove(1));
        Assert.True(puzzle.CanMove(5));
    }

    [Fact]
    public void Puzzle_is_shuffled_by_sliding_so_it_can_be_solved()
    {
        var puzzle = new SlidingPuzzle();
        puzzle.Shuffle(new Random(7));

        Assert.False(puzzle.IsSolved);
        // Solvable on a board four wide: inversions plus the gap's row from the bottom (counting from 1) is odd.
        int[] tiles = [.. Enumerable.Range(0, 16).Select(place => Enumerable.Range(1, 15).FirstOrDefault(tile => puzzle.PlaceOf(tile) == place)).Where(tile => tile != 0)];
        int inversions = tiles.Select((tile, i) => tiles.Skip(i + 1).Count(later => later < tile)).Sum();
        int gapRowFromBottom = 4 - puzzle.Gap / 4;
        Assert.Equal(1, (inversions + gapRowFromBottom) % 2);
    }

    [Fact]
    public void Puzzle_board_needs_every_tile_once()
    {
        Assert.Throws<ArgumentException>(() => new SlidingPuzzle([1, 1, 3, 4, 0, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]));
        Assert.Throws<ArgumentException>(() => new SlidingPuzzle([0, 1, 2]));
    }

    private const string Rates = """
        {"amount":1.0,"base":"EUR","date":"2026-10-16","rates":{"NOK":10.624,"SEK":11.145,"USD":1.1253}}
        """;

    [Fact]
    public void Exchange_rates_are_read_against_the_euro()
    {
        RateTable rates = ExchangeRates.Parse(Rates);

        Assert.Equal(new DateOnly(2026, 10, 16), rates.Date);
        Assert.Equal(1, rates.PerEuro["EUR"]);
        Assert.Equal(10.624, rates.PerEuro["NOK"]);
        Assert.Equal(4, rates.PerEuro.Count);
    }

    [Fact]
    public void Currency_is_converted_through_the_euro()
    {
        RateTable rates = ExchangeRates.Parse(Rates);

        Assert.Equal(1062.4, rates.Convert(100, "EUR", "NOK"), 6);
        Assert.Equal(100 / 1.1253 * 10.624, rates.Convert(100, "USD", "NOK"), 6);
        Assert.Equal(100, rates.Convert(rates.Convert(100, "SEK", "USD"), "USD", "SEK"), 6);
    }

    [Theory]
    [InlineData("NOK", "USD", "NOK")]
    [InlineData("USD", "USD", "EUR")]
    [InlineData("XAF", "USD", "EUR")]  // not one the bank sets
    [InlineData(null, "USD", "EUR")]
    public void Currency_widget_starts_from_dollars_to_the_computers_currency(string? local, string from, string to)
    {
        Assert.Equal((from, to), ExchangeRates.Defaults(local, ExchangeRates.Parse(Rates).PerEuro));
    }

    [Fact]
    public void Currency_has_its_english_name()
    {
        Assert.Equal("Norwegian Krone", ExchangeRates.Name("NOK"));
        Assert.Equal("XYZ", ExchangeRates.Name("XYZ"));
    }

    private const string Chart = """
        {"chart":{"result":[{"meta":{"currency":"USD","symbol":"^DJI","regularMarketTime":1792183329,
        "regularMarketPrice":52002.41,"shortName":"Dow Jones Industrial Average","chartPreviousClose":52177.7,
        "previousClose":52177.7},"timestamp":[1,2,3,4],
        "indicators":{"quote":[{"close":[52474.75,null,52369.8,52002.41],"open":[1,2,3,4]}]}}],"error":null}}
        """;

    [Fact]
    public void Stock_quote_is_read_from_a_chart()
    {
        StockQuote quote = StockQuotes.Parse(Chart)!;

        Assert.Equal("^DJI", quote.Symbol);
        Assert.Equal("Dow Jones Industrial Average", quote.Name);
        Assert.Equal(52002.41, quote.Price);
        Assert.Equal(52177.7, quote.PreviousClose);
        Assert.Equal("USD", quote.Currency);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792183329), quote.Time);
        Assert.Equal([52474.75, 52369.8, 52002.41], quote.Day);  // the five minutes without a trade left out
        Assert.Equal(-175.29, quote.Change, 2);
        Assert.Equal(-0.336, quote.ChangePercent, 3);
    }

    [Fact]
    public void Unknown_stock_has_no_quote()
    {
        const string notFound = """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found, symbol may be delisted"}}}""";

        Assert.Null(StockQuotes.Parse(notFound));
    }

    [Fact]
    public void Stock_change_is_written_with_its_sign()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            StockQuote quote = StockQuotes.Parse(Chart)!;
            Assert.Equal("52,002.41", StockQuotes.FormatPrice(quote.Price));
            Assert.Equal("−175.29 (−0.34%)", StockQuotes.FormatChange(quote));
            Assert.Equal("+1.25 (+0.25%)", StockQuotes.FormatChange(quote with { Price = 501.25, PreviousClose = 500 }));
            Assert.Equal("0.00 (0.00%)", StockQuotes.FormatChange(quote with { Price = 500, PreviousClose = 500 }));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Stock_symbols_are_read_in_capitals_each_once()
    {
        Assert.Equal(["MSFT", "^DJI", "EQNR.OL"], StockQuotes.ParseSymbols(" msft, ^dji;EQNR.OL  Msft,,"));
        Assert.Empty(StockQuotes.ParseSymbols(" , "));
        Assert.Equal(StockQuotes.MaxSymbols, StockQuotes.ParseSymbols(string.Join(",", Enumerable.Range(0, 30).Select(i => $"S{i}"))).Count);
    }
}
