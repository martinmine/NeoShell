using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NeoShell.Widgets;
using Windows.Foundation;
using Windows.UI;

namespace NeoShell.Themes.Concept2088;

/// <summary>
/// What the 2088 concept's surfaces share (see docs/design/theme-2088.md): the palette (near-black, white text, one
/// signal red), the fonts, and the pieces its panels are made of.
/// </summary>
internal static class Hud
{
    public static readonly Color Void = Color.FromArgb(0xFF, 0x07, 0x07, 0x0A);
    public static readonly Color Panel = Color.FromArgb(0xFF, 0x0C, 0x0C, 0x11);
    public static readonly Color Raised = Color.FromArgb(0xFF, 0x16, 0x16, 0x1D);
    public static readonly Color Line = Color.FromArgb(0xFF, 0x2A, 0x2A, 0x33);
    public static readonly Color LineBright = Color.FromArgb(0xFF, 0x4A, 0x4A, 0x56);
    public static readonly Color Text = Color.FromArgb(0xFF, 0xF2, 0xF2, 0xF4);
    public static readonly Color Dim = Color.FromArgb(0xFF, 0x8E, 0x8E, 0x9A);
    public static readonly Color Faint = Color.FromArgb(0xFF, 0x55, 0x55, 0x60);
    public static readonly Color Red = Color.FromArgb(0xFF, 0xFF, 0x2A, 0x3D);
    public static readonly Color RedHot = Color.FromArgb(0xFF, 0xFF, 0x5A, 0x69);
    public static readonly Color RedDeep = Color.FromArgb(0xFF, 0x3A, 0x0A, 0x12);

    /// <summary>Headings and labels: Windows' own DIN-like face, condensed.</summary>
    public static readonly FontFamily Display = new("Bahnschrift SemiCondensed");

    /// <summary>Readouts: numbers that change keep their width.</summary>
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>The Japanese side of a label.</summary>
    public static readonly FontFamily Japanese = new("Yu Gothic UI");

    private static ControlTemplate? s_buttonTemplate;
    private static ResourceMonitor? s_resources;

    /// <summary>The processor and memory readouts Nexus and the desktop show; it samples only while someone listens.</summary>
    public static ResourceMonitor Resources => s_resources ??= new();

    public static SolidColorBrush Solid(Color color) => new(color);

    public static SolidColorBrush Solid(Color color, double opacity) => new(color) { Opacity = opacity };

    public static TextBlock Label(string text, double size, Color color, FontFamily? font = null, int spacing = 0, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = font ?? Display,
        Foreground = Solid(color),
        CharacterSpacing = spacing,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextLineBounds = TextLineBounds.Tight,
        IsHitTestVisible = false,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A label in English with its Japanese after it, the concept's bilingual headings ("MODULES // モジュール").</summary>
    public static StackPanel Heading(string english, string japanese, Color color)
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsHitTestVisible = false };
        heading.Children.Add(Label(english, 11, color, spacing: 200, bold: true));
        heading.Children.Add(Label("//", 11, Faint, Mono));
        heading.Children.Add(Label(japanese, 11, Red, Japanese));
        return heading;
    }

    /// <summary>A button that is only its content; the content shows hover and press itself.</summary>
    public static Button Plain(string automationId, string name, UIElement content)
    {
        s_buttonTemplate ??= (ControlTemplate)XamlReader.Load(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
            "<ContentPresenter Background='Transparent' Content='{TemplateBinding Content}'" +
            " HorizontalContentAlignment='Stretch' VerticalContentAlignment='Stretch'/></ControlTemplate>");
        var button = new Button
        {
            Template = s_buttonTemplate,
            Content = content,
            UseSystemFocusVisuals = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>A ruler's ticks along the top of <paramref name="canvas"/>: a short one every 8 pixels, a long one every 64.</summary>
    public static void DrawRuler(Canvas canvas, double width, Color color)
    {
        canvas.Children.Clear();
        for (double x = 0; x < width; x += 8)
        {
            bool major = x % 64 == 0;
            canvas.Children.Add(new Rectangle
            {
                Width = 1,
                Height = major ? 5 : 2,
                Fill = Solid(color, major ? 0.9 : 0.45),
                RenderTransform = new TranslateTransform { X = x },
            });
        }
    }

    /// <summary>Thin lines every 3 pixels over a surface, as an old monitor's scan lines.</summary>
    public static Brush ScanLines(double opacity) => new LinearGradientBrush
    {
        MappingMode = BrushMappingMode.Absolute,
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 3),
        SpreadMethod = GradientSpreadMethod.Repeat,
        Opacity = opacity,
        GradientStops =
        {
            new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0 },
            new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0.66 },
            new GradientStop { Color = Color.FromArgb(0xFF, 0, 0, 0), Offset = 0.67 },
            new GradientStop { Color = Color.FromArgb(0xFF, 0, 0, 0), Offset = 1 },
        },
    };
}

/// <summary>
/// The concept's panel: a plate with its top-left and bottom-right corners cut off, an outline, and edge markers (L
/// brackets on the two square corners, a tick on each cut) so it reads as a physical part. Its other children sit on
/// the plate.
/// </summary>
internal sealed partial class HudFrame : Grid
{
    private readonly Polygon _plate = new() { IsHitTestVisible = false };
    private readonly Canvas _markers = new() { IsHitTestVisible = false };
    private Color _markerColor = Hud.Red;
    private double _markerLength = 8;

    public HudFrame(double cut, Color fill, Color stroke)
    {
        Cut = cut;
        Children.Add(_plate);
        Children.Add(_markers);
        SetLook(fill, stroke);
        SizeChanged += (_, _) => Shape();
    }

    public double Cut { get; }

    /// <summary>How long the L brackets' arms are; 0 leaves the markers out.</summary>
    public double MarkerLength
    {
        get => _markerLength;
        set
        {
            if (value == _markerLength)
                return;
            _markerLength = value;
            Shape();
        }
    }

    public void SetLook(Color fill, Color stroke, Color? marker = null)
    {
        _plate.Fill = Hud.Solid(fill);
        _plate.Stroke = Hud.Solid(stroke);
        _plate.StrokeThickness = 1;
        if (marker is { } color && color != _markerColor)
        {
            _markerColor = color;
            Shape();
        }
    }

    private void Shape()
    {
        double w = ActualWidth, h = ActualHeight, c = Math.Min(Cut, Math.Min(w, h) / 2);
        if (w <= 0 || h <= 0)
            return;

        // Half a pixel in, so the 1-pixel outline is drawn whole rather than half outside.
        _plate.Points = new PointCollection { new(c + 0.5, 0.5), new(w - 0.5, 0.5), new(w - 0.5, h - c - 0.5), new(w - c - 0.5, h - 0.5), new(0.5, h - 0.5), new(0.5, c + 0.5) };

        _markers.Children.Clear();
        double m = MarkerLength;
        if (m <= 0)
            return;
        // Top right and bottom left: L brackets 2 thick, on the outline.
        Bar(w - m, 0, m, 2);
        Bar(w - 2, 0, 2, m);
        Bar(0, h - 2, m, 2);
        Bar(0, h - m, 2, m);
        // The cuts: a short tick across each.
        if (c >= 6)
        {
            Tick(c / 2 - 2, c / 2 - 2);
            Tick(w - c / 2 - 2, h - c / 2 - 2);
        }
    }

    private void Bar(double x, double y, double width, double height) =>
        _markers.Children.Add(new Rectangle
        {
            Width = width,
            Height = height,
            Fill = Hud.Solid(_markerColor),
            RenderTransform = new TranslateTransform { X = x, Y = y },
        });

    private void Tick(double x, double y) =>
        _markers.Children.Add(new Rectangle
        {
            Width = 4,
            Height = 4,
            Fill = Hud.Solid(_markerColor),
            RenderTransform = new TranslateTransform { X = x, Y = y },
        });
}

/// <summary>A meter of separate cells, lit from the left (the concept's progress and load bars).</summary>
internal sealed partial class SegmentBar : StackPanel
{
    private readonly Rectangle[] _cells;

    public SegmentBar(int cells, double cellWidth, double height)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        IsHitTestVisible = false;
        _cells = [.. Enumerable.Range(0, cells).Select(_ => new Rectangle { Width = cellWidth, Height = height })];
        foreach (Rectangle cell in _cells)
            Children.Add(cell);
        SetValue(0);
    }

    /// <summary>0 to 1; the cells past 85% light up white, a warning in a red interface.</summary>
    public void SetValue(double value)
    {
        int lit = (int)Math.Round(Math.Clamp(value, 0, 1) * _cells.Length);
        for (int i = 0; i < _cells.Length; i++)
        {
            bool hot = i >= _cells.Length * 0.85;
            _cells[i].Fill = Hud.Solid(i < lit ? hot ? Hud.Text : Hud.Red : Hud.Line);
        }
    }
}
