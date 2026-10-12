using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Windowing;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;

namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// XP's menus: <see cref="Classic"/> the flat white menus (the taskbar's, a window's system menu), <see cref="Programs"/>
/// Start's cascading All Programs, <see cref="Group"/> the blue menu of a group button's windows.
/// </summary>
internal enum XpMenuStyle { Classic, Programs, Group }

/// <summary>Where a menu goes from what it's shown for (physical pixels).</summary>
internal enum XpMenuPlacement
{
    /// <summary>Top left at the point, flipped up or left to stay on screen (a right-click's menu).</summary>
    AtPoint,

    /// <summary>Bottom left at the rectangle's top left (a task button's group).</summary>
    Above,

    /// <summary>Beside the rectangle, its top at the rectangle's (a submenu, Start's All Programs).</summary>
    Beside,
}

internal sealed record XpMenuItem
{
    public string Text { get; init; } = "";

    /// <summary>A shortcut shown in its own column ("Alt+F4").</summary>
    public string Shortcut { get; init; } = "";

    public Action? Invoke { get; init; }

    /// <summary>A submenu's items, read when it opens.</summary>
    public Func<IReadOnlyList<XpMenuItem>>? Submenu { get; init; }

    public Func<Task<ImageSource?>>? Icon { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Bold, what a double-click would do.</summary>
    public bool IsDefault { get; init; }

    public bool IsChecked { get; init; }

    public SystemMenuGlyph Glyph { get; init; }

    public string? AutomationId { get; init; }

    public static XpMenuItem Separator { get; } = new() { IsSeparator = true };
}

/// <summary>
/// One of XP's menus on screen, with the submenus open from it. Drawn from XP's measurements (docs/design/theme-xp.md):
/// a hovered item lights up, a submenu opens after the menu delay (or at once on a click), a click on an item runs it and
/// closes every menu, a click anywhere else closes them too.
/// </summary>
internal sealed class XpMenu
{
    private const int Shadow = 5;
    private static readonly TimeSpan s_submenuDelay = TimeSpan.FromMilliseconds(400);

    // Classic: grey border, 2 above and below the items, 17-pixel items (21 for a system menu command with a glyph).
    private const uint ClassicBorder = 0xACA899;
    private const uint ClassicHighlight = 0x316AC5;
    // Programs: a blue band down each column's left, blue borders, 23-pixel items.
    private static readonly uint[] s_programsBand = [0x3E87E4, 0x84B2ED, 0x2C7AE0, 0x2D7CE2, 0x307FE5, 0x3382E8];
    private const uint ProgramsBorder = 0x55A1FF;
    private const uint ProgramsRightBorder = 0x347EE2;
    private const uint ProgramsHighlight = 0x1665CB;
    // Group: the taskbar's blue with an orange top, 22-pixel items 26 apart.
    private const uint GroupBody = 0x2157D5;
    private const uint GroupHighlight = 0x4F96FF;

    private readonly XpPopup _popup = new(activates: false);
    private readonly IReadOnlyList<XpMenuItem> _items;
    private readonly XpMenuStyle _style;
    private readonly XpMenu? _parent;
    private readonly DisplayMonitor _monitor;
    private readonly double _scale;
    private readonly int _minWidth;
    private readonly List<(int Left, int Top, int Width, int Height)> _rects = [];
    private readonly DispatcherQueueTimer _submenuTimer;
    private ClickCatcher? _catcher;
    private Action? _closed;
    private int _width, _height;
    private int _hot = -1;
    private XpMenu? _child;
    private int _childItem = -1;
    private bool _closedAlready;

    private XpMenu(IReadOnlyList<XpMenuItem> items, XpMenuStyle style, XpMenu? parent, DisplayMonitor monitor, int minWidth)
    {
        _items = items;
        _style = style;
        _parent = parent;
        _monitor = monitor;
        _scale = monitor.Dpi / 96.0;
        _minWidth = minWidth;
        _submenuTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _submenuTimer.IsRepeating = false;
        _submenuTimer.Interval = s_submenuDelay;
        _submenuTimer.Tick += (_, _) => OpenSubmenu(_hot);
        Layout();
        AddItems();
        Paint();
    }

    /// <summary>Whether this menu (or one of its submenus) is still open.</summary>
    public bool IsOpen => !_closedAlready;

    /// <param name="anchor">The point or rectangle it's shown for, in physical pixels.</param>
    /// <param name="catchClicks">A click anywhere else closes the menus (not for Start's, which Start closes itself).</param>
    /// <param name="minWidth">At least this wide, in art pixels (a group's menu is as wide as its button).</param>
    public static XpMenu Show(IReadOnlyList<XpMenuItem> items, XpMenuStyle style, DisplayMonitor monitor, RectInt32 anchor,
        XpMenuPlacement placement, Action? closed = null, bool catchClicks = true, int minWidth = 0)
    {
        var menu = new XpMenu(items, style, null, monitor, minWidth) { _closed = closed };
        menu.Place(anchor, placement);
        if (catchClicks)
        {
            menu._catcher = new ClickCatcher();
            menu._catcher.Clicked += menu.CloseAll;
            menu._catcher.Show(monitor.Bounds, menu._popup.Handle);
        }
        return menu;
    }

    /// <summary>Closes this menu and every menu it was opened from.</summary>
    public void CloseAll()
    {
        XpMenu root = this;
        while (root._parent is { } parent)
            root = parent;
        root.Close();
    }

    /// <summary>A key, given to the innermost open menu: arrows move and open, Enter runs, Esc and Left close a level.</summary>
    public bool HandleKey(VirtualKey key)
    {
        if (_child is { IsOpen: true } child)
            return child.HandleKey(key);

        switch (key)
        {
            case VirtualKey.Down:
            case VirtualKey.Up:
                MoveHot(key == VirtualKey.Down ? 1 : -1);
                return true;
            case VirtualKey.Right:
                if (_hot >= 0 && _items[_hot].Submenu is not null)
                    OpenSubmenu(_hot, keyboard: true);
                return true;
            case VirtualKey.Enter:
                if (_hot >= 0)
                    Choose(_hot, keyboard: true);
                return true;
            case VirtualKey.Left when _parent is not null:
            case VirtualKey.Escape:
                if (_parent is { } parent)
                {
                    Close();
                    parent._child = null;
                    parent._childItem = -1;
                }
                else
                {
                    CloseAll();
                }
                return true;
        }
        return false;
    }

    private void Close()
    {
        if (_closedAlready)
            return;

        _closedAlready = true;
        _submenuTimer.Stop();
        _child?.Close();
        _catcher?.Dispose();
        _popup.Shut();
        _closed?.Invoke();
    }

    private void Place(RectInt32 anchor, XpMenuPlacement placement)
    {
        int width = (int)Math.Round(_width * _scale), height = (int)Math.Round(_height * _scale);
        RectInt32 screen = _monitor.Bounds;
        int x, y;
        switch (placement)
        {
            case XpMenuPlacement.Above:
                (x, y) = (anchor.X, anchor.Y - height);
                break;
            case XpMenuPlacement.Beside:
                x = anchor.X + anchor.Width;
                if (x + width > screen.X + screen.Width)
                    x = anchor.X - width;
                y = Math.Min(anchor.Y, screen.Y + screen.Height - height);
                break;
            default:
                x = anchor.X + width <= screen.X + screen.Width ? anchor.X : anchor.X - width;
                y = anchor.Y + height <= screen.Y + screen.Height ? anchor.Y : anchor.Y - height;
                break;
        }
        _popup.ShowAt(new PointInt32(Math.Max(screen.X, x), Math.Max(screen.Y, y)), _scale);
    }

    // Each item's rectangle in the menu (without the shadow), and the menu's size.
    private void Layout()
    {
        switch (_style)
        {
            case XpMenuStyle.Classic:
            {
                int textLeft = HasPictures ? 25 : 20;
                int text = _items.Max(item => Measure(item));
                int shortcut = _items.Max(item => item.Shortcut.Length > 0 ? GdiText.Measure(item.Shortcut, XpArt.Tahoma) : 0);
                _width = Math.Max(_minWidth, textLeft + text + (shortcut > 0 ? 20 + shortcut + 21 : 23) + 1);
                int y = 3;
                foreach (XpMenuItem item in _items)
                {
                    int height = item.IsSeparator ? 9 : item.Glyph != SystemMenuGlyph.None || item.Icon is not null ? 21 : 17;
                    _rects.Add((0, y, _width, height));
                    y += height;
                }
                _height = y + 3;
                break;
            }
            case XpMenuStyle.Programs:
            {
                // Columns as tall as the screen allows, all as wide as the widest item, as XP's All Programs.
                int columnWidth = Math.Max(_minWidth, 36 + _items.Max(Measure) + 22);
                int maxHeight = (int)(_monitor.Bounds.Height / _scale) - 2;
                int column = 0, y = 1, tallest = 0;
                foreach (XpMenuItem item in _items)
                {
                    int height = item.IsSeparator ? 8 : 23;
                    if (y + height > maxHeight - 1 && y > 1)
                    {
                        column++;
                        y = 1;
                    }
                    _rects.Add((column * columnWidth, y, columnWidth, height));
                    y += height;
                    tallest = Math.Max(tallest, y);
                }
                _width = (column + 1) * columnWidth;
                _height = tallest + 1;
                break;
            }
            default:
            {
                // Long titles are cut off with an ellipsis rather than making the menu wider than a third of the screen.
                int widest = (int)(_monitor.Bounds.Width / _scale / 3);
                _width = Math.Max(_minWidth, Math.Min(widest, 35 + _items.Max(Measure) + 24));
                for (int i = 0; i < _items.Count; i++)
                    _rects.Add((0, 2 + i * 26, _width, 22));
                _height = 2 + _items.Count * 26 - 4 + 2;
                break;
            }
        }
    }

    private static int Measure(XpMenuItem item) =>
        item.IsSeparator ? 0 : GdiText.Measure(item.Text, item.IsDefault ? XpArt.TahomaBold : XpArt.Tahoma);

    private void AddItems()
    {
        _popup.Overlay.Children.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            XpMenuItem item = _items[i];
            if (item.IsSeparator)
                continue;

            (int left, int top, int width, int height) = _rects[i];
            if (item.Icon is not null)
                _ = ShowIcon(item, IconPlace(left, top, height));
            Button hit = XpHit.Create(item.AutomationId ?? "XpMenuItem", item.Text);
            XpHit.Place(hit, left, top, width, height);
            int index = i;
            hit.PointerEntered += (_, _) => Hover(index);
            hit.Click += (_, _) => Choose(index, keyboard: false);
            _popup.Overlay.Children.Add(hit);
        }
    }

    private (int X, int Y) IconPlace(int left, int top, int height) => _style switch
    {
        XpMenuStyle.Programs => (left + 12, top + 4),
        XpMenuStyle.Group => (left + 11, top + 4),
        _ => (left + 4, top + (height - 16) / 2),
    };

    // A classic menu with pictures (a system menu's glyphs, Quick Launch's icons) has its text further in and taller items.
    private bool HasPictures => _items.Any(item => item.Glyph != SystemMenuGlyph.None || item.Icon is not null);

    private async Task ShowIcon(XpMenuItem item, (int X, int Y) place)
    {
        if (item.Icon is not { } load || await load() is not { } source || _closedAlready)
            return;

        var image = new Image { Source = source, IsHitTestVisible = false };
        XpHit.Place(image, place.X, place.Y, 16, 16);
        _popup.Overlay.Children.Insert(0, image);
    }

    private void Hover(int index)
    {
        if (index == _hot)
            return;

        _hot = index;
        Paint();
        _submenuTimer.Stop();
        // Another item closes the open submenu after the delay too, so a diagonal move into the submenu keeps it.
        if (_items[index].Submenu is not null || _child is not null)
            _submenuTimer.Start();
    }

    private void Choose(int index, bool keyboard)
    {
        XpMenuItem item = _items[index];
        if (!item.IsEnabled)
            return;

        if (item.Submenu is not null)
        {
            OpenSubmenu(index, keyboard);
            return;
        }
        CloseAll();
        item.Invoke?.Invoke();
    }

    private void OpenSubmenu(int index, bool keyboard = false)
    {
        _submenuTimer.Stop();
        if (index == _childItem && _child is { IsOpen: true })
            return;

        _child?.Close();
        _child = null;
        _childItem = -1;
        if (index < 0 || _items[index].Submenu is not { } submenu || !_items[index].IsEnabled)
            return;

        IReadOnlyList<XpMenuItem> items = submenu();
        if (items.Count == 0)
            items = [new XpMenuItem { Text = "(Empty)", IsEnabled = false }];
        var child = new XpMenu(items, _style, this, _monitor, 0);
        (int left, int top, int width, _) = _rects[index];
        RectInt32 bounds = _popup.ScreenBounds;
        // Classic submenus overlap their menu by its right margin, Start's open right beside the column, level with the item.
        int overlap = _style == XpMenuStyle.Classic ? 5 : 0;
        int lift = _style == XpMenuStyle.Classic ? 3 : 1;
        var anchor = new RectInt32(
            bounds.X + (int)Math.Round(left * _scale), bounds.Y + (int)Math.Round((top - lift) * _scale),
            (int)Math.Round((width - overlap) * _scale), 1);
        child.Place(anchor, XpMenuPlacement.Beside);
        if (keyboard)
            child.MoveHot(1);
        _child = child;
        _childItem = index;
    }

    private void MoveHot(int step)
    {
        if (_items.All(item => item.IsSeparator))
            return;

        int index = _hot;
        do
            index = (index + step + _items.Count) % _items.Count;
        while (_items[index].IsSeparator);
        _hot = index;
        Paint();
    }

    private void Paint()
    {
        var canvas = new XpCanvas(_width + Shadow, _height + Shadow);
        XpPainting.DropShadow(canvas, 0, 0, _width, _height);
        switch (_style)
        {
            case XpMenuStyle.Classic:
                canvas.Fill(0, 0, _width, _height, ClassicBorder);
                canvas.Fill(1, 1, _width - 2, _height - 2, 0xFFFFFF);
                break;
            case XpMenuStyle.Programs:
                canvas.Fill(0, 0, _width, _height, 0xFFFFFF);
                foreach (int columnLeft in _rects.Select(rect => rect.Left).Distinct())
                {
                    for (int i = 0; i < s_programsBand.Length; i++)
                        canvas.Fill(columnLeft + i, 0, 1, _height, s_programsBand[i]);
                }
                canvas.Fill(0, 0, _width, 1, ProgramsBorder);
                canvas.Fill(0, _height - 1, _width, 1, ProgramsBorder);
                canvas.Fill(_width - 1, 1, 1, _height - 2, ProgramsRightBorder);
                break;
            default:
                canvas.Fill(0, 0, _width, _height, GroupBody);
                canvas.Fill(0, 0, _width, 1, 0xE68B2C);
                canvas.Fill(0, 1, _width, 1, 0xFFC73C);
                canvas.Fill(0, 2, 1, _height - 2, 0x1E4FC3);
                canvas.Fill(1, 2, 1, _height - 2, 0x3A97F3);
                canvas.Fill(2, 2, 1, _height - 2, 0x2560DE);
                canvas.Fill(_width - 2, 2, 1, _height - 2, 0x2053CA);
                canvas.Fill(_width - 1, 2, 1, _height - 2, 0x1D4AB5);
                canvas.Fill(3, _height - 2, _width - 5, 1, 0x2053C9);
                canvas.Fill(3, _height - 1, _width - 4, 1, 0x1D4AB5);
                break;
        }
        bool glyphs = HasPictures;
        int shortcutX = _style == XpMenuStyle.Classic ? (glyphs ? 25 : 20) + _items.Max(Measure) + 20 : 0;
        for (int i = 0; i < _items.Count; i++)
            PaintItem(canvas, _items[i], _rects[i], i == _hot, glyphs, shortcutX);
        _popup.Draw(canvas);
    }

    private void PaintItem(XpCanvas canvas, XpMenuItem item, (int Left, int Top, int Width, int Height) rect, bool hot, bool glyphs, int shortcutX)
    {
        (int left, int top, int width, int height) = rect;
        switch (_style)
        {
            case XpMenuStyle.Classic:
            {
                if (item.IsSeparator)
                {
                    canvas.Fill(left + 4, top + 3, width - 9, 1, ClassicBorder);
                    return;
                }
                if (hot)
                    canvas.Fill(left + 3, top, width - 9, height, ClassicHighlight);
                uint color = !item.IsEnabled ? ClassicBorder : hot ? 0xFFFFFFu : 0x000000u;
                // Text 1 below a 17-pixel item's top, 3 below a 21-pixel one's; Marlett's glyphs 4 below.
                int textTop = top + (height - 13) / 2 - 1;
                canvas.Text(item.Text, left + (glyphs ? 25 : 20), textTop, width - 40, 13, item.IsDefault ? XpArt.TahomaBold : XpArt.Tahoma, color);
                if (item.Shortcut.Length > 0)
                    canvas.Text(item.Shortcut, left + shortcutX, textTop, width - shortcutX, 13, XpArt.Tahoma, color);
                string? glyph = item.Glyph switch
                {
                    SystemMenuGlyph.Restore => "2",
                    SystemMenuGlyph.Minimize => "0",
                    SystemMenuGlyph.Maximize => "1",
                    SystemMenuGlyph.Close => "r",
                    _ => item.IsChecked ? "a" : null,
                };
                if (glyph is not null)
                    canvas.Text(glyph, left + 4, item.Glyph != SystemMenuGlyph.None ? top + 4 : textTop, 16, 16, Marlett, color);
                if (item.Submenu is not null)
                    XpPainting.SubmenuArrow(canvas, left + width - 12, top + 5, hot ? 0xFFFFFFu : 0x000000u);
                break;
            }
            case XpMenuStyle.Programs:
            {
                if (item.IsSeparator)
                {
                    XpPainting.FadingLine(canvas, left + 6, left + width - 20, top + 3);
                    return;
                }
                if (hot)
                    canvas.Fill(left + 6, top - 1, width - 6, 23, ProgramsHighlight);
                uint color = !item.IsEnabled ? ClassicBorder : hot ? 0xFFFFFFu : 0x000000u;
                canvas.Text(item.Text, left + 36, top + 5, width - 58, 13, XpArt.Tahoma, color);
                if (item.Submenu is not null)
                    XpPainting.SubmenuArrow(canvas, left + width - 9, top + 7, hot ? 0xFFFFFFu : 0x000000u);
                break;
            }
            default:
                if (hot)
                    canvas.Fill(3, top, width - 3, height, GroupHighlight);
                canvas.Text(item.Text, 35, top + 4, width - 45, 13, XpArt.Tahoma, 0xFFFFFF);
                break;
        }
    }

    private static readonly GdiFont Marlett = new("Marlett", 13, Bold: false);
}
