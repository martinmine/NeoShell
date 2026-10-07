using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Notifications;

namespace NeoShell.Taskbar;

/// <summary>
/// An app's badge on its task button, over the top-right corner of the icon: a count or a glyph on the accent colour,
/// or a coloured dot. It pops in and shrinks away as Explorer's does, and slides when a wider count moves its left
/// edge (Taskbar.View.dll, <c>SharedAnimations</c>).
/// </summary>
public sealed partial class TaskBadge : UserControl
{
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(
        nameof(Badge), typeof(AppBadge), typeof(TaskBadge), new PropertyMetadata(null, (d, _) => ((TaskBadge)d).Update()));

    private CompositionScopedBatch? _exit;
    // The plate's width before an update of a badge already shown, for the slide; 0 for none.
    private double _widthBefore;

    public TaskBadge()
    {
        InitializeComponent();
        ElementCompositionPreview.SetIsTranslationEnabled(Plate, true);
        Plate.SizeChanged += Plate_SizeChanged;
    }

    public AppBadge? Badge
    {
        get => (AppBadge?)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    private void Update()
    {
        if (Badge is not { } badge)
        {
            if (Plate.Visibility == Visibility.Visible && _exit is null)
                Exit();
            return;
        }

        bool shown = Plate.Visibility == Visibility.Visible && _exit is null;
        _widthBefore = shown ? Plate.ActualWidth : 0;
        bool numeric = badge.Glyph == BadgeGlyph.None;
        Label.Text = BadgeLook.Text(badge);
        Label.FontSize = numeric ? 11 : 12;
        Label.Padding = numeric ? new Thickness(4, 0, 4, 0) : new Thickness(0);
        // Tight: the count's box is just its digits' height, centred in the plate as in Explorer.
        Label.TextLineBounds = numeric ? TextLineBounds.Tight : TextLineBounds.Full;
        if (numeric)
            Label.ClearValue(TextBlock.FontFamilyProperty);
        else
            Label.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"];

        if (BadgeLook.Background(badge.Glyph) is { } background)
            Plate.Background = new SolidColorBrush(background);
        else
            Plate.ClearValue(Border.BackgroundProperty);
        if (BadgeLook.Foreground(badge.Glyph) is { } foreground)
            Label.Foreground = new SolidColorBrush(foreground);
        else
            Label.ClearValue(TextBlock.ForegroundProperty);

        if (!shown)
            Enter();
    }

    /// <summary>
    /// Grows from nothing about the centre of its left end, past full size and back: 500 ms, to 110% at a third
    /// (ease in and out), then settling.
    /// </summary>
    private void Enter()
    {
        _exit = null;
        Plate.Visibility = Visibility.Visible;
        Visual visual = ElementCompositionPreview.GetElementVisual(Plate);
        Compositor compositor = visual.Compositor;
        visual.CenterPoint = new Vector3(8, 8, 0);
        Vector3KeyFrameAnimation grow = compositor.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(0, new Vector3(0, 0, 1));
        grow.InsertKeyFrame(0.33f, new Vector3(1.1f, 1.1f, 1), compositor.CreateCubicBezierEasingFunction(new(0.85f, 0), new(0.75f, 1)));
        grow.InsertKeyFrame(1, Vector3.One, compositor.CreateCubicBezierEasingFunction(new(0.35f, 0), new(0, 1)));
        grow.Duration = TimeSpan.FromMilliseconds(500);
        visual.StartAnimation("Scale", grow);
    }

    /// <summary>Shrinks away about its centre: 167 ms, fast then slow.</summary>
    private void Exit()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(Plate);
        Compositor compositor = visual.Compositor;
        visual.CenterPoint = new Vector3((float)Plate.ActualWidth / 2, (float)Plate.ActualHeight / 2, 0);
        Vector3KeyFrameAnimation shrink = compositor.CreateVector3KeyFrameAnimation();
        shrink.InsertKeyFrame(1, new Vector3(0, 0, 1), compositor.CreateCubicBezierEasingFunction(new(0, 0), new(0, 1)));
        shrink.Duration = TimeSpan.FromMilliseconds(167);

        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Scale", shrink);
        batch.End();
        batch.Completed += (_, _) =>
        {
            // A badge that came back meanwhile has started its own entrance.
            if (_exit == batch)
            {
                _exit = null;
                Plate.Visibility = Visibility.Collapsed;
            }
        };
        _exit = batch;
    }

    /// <summary>
    /// The plate keeps its right edge, so a longer count moves its left edge: it slides there from where it was, 333 ms
    /// easing out, as with Explorer's implicit reposition animation.
    /// </summary>
    private void Plate_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Only a badge that changed while shown slides: a new one pops in where it belongs.
        float moved = (float)(e.NewSize.Width - _widthBefore);
        bool slides = _widthBefore > 0 && moved != 0;
        _widthBefore = 0;
        if (!slides)
            return;

        Visual visual = ElementCompositionPreview.GetElementVisual(Plate);
        Compositor compositor = visual.Compositor;
        Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new Vector3(moved, 0, 0));
        slide.InsertKeyFrame(1, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new(0.55f, 0), new(0, 1)));
        slide.Duration = TimeSpan.FromMilliseconds(333);
        visual.StartAnimation("Translation", slide);
    }
}
