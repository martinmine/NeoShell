using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Logging;
using NeoShell.Settings;
using Windows.Globalization.NumberFormatting;

namespace NeoShell.Widgets;

/// <summary>
/// Vista's currency converter: an amount in one currency and what it is in another, either typed into, at the
/// European Central Bank's rates of the day.
/// </summary>
internal sealed partial class CurrencyWidget : WidgetView
{
    private static readonly TimeSpan s_refresh = TimeSpan.FromHours(6);
    private static readonly TimeSpan s_retry = TimeSpan.FromMinutes(5);

    private readonly DispatcherQueueTimer _timer;
    private RateTable? _rates;
    // Set while the widget fills its own boxes, so that doesn't count as typing.
    private bool _filling;

    public CurrencyWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        FromAmount.NumberFormatter = AmountFormatter();
        ToAmount.NumberFormatter = AmountFormatter();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => Refresh();
        Refresh();
    }

    public override bool ContentUnderButtons => true;

    public override void Close() => _timer.Stop();

    public override FrameworkElement CreateSettings() => new TextBlock
    {
        Text = "Rates from the European Central Bank, through Frankfurter (frankfurter.dev). The bank sets them once a working day.",
        TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
    };

    private async void Refresh()
    {
        try
        {
            RateTable rates = await ExchangeRates.GetAsync();
            bool first = _rates is null;
            _rates = rates;
            if (first)
                FillCurrencies();
            else
                Convert(fromFirst: true);
            ShowRate();
            Converter.Visibility = Footer.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Collapsed;
            Schedule(s_refresh);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or FormatException)
        {
            Log.Warn("Could not get the exchange rates", ex);
            // Rates already shown stay: they are a day old at most.
            if (_rates is null)
                StatusText.Text = "The exchange rates couldn't be fetched. They're tried again in a few minutes.";
            Schedule(s_retry);
        }
    }

    private void Schedule(TimeSpan after)
    {
        _timer.Interval = after;
        _timer.Start();
    }

    private void FillCurrencies()
    {
        RateTable rates = _rates!;
        (string defaultFrom, string defaultTo) = ExchangeRates.Defaults(LocalCurrency(), rates.PerEuro);
        string from = Settings.CurrencyFrom is { } savedFrom && rates.PerEuro.ContainsKey(savedFrom) ? savedFrom : defaultFrom;
        string to = Settings.CurrencyTo is { } savedTo && rates.PerEuro.ContainsKey(savedTo) ? savedTo : defaultTo;

        _filling = true;
        foreach (string code in rates.PerEuro.Keys.Order(StringComparer.Ordinal))
        {
            FromCurrency.Items.Add(Item(code));
            ToCurrency.Items.Add(Item(code));
        }
        Select(FromCurrency, from);
        Select(ToCurrency, to);
        FromAmount.Value = Settings.CurrencyAmount ?? 1;
        _filling = false;
        Convert(fromFirst: true);
    }

    private static ComboBoxItem Item(string code)
    {
        var item = new ComboBoxItem { Content = code, Tag = code };
        ToolTipService.SetToolTip(item, ExchangeRates.Name(code));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, $"{code}, {ExchangeRates.Name(code)}");
        return item;
    }

    private static void Select(ComboBox box, string code) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == code);

    private static string Code(ComboBox box) => (string)((ComboBoxItem)box.SelectedItem).Tag;

    private static string? LocalCurrency()
    {
        try
        {
            return RegionInfo.CurrentRegion.ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Fills the other box from the one typed into (or, for a change of currency, the second from the first).
    private void Convert(bool fromFirst)
    {
        if (_rates is null || FromCurrency.SelectedItem is null || ToCurrency.SelectedItem is null)
            return;

        NumberBox source = fromFirst ? FromAmount : ToAmount;
        NumberBox target = fromFirst ? ToAmount : FromAmount;
        string from = Code(fromFirst ? FromCurrency : ToCurrency);
        string to = Code(fromFirst ? ToCurrency : FromCurrency);
        _filling = true;
        target.Value = double.IsNaN(source.Value) ? double.NaN : Math.Round(_rates.Convert(source.Value, from, to), 2);
        _filling = false;
        ShowRate();
    }

    private void ShowRate()
    {
        if (_rates is null || FromCurrency.SelectedItem is null || ToCurrency.SelectedItem is null)
            return;

        CultureInfo culture = CultureInfo.CurrentCulture;
        string from = Code(FromCurrency), to = Code(ToCurrency);
        RateText.Text = string.Format(culture, "1 {0} = {1:#,0.####} {2}", from, _rates.Convert(1, from, to), to);
        DateText.Text = "Rates of " + _rates.Date.ToString("D", culture);
        ToolTipService.SetToolTip(FromCurrency, ExchangeRates.Name(from));
        ToolTipService.SetToolTip(ToCurrency, ExchangeRates.Name(to));
    }

    private void Save()
    {
        if (FromCurrency.SelectedItem is null || ToCurrency.SelectedItem is null)
            return;
        SaveSettings(Settings with
        {
            CurrencyFrom = Code(FromCurrency),
            CurrencyTo = Code(ToCurrency),
            CurrencyAmount = double.IsNaN(FromAmount.Value) ? null : FromAmount.Value,
        });
    }

    private void FromAmount_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_filling)
            return;
        Convert(fromFirst: true);
        Save();
    }

    private void ToAmount_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_filling)
            return;
        Convert(fromFirst: false);
        Save();
    }

    private void Currency_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling)
            return;
        Convert(fromFirst: true);
        Save();
    }

    private void SwapButton_Click(object sender, RoutedEventArgs e)
    {
        if (FromCurrency.SelectedItem is null || ToCurrency.SelectedItem is null)
            return;
        string from = Code(FromCurrency), to = Code(ToCurrency);
        _filling = true;
        Select(FromCurrency, to);
        Select(ToCurrency, from);
        _filling = false;
        Convert(fromFirst: true);
        Save();
    }

    // Two decimals, grouped: "1,250.00".
    private static DecimalFormatter AmountFormatter() => new()
    {
        IntegerDigits = 1,
        FractionDigits = 2,
        IsGrouped = true,
        NumberRounder = new IncrementNumberRounder { Increment = 0.01, RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp },
    };
}
