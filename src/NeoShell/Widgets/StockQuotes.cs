using System.Globalization;
using System.Net;
using System.Text.Json;

namespace NeoShell.Widgets;

/// <param name="Day">The prices through the trading day so far (every five minutes), for its chart.</param>
public sealed record StockQuote(
    string Symbol,
    string Name,
    double Price,
    double PreviousClose,
    string? Currency,
    DateTimeOffset Time,
    IReadOnlyList<double> Day)
{
    public double Change => Price - PreviousClose;

    public double ChangePercent => PreviousClose == 0 ? 0 : Change / PreviousClose * 100;
}

/// <summary>
/// Stock quotes from Yahoo Finance's chart service (query1.finance.yahoo.com/v8/finance/chart), which answers without
/// a key; MSN Money, which Vista's gadget used, is gone. Quotes are delayed as the exchanges require.
/// </summary>
public static class StockQuotes
{
    /// <summary>A new widget's symbols: the Dow Jones and NASDAQ indexes, and Microsoft.</summary>
    public static readonly IReadOnlyList<string> DefaultSymbols = ["^DJI", "^IXIC", "MSFT"];

    /// <summary>Enough for a sidebar; each symbol is a request of its own.</summary>
    public const int MaxSymbols = 20;

    private static readonly HttpClient s_http = CreateClient();

    /// <summary>The symbol's quote; null when Yahoo doesn't know it.</summary>
    /// <exception cref="HttpRequestException">The quote couldn't be fetched.</exception>
    public static async Task<StockQuote?> GetAsync(string symbol, CancellationToken cancel)
    {
        string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=5m";
        using HttpResponseMessage response = await s_http.GetAsync(url, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancel));
    }

    /// <summary>The quote in a chart answer; null when it has none (an unknown symbol).</summary>
    public static StockQuote? Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("chart", out JsonElement chart)
            || !chart.TryGetProperty("result", out JsonElement results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement result = results[0];
        JsonElement meta = result.GetProperty("meta");
        if (!meta.TryGetProperty("regularMarketPrice", out JsonElement price))
            return null;
        string symbol = meta.GetProperty("symbol").GetString() ?? "";
        double previous = Number(meta, "chartPreviousClose") ?? Number(meta, "previousClose") ?? price.GetDouble();

        var day = new List<double>();
        if (result.TryGetProperty("indicators", out JsonElement indicators)
            && indicators.TryGetProperty("quote", out JsonElement quotes)
            && quotes.GetArrayLength() > 0
            && quotes[0].TryGetProperty("close", out JsonElement closes))
        {
            // Five minutes without a trade have no price.
            foreach (JsonElement close in closes.EnumerateArray())
            {
                if (close.ValueKind == JsonValueKind.Number)
                    day.Add(close.GetDouble());
            }
        }

        return new StockQuote(
            symbol,
            Text(meta, "shortName") ?? Text(meta, "longName") ?? symbol,
            price.GetDouble(),
            previous,
            Text(meta, "currency"),
            DateTimeOffset.FromUnixTimeSeconds(meta.TryGetProperty("regularMarketTime", out JsonElement time) ? time.GetInt64() : 0),
            day);
    }

    /// <summary>
    /// The symbols typed into the settings, separated by commas, semicolons or spaces: in capitals, each once, at most
    /// <see cref="MaxSymbols"/>.
    /// </summary>
    public static IReadOnlyList<string> ParseSymbols(string text) =>
        [.. text.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(symbol => symbol.ToUpperInvariant())
            .Distinct()
            .Take(MaxSymbols)];

    /// <summary>A price as the exchange gives it: two decimals, grouped.</summary>
    public static string FormatPrice(double price) => price.ToString("#,0.00", CultureInfo.CurrentCulture);

    /// <summary>The change since the last close: "+1.25 (+0.24%)", "−12.61 (−2.34%)".</summary>
    public static string FormatChange(StockQuote quote)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        string sign = quote.Change > 0 ? "+" : quote.Change < 0 ? "−" : "";
        return string.Format(culture, "{0}{1:#,0.00} ({0}{2:0.00}%)", sign, Math.Abs(quote.Change), Math.Abs(quote.ChangePercent));
    }

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NeoShell/1.0 (+https://github.com/martinmine/NeoShell)");
        return client;
    }
}
