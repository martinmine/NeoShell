using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NeoShell.Widgets;

/// <param name="Symbol">MET's weather symbol for the hour that starts then, like "partlycloudy_day".</param>
public sealed record WeatherHour(DateTimeOffset Time, double Temperature, string? Symbol);

/// <param name="WindSpeed">Metres a second.</param>
public sealed record WeatherForecast(WeatherHour Now, double WindSpeed, IReadOnlyList<WeatherHour> Hours);

/// <summary>
/// The forecast from the Norwegian Meteorological Institute (api.met.no, Locationforecast 2.0). Its terms ask for a
/// User-Agent that names the app and how to reach its makers, coordinates of at most four decimals, and the cache
/// rules it sends (Expires, If-Modified-Since); a forecast is reused until it expires.
/// </summary>
public static class MetWeather
{
    /// <summary>How many hours after the current one the widget shows.</summary>
    public const int HourCount = 5;

    private static readonly HttpClient s_http = CreateClient();
    private static Cached? s_cache;

    private sealed record Cached(string Url, DateTimeOffset Expires, DateTimeOffset? LastModified, string Json);

    /// <exception cref="HttpRequestException">No forecast could be fetched.</exception>
    public static async Task<WeatherForecast?> GetAsync(double latitude, double longitude, CancellationToken cancel)
    {
        string url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.met.no/weatherapi/locationforecast/2.0/compact?lat={Math.Round(latitude, 4)}&lon={Math.Round(longitude, 4)}");
        Cached? cache = s_cache?.Url == url ? s_cache : null;
        if (cache is not null && DateTimeOffset.UtcNow < cache.Expires)
            return Parse(cache.Json, DateTimeOffset.UtcNow);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cache?.LastModified is { } lastModified)
            request.Headers.IfModifiedSince = lastModified;
        using HttpResponseMessage response = await s_http.SendAsync(request, cancel);
        string json;
        if (response.StatusCode == HttpStatusCode.NotModified && cache is not null)
        {
            json = cache.Json;
        }
        else
        {
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(cancel);
        }
        s_cache = new Cached(
            url,
            response.Content.Headers.Expires ?? DateTimeOffset.UtcNow.AddMinutes(30),
            response.Content.Headers.LastModified ?? cache?.LastModified,
            json);
        return Parse(json, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The hour under way at <paramref name="now"/> and the next <see cref="HourCount"/>; null when the answer has no
    /// forecast for then.
    /// </summary>
    public static WeatherForecast? Parse(string json, DateTimeOffset now)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("properties", out JsonElement properties)
            || !properties.TryGetProperty("timeseries", out JsonElement series))
        {
            return null;
        }

        var hours = new List<WeatherHour>();
        double wind = 0;
        foreach (JsonElement entry in series.EnumerateArray())
        {
            DateTimeOffset time = entry.GetProperty("time").GetDateTimeOffset();
            // The hour under way started up to an hour ago.
            if (time <= now.AddHours(-1))
                continue;

            JsonElement data = entry.GetProperty("data");
            JsonElement details = data.GetProperty("instant").GetProperty("details");
            if (hours.Count == 0 && details.TryGetProperty("wind_speed", out JsonElement windSpeed))
                wind = windSpeed.GetDouble();
            hours.Add(new WeatherHour(time, details.GetProperty("air_temperature").GetDouble(), Symbol(data)));
            if (hours.Count == HourCount + 1)
                break;
        }
        return hours.Count == 0 ? null : new WeatherForecast(hours[0], wind, hours[1..]);
    }

    /// <summary>A picture of the weather for MET's symbol code.</summary>
    public static string Emoji(string? symbol)
    {
        if (symbol is null)
            return "\U0001F321"; // thermometer
        bool night = symbol.EndsWith("_night", StringComparison.Ordinal);
        string weather = Base(symbol);
        return weather.Contains("thunder", StringComparison.Ordinal) ? "\u26C8"
            : weather.Contains("snow", StringComparison.Ordinal) || weather.Contains("sleet", StringComparison.Ordinal) ? "\U0001F328"
            : weather.Contains("showers", StringComparison.Ordinal) && !night ? "\U0001F326"
            : weather.Contains("rain", StringComparison.Ordinal) ? "\U0001F327"
            : weather == "fog" ? "\U0001F32B"
            : weather == "cloudy" ? "\u2601"
            : weather == "partlycloudy" ? (night ? "\u2601" : "\u26C5")
            : weather == "fair" ? (night ? "\U0001F319" : "\U0001F324")
            : weather == "clearsky" ? (night ? "\U0001F319" : "\u2600")
            : "\U0001F321";
    }

    /// <summary>MET's symbol code in words: "lightrainshowers_day" is "Light rain showers".</summary>
    public static string Describe(string? symbol)
    {
        if (symbol is null)
            return "";

        // The codes run words together; "lightssleet" is MET's own spelling.
        string[] words = ["lights", "light", "heavy", "partly", "cloudy", "clear", "sky", "fair", "fog", "rain", "sleet", "snow", "showers", "and", "thunder"];
        string weather = Base(symbol);
        var text = new StringBuilder();
        for (int i = 0; i < weather.Length;)
        {
            string? word = words.FirstOrDefault(w => string.CompareOrdinal(weather, i, w, 0, w.Length) == 0);
            if (word is null)
                return Capitalize(weather);
            if (text.Length > 0)
                text.Append(' ');
            text.Append(word == "lights" ? "light" : word);
            i += word.Length;
        }
        return Capitalize(text.ToString());
    }

    private static string Base(string symbol) => symbol.Split('_')[0];

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // The hour's symbol; further out the forecast only has one for six or twelve hours.
    private static string? Symbol(JsonElement data)
    {
        foreach (string period in (string[])["next_1_hours", "next_6_hours", "next_12_hours"])
        {
            if (data.TryGetProperty(period, out JsonElement next)
                && next.TryGetProperty("summary", out JsonElement summary)
                && summary.TryGetProperty("symbol_code", out JsonElement code))
            {
                return code.GetString();
            }
        }
        return null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NeoShell/1.0 (+https://github.com/martinmine/NeoShell)");
        return client;
    }
}
