using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>
/// Vista's stocks gadget: a list of stocks and indexes with their price and the day's change, each expanding to the
/// day's chart. Refreshed every five minutes while shown.
/// </summary>
internal sealed partial class StocksWidget : WidgetView
{
    private static readonly TimeSpan s_refresh = TimeSpan.FromMinutes(5);

    private readonly DispatcherQueueTimer _timer;
    // Waits for typing in the settings to stop before fetching the new symbols.
    private readonly DispatcherQueueTimer _typing;
    private CancellationTokenSource? _fetch;

    public StocksWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => Refresh();
        _typing = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _typing.Interval = TimeSpan.FromSeconds(1);
        _typing.IsRepeating = false;
        _typing.Tick += (_, _) => Refresh();
        BuildRows();
        Refresh();
    }

    public override bool ContentUnderButtons => true;

    private IReadOnlyList<string> Symbols => Settings.StockSymbols ?? StockQuotes.DefaultSymbols;

    public override void Close()
    {
        _timer.Stop();
        _typing.Stop();
        _fetch?.Cancel();
    }

    public override FrameworkElement CreateSettings()
    {
        var symbols = new TextBox
        {
            Header = "Symbols",
            Text = string.Join(", ", Symbols),
            PlaceholderText = "MSFT, ^DJI, EQNR.OL",
            TextWrapping = TextWrapping.Wrap,
        };
        symbols.TextChanged += (_, _) =>
        {
            IReadOnlyList<string> parsed = StockQuotes.ParseSymbols(symbols.Text);
            if (parsed.SequenceEqual(Symbols))
                return;
            SaveSettings(Settings with { StockSymbols = parsed });
            BuildRows();
            _typing.Stop();
            _typing.Start();
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(symbols);
        panel.Children.Add(new TextBlock
        {
            Text = "Separated by commas, as Yahoo Finance writes them: an exchange's suffix after a dot (EQNR.OL is Oslo), "
                + "indexes after a ^. Quotes from Yahoo Finance, delayed.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        return panel;
    }

    // One row a symbol, kept for a symbol still listed so it stays expanded.
    private void BuildRows()
    {
        var rows = Rows.Children.Cast<StockRow>().ToDictionary(row => row.Symbol);
        Rows.Children.Clear();
        foreach (string symbol in Symbols)
            Rows.Children.Add(rows.GetValueOrDefault(symbol) ?? new StockRow(symbol));
        if (Symbols.Count == 0)
            StatusText.Text = "Add stocks in this widget's settings.";
    }

    private async void Refresh()
    {
        _fetch?.Cancel();
        var fetch = _fetch = new CancellationTokenSource();
        _timer.Stop();
        StockRow[] rows = [.. Rows.Children.Cast<StockRow>()];
        if (rows.Length == 0)
            return;

        var quotes = await Task.WhenAll(rows.Select(row => GetAsync(row.Symbol, fetch.Token)));
        if (fetch.IsCancellationRequested)
            return;

        bool failed = false;
        for (int i = 0; i < rows.Length; i++)
        {
            switch (quotes[i])
            {
                case (true, { } quote):
                    rows[i].Show(quote);
                    break;
                case (true, null):
                    rows[i].ShowNotFound();
                    break;
                default:
                    // Keeps what it showed.
                    failed = true;
                    break;
            }
        }
        StatusText.Text = failed
            ? "Some quotes couldn't be fetched. They're tried again in a few minutes."
            : "Updated " + DateTime.Now.ToString("t", CultureInfo.CurrentCulture) + " · Delayed quotes";
        _timer.Interval = s_refresh;
        _timer.Start();
    }

    // Whether it was fetched (a symbol Yahoo doesn't know is fetched, with no quote), and the quote.
    private static async Task<(bool Fetched, StockQuote? Quote)> GetAsync(string symbol, CancellationToken cancel)
    {
        try
        {
            return (true, await StockQuotes.GetAsync(symbol, cancel));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            if (!cancel.IsCancellationRequested)
                Log.Warn($"Could not get the quote for {symbol}", ex);
            return (false, null);
        }
    }
}
