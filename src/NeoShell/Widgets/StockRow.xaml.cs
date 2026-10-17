using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace NeoShell.Widgets;

internal sealed partial class StockRow : UserControl
{
    private StockQuote? _quote;

    public StockRow(string symbol)
    {
        InitializeComponent();
        Symbol = symbol;
        SymbolText.Text = symbol;
        AutomationProperties.SetAutomationId(Header, "StockRow");
        AutomationProperties.SetName(Header, symbol);
        ShowExpanded();
    }

    public string Symbol { get; }

    public bool IsExpanded { get; private set; }

    public void Show(StockQuote quote)
    {
        _quote = quote;
        NameText.Text = quote.Name;
        PriceText.Text = StockQuotes.FormatPrice(quote.Price);
        ChangeText.Text = StockQuotes.FormatChange(quote);
        VisualStateManager.GoToState(this, quote.Change > 0 ? "Rise" : quote.Change < 0 ? "Fall" : "Level", false);
        AutomationProperties.SetName(Header, $"{quote.Symbol}, {quote.Name}, {PriceText.Text}, {ChangeText.Text}");
        if (IsExpanded)
            Draw();
    }

    /// <summary>Yahoo doesn't know the symbol: typed wrong, or delisted.</summary>
    public void ShowNotFound()
    {
        _quote = null;
        NameText.Text = "Not found";
        PriceText.Text = "—";
        ChangeText.Text = "";
        VisualStateManager.GoToState(this, "Level", false);
        AutomationProperties.SetName(Header, $"{Symbol}, not found");
        Draw();
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
        ShowExpanded();
        Draw();
    }

    private void ShowExpanded()
    {
        Graph.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        Chevron.Glyph = IsExpanded ? "" : "";
        AutomationProperties.SetHelpText(Header, IsExpanded ? "Expanded" : "Collapsed");
    }

    private void Graph_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();

    // The day so far fills the width; its lowest and highest prices, and the last close, fit the height.
    private void Draw()
    {
        double width = Graph.ActualWidth;
        double height = Graph.ActualHeight;
        var line = new PointCollection();
        CloseLine.Visibility = Visibility.Collapsed;
        if (_quote is { Day.Count: > 1 } quote && width > 0)
        {
            const double inset = 4;
            double low = Math.Min(quote.Day.Min(), quote.PreviousClose);
            double high = Math.Max(quote.Day.Max(), quote.PreviousClose);
            double range = high - low;
            double Y(double price) => range == 0 ? height / 2 : inset + (height - 2 * inset) * (high - price) / range;
            for (int i = 0; i < quote.Day.Count; i++)
                line.Add(new Point(width * i / (quote.Day.Count - 1), Y(quote.Day[i])));

            CloseLine.X1 = 0;
            CloseLine.X2 = width;
            CloseLine.Y1 = CloseLine.Y2 = Y(quote.PreviousClose);
            CloseLine.Visibility = Visibility.Visible;
        }
        Line.Points = line;
    }
}
