using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace NeoShell.Snap;

/// <summary>
/// The layouts of Explorer's Snap layouts flyouts (under the maximize button, Win+Z, and the bar at the top of the
/// screen): small previews of the screen, a zone of each to click. Measured on Explorer's at 100%: previews 98 by 64,
/// 12 apart and from the edges, zones 4 apart with only the layout's outer corners rounded, a faint fill and a
/// fainter outline; the zone under the pointer takes the accent colour. A suggested window's zone shows its app's
/// icon instead of the fill.
/// </summary>
internal sealed class SnapLayoutPicker
{
    // Effective pixels.
    public const double PreviewWidth = 98;
    public const double PreviewHeight = 64;
    public const double Spacing = 12;
    // Around the layouts: the flyout's 1 pixel edge, then 12 (14 at its top; the window's first column doesn't show);
    // the bar's 12 all round.
    public static readonly Thickness FlyoutPadding = new(14, 15, 12, 13);
    public static readonly Thickness BarPadding = new(12);
    private const double ZoneGap = 4;

    private readonly IReadOnlyList<SnapChoice> _choices;
    private readonly bool _dark;
    private readonly Brush _fill;
    private readonly Brush _stroke;
    private readonly Brush _accent;
    private readonly Brush _none = new SolidColorBrush(Colors.Transparent);
    private readonly List<List<Border>> _zones = [];
    private (int Choice, int Zone)? _highlighted;
    private readonly Thickness _padding;

    /// <param name="icons">The icons of the suggested windows, by their index in the choices.</param>
    /// <param name="numbers">Win+Z's: each layout's number in its middle, for the keyboard.</param>
    public SnapLayoutPicker(IReadOnlyList<SnapChoice> choices, int columns, Thickness padding, ElementTheme theme,
        IReadOnlyList<ImageSource?> icons, bool numbers)
    {
        _choices = choices;
        _padding = padding;
        _dark = theme == ElementTheme.Dark;
        // Explorer's, measured over its dark acrylic: white at 16% and 42%. The light theme's are the same in black.
        _fill = new SolidColorBrush(_dark ? Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0, 0, 0));
        _stroke = new SolidColorBrush(_dark ? Color.FromArgb(0x6B, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x5C, 0, 0, 0));
        _accent = new SolidColorBrush((Color)Application.Current.Resources[_dark ? "SystemAccentColorLight2" : "SystemAccentColorDark1"]);

        Columns = columns;
        int rows = (choices.Count + columns - 1) / columns;
        Root = new Grid
        {
            RequestedTheme = theme,
            Padding = padding,
            ColumnSpacing = Spacing,
            RowSpacing = Spacing,
        };
        for (int i = 0; i < columns; i++)
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PreviewWidth) });
        for (int i = 0; i < rows; i++)
            Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PreviewHeight) });
        for (int i = 0; i < choices.Count; i++)
        {
            Canvas layout = CreateLayout(i, icons, numbers);
            Grid.SetColumn(layout, i % columns);
            Grid.SetRow(layout, i / columns);
            Root.Children.Add(layout);
        }
    }

    public Grid Root { get; }

    /// <summary>
    /// The flyouts' acrylic, darker than the system's default: Explorer's shows little of what's behind (measured over a
    /// green window: #152E20 for #2E8B57).
    /// </summary>
    public static Color BackdropTint(ElementTheme theme) =>
        theme == ElementTheme.Dark ? Color.FromArgb(0xFF, 0x00, 0x00, 0x00) : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);

    public static (float Tint, float Luminosity) BackdropOpacities => (0.0f, 0.66f);

    public int Columns { get; }

    /// <summary>The picker's size in effective pixels.</summary>
    public Size Size
    {
        get
        {
            int rows = (_choices.Count + Columns - 1) / Columns;
            return new Size(_padding.Left + _padding.Right + Columns * PreviewWidth + (Columns - 1) * Spacing,
                _padding.Top + _padding.Bottom + rows * PreviewHeight + (rows - 1) * Spacing);
        }
    }

    public IReadOnlyList<SnapChoice> Choices => _choices;

    /// <summary>A zone was clicked: the layout's and the zone's index.</summary>
    public event Action<int, int>? Picked;

    /// <summary>The zone at a point (effective pixels from the picker's corner), if any.</summary>
    public (int Choice, int Zone)? ZoneAt(Point point)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            for (int z = 0; z < _zones[i].Count; z++)
            {
                Border zone = _zones[i][z];
                Rect bounds = zone.TransformToVisual(Root).TransformBounds(new Rect(0, 0, zone.Width, zone.Height));
                if (bounds.Contains(point))
                    return (i, z);
            }
        }
        return null;
    }

    public (int Choice, int Zone)? Highlighted => _highlighted;

    /// <summary>
    /// Shows the zone as the one to snap to (null for none). In a layout with suggestions, the window's own zone fills
    /// with the accent colour and the suggested ones are outlined in it, as all of them would be placed.
    /// </summary>
    public void Highlight((int Choice, int Zone)? target)
    {
        if (target == _highlighted)
            return;
        if (_highlighted is { } old)
            Paint(old.Choice, null);
        _highlighted = target;
        if (target is { } next)
            Paint(next.Choice, next.Zone);
    }

    public void Pick(int choice, int zone) => Picked?.Invoke(choice, zone);

    private Canvas CreateLayout(int index, IReadOnlyList<ImageSource?> icons, bool numbers)
    {
        var canvas = new Canvas { Width = PreviewWidth, Height = PreviewHeight };
        SnapChoice choice = _choices[index];
        var zones = new List<Border>();
        for (int i = 0; i < choice.Zones.Count; i++)
        {
            SnapZone zone = choice.Zones[i];
            // Inner edges give way for the gap; the layout's outer edges and corners are the preview's.
            bool left = zone.X < 0.001, top = zone.Y < 0.001;
            bool right = zone.X + zone.Width > 0.999, bottom = zone.Y + zone.Height > 0.999;
            double x = zone.X * PreviewWidth + (left ? 0 : ZoneGap / 2);
            double y = zone.Y * PreviewHeight + (top ? 0 : ZoneGap / 2);
            double width = (zone.X + zone.Width) * PreviewWidth - (right ? 0 : ZoneGap / 2) - x;
            double height = (zone.Y + zone.Height) * PreviewHeight - (bottom ? 0 : ZoneGap / 2) - y;
            const double radius = 4;
            var border = new Border
            {
                Width = width,
                Height = height,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(left && top ? radius : 0, right && top ? radius : 0, right && bottom ? radius : 0, left && bottom ? radius : 0),
            };
            int suggested = choice.Suggested[i];
            if (suggested >= 0 && suggested < icons.Count)
                border.Child = new Image { Source = icons[suggested], Width = 20, Height = 20 };
            AutomationProperties.SetName(border, $"Layout {index + 1}, zone {i + 1}");
            AutomationProperties.SetAutomationId(border, $"SnapZone{index + 1}_{i + 1}");
            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            int zoneIndex = i;
            border.PointerEntered += (_, _) => Highlight((index, zoneIndex));
            border.PointerExited += (_, _) =>
            {
                if (_highlighted == (index, zoneIndex))
                    Highlight(null);
            };
            border.Tapped += (_, _) => Pick(index, zoneIndex);
            canvas.Children.Add(border);
            zones.Add(border);
        }
        _zones.Add(zones);
        Paint(index, null);

        if (numbers)
        {
            // Explorer's badge: an opaque grey block with the number, over the middle of the layout.
            var badge = new Border
            {
                Width = 13,
                Height = 24,
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x76, 0x76, 0x76)),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = (index + 1).ToString(),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Canvas.SetLeft(badge, Math.Round((PreviewWidth - 13) / 2));
            Canvas.SetTop(badge, (PreviewHeight - 24) / 2);
            canvas.Children.Add(badge);
        }
        return canvas;
    }

    // A layout's zones, with the one to snap to (or none) in the accent colour.
    private void Paint(int choice, int? target)
    {
        SnapChoice layout = _choices[choice];
        bool suggestion = layout.IsSuggestion;
        for (int i = 0; i < _zones[choice].Count; i++)
        {
            Border zone = _zones[choice][i];
            bool own = layout.Suggested[i] < 0;
            bool lit = target is { } t && (suggestion || i == t);
            zone.Background = lit && (own || !suggestion) ? _accent : own ? _fill : _none;
            zone.BorderBrush = lit ? _accent : _stroke;
        }
    }
}
