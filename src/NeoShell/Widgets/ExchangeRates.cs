using System.Globalization;
using System.Text.Json;

namespace NeoShell.Widgets;

/// <param name="Date">The working day the European Central Bank set the rates.</param>
/// <param name="PerEuro">What one euro buys of each currency, the euro's own (1) among them.</param>
public sealed record RateTable(DateOnly Date, IReadOnlyDictionary<string, double> PerEuro)
{
    /// <summary>An amount of one currency in another, through the euro: the bank sets every rate against it.</summary>
    public double Convert(double amount, string from, string to) => amount / PerEuro[from] * PerEuro[to];
}

/// <summary>
/// The European Central Bank's reference rates, through Frankfurter (api.frankfurter.dev, free and without a key).
/// The bank sets them once a working day, around 16:00 CET, so one fetch serves every currency widget for an hour.
/// </summary>
public static class ExchangeRates
{
    private static readonly TimeSpan s_reuse = TimeSpan.FromHours(1);
    private static readonly HttpClient s_http = CreateClient();
    private static readonly Lazy<Dictionary<string, string>> s_names = new(ReadNames);
    private static Task<RateTable>? s_fetch;
    private static DateTime s_fetched;

    /// <exception cref="HttpRequestException">The rates couldn't be fetched.</exception>
    public static Task<RateTable> GetAsync()
    {
        // A failed fetch isn't kept: the next widget, or the next try, asks again.
        if (s_fetch is null || s_fetch.IsFaulted || s_fetch.IsCanceled || DateTime.UtcNow - s_fetched > s_reuse)
        {
            s_fetched = DateTime.UtcNow;
            s_fetch = FetchAsync();
        }
        return s_fetch;
    }

    private static async Task<RateTable> FetchAsync() =>
        Parse(await s_http.GetStringAsync("https://api.frankfurter.dev/v1/latest"));

    public static RateTable Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string baseCurrency = root.GetProperty("base").GetString() ?? "EUR";
        var perEuro = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [baseCurrency] = 1 };
        foreach (JsonProperty rate in root.GetProperty("rates").EnumerateObject())
            perEuro[rate.Name] = rate.Value.GetDouble();
        DateOnly date = DateOnly.ParseExact(root.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new RateTable(date, perEuro);
    }

    /// <summary>
    /// The two currencies a new widget converts between: the US dollar to the computer's own, or to the euro where
    /// the dollar is the computer's own (or its own isn't one the bank sets).
    /// </summary>
    public static (string From, string To) Defaults(string? local, IReadOnlyDictionary<string, double> perEuro) =>
        local is not null && local != "USD" && perEuro.ContainsKey(local) ? ("USD", local) : ("USD", "EUR");

    /// <summary>The currency's name in English, "Norwegian Krone"; the code itself for one Windows doesn't name.</summary>
    public static string Name(string code) => s_names.Value.GetValueOrDefault(code, code);

    // Windows names each country's currency; several countries share some (the euro).
    private static Dictionary<string, string> ReadNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                names.TryAdd(region.ISOCurrencySymbol, region.CurrencyEnglishName);
            }
            catch (ArgumentException)
            {
                // A culture without a region of its own.
            }
        }
        return names;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NeoShell/1.0 (+https://github.com/martinmine/NeoShell)");
        return client;
    }
}
