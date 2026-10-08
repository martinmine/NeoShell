using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Settings;
using Windows.Foundation;

namespace NeoShell.Desktop;

/// <summary>
/// The desktop's items panel: each icon's container goes at its position (<see cref="DesktopIcon.Position"/>, in cells),
/// one item in size.
/// </summary>
internal sealed partial class DesktopIconPanel : Panel
{
    private Size _item = new(80, 100);

    /// <summary>The size of one cell in effective pixels.</summary>
    public Size ItemSize
    {
        get => _item;
        set
        {
            if (_item == value)
                return;
            _item = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double columns = 0, rows = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(_item);
            if (Icon(child)?.Position is { } position)
            {
                columns = Math.Max(columns, position.X + 1);
                rows = Math.Max(rows, position.Y + 1);
            }
        }
        return new Size(columns * _item.Width, rows * _item.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in Children)
        {
            if (Icon(child) is { } icon)
            {
                IconPosition position = icon.Position ?? default;
                child.Arrange(new Rect(position.X * _item.Width, position.Y * _item.Height, _item.Width, _item.Height));
            }
        }
        return finalSize;
    }

    /// <summary>The cell at a point on the panel.</summary>
    public GridCell CellAt(Point point) =>
        new(Math.Max(0, (int)(point.X / _item.Width)), Math.Max(0, (int)(point.Y / _item.Height)));

    /// <summary>The cell nearest to an icon whose top left corner is at a point on the panel.</summary>
    public GridCell NearestCell(Point topLeft) =>
        new(Math.Max(0, (int)Math.Round(topLeft.X / _item.Width)), Math.Max(0, (int)Math.Round(topLeft.Y / _item.Height)));

    private static DesktopIcon? Icon(UIElement child) => (child as ContentControl)?.Content as DesktopIcon;
}
