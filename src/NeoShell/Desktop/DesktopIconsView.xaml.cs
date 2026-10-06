using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.Themes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using GridCell = NeoShell.Settings.GridCell;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace NeoShell.Desktop;

/// <summary>
/// The desktop's icons on one monitor's wallpaper window, with Explorer's mouse and keyboard handling and context
/// menus for the icons and for the desktop itself.
/// </summary>
internal sealed partial class DesktopIconsView : UserControl
{
    private static readonly string[] RunnableExtensions = [".exe", ".lnk", ".bat", ".cmd", ".msi"];

    /// <summary>Glyphs for the shell's standard commands, which come without an image.</summary>
    private static readonly Dictionary<string, string> VerbGlyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["open"] = "",
        ["runas"] = "",
        ["openas"] = "",
        ["cut"] = "",
        ["copy"] = "",
        ["rename"] = "",
        ["delete"] = "",
        ["copyaspath"] = "",
        ["properties"] = "",
    };

    /// <summary>
    /// What Display settings and Personalize open instead of the Settings app, which can't start while NeoShell is
    /// the shell (the only time the desktop is NeoShell's). Control Panel's pages for them open Settings too; these
    /// classic dialogs are what's left: the display adapter's properties (with its modes) and the desktop icons.
    /// </summary>
    private static readonly Dictionary<string, string> ClassicSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Display"] = "display.dll,ShowAdapterSettings 0",
        ["Personalize"] = "shell32.dll,Control_RunDLL desk.cpl,,0",
    };

    /// <summary>Verbs that work on the files without opening a window.</summary>
    private static readonly string[] InPlaceVerbs = ["cut", "copy", "delete", "copyaspath"];

    private readonly DesktopIcons _icons;
    private readonly nint _hwnd;
    private readonly DisplayMonitor _monitor;
    private MarqueeDrag? _marquee;
    private bool _dragged;
    private IconPress? _press;
    private DesktopDragDrop? _dragDrop;
    private OwnDrag? _ownDrag;
    private int _rows = 1;
    private int _columns = 1;

    /// <param name="hwnd">The wallpaper window: owner of the shell's menus and dialogs.</param>
    public DesktopIconsView(DesktopIcons icons, nint hwnd, DisplayMonitor monitor)
    {
        _icons = icons;
        _hwnd = hwnd;
        _monitor = monitor;
        InitializeComponent();

        IconGrid.ItemsSource = icons.Icons;
        Loaded += (_, _) =>
        {
            icons.Refreshed += Apply;
            icons.Scale = XamlRoot.RasterizationScale;
            Apply();
            _ = icons.RefreshAsync();
            StartDragDrop();
        };
        Unloaded += (_, _) =>
        {
            icons.Refreshed -= Apply;
            _dragDrop?.Dispose();
            _dragDrop = null;
        };
        // Handled events too: the grid's scroll viewer takes presses on the empty space between and around icons.
        Root.AddHandler(PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), handledEventsToo: true);
        Root.AddHandler(PointerMovedEvent, new PointerEventHandler(Root_PointerMoved), handledEventsToo: true);
        Root.AddHandler(PointerReleasedEvent, new PointerEventHandler(Root_PointerReleased), handledEventsToo: true);
        Root.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Root_PointerReleased), handledEventsToo: true);
    }

    private IReadOnlyList<DesktopItem> Selection => [.. IconGrid.SelectedItems.Cast<DesktopIcon>().Select(icon => icon.Item)];

    /// <summary>Applies the view settings, the system theme (for the menus) and the work area (to keep clear of the taskbar).</summary>
    private void Apply()
    {
        RequestedTheme = ShellTheme.Current.ReadTheme();
        IconGrid.Visibility = _icons.View.ShowIcons ? Visibility.Visible : Visibility.Collapsed;

        if (IconGrid.ItemsPanelRoot is not DesktopIconPanel panel)
            return;
        int size = _icons.View.IconSize;
        panel.ItemSize = new Size(Math.Max(80, size + 36), size + 52);

        // The work area changes when the taskbar does (auto-hide, another size), so it's read again every time.
        if (XamlRoot is null || DisplayMonitor.GetAll().FirstOrDefault(monitor => monitor.Handle == _monitor.Handle) is not { } current)
            return;

        double scale = XamlRoot.RasterizationScale;
        RectInt32 bounds = current.Bounds;
        RectInt32 work = current.WorkArea;
        IconGrid.Margin = new Thickness(
            (work.X - bounds.X) / scale,
            (work.Y - bounds.Y) / scale,
            (bounds.X + bounds.Width - work.X - work.Width) / scale,
            (bounds.Y + bounds.Height - work.Y - work.Height) / scale);

        // The grid is what fits in the work area, inside the list's padding.
        _rows = Math.Max(1, (int)((work.Height / scale - IconGrid.Padding.Top - IconGrid.Padding.Bottom) / panel.ItemSize.Height));
        _columns = Math.Max(1, (int)((work.Width / scale - IconGrid.Padding.Left - IconGrid.Padding.Right) / panel.ItemSize.Width));
        _icons.Arrange(_rows, _columns);
        panel.InvalidateMeasure();
    }

    /// <summary>The cell of the icon grid at a point on the screen (physical pixels).</summary>
    private GridCell CellAtScreen(int x, int y) =>
        IconGrid.ItemsPanelRoot is DesktopIconPanel panel ? panel.CellAt(PanelPoint(panel, x, y)) : new GridCell(0, 0);

    /// <summary>A point on the screen (physical pixels) on the icons' panel.</summary>
    private Point PanelPoint(DesktopIconPanel panel, int x, int y)
    {
        double scale = XamlRoot.RasterizationScale;
        var point = new Point((x - _monitor.Bounds.X) / scale, (y - _monitor.Bounds.Y) / scale);
        return Root.TransformToVisual(panel).TransformPoint(point);
    }

    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // A click on the desktop itself clears the selection, as in Explorer.
        if (!_dragged && IconAt(e.OriginalSource) is null)
            IconGrid.SelectedItems.Clear();
    }

    /// <summary>
    /// A press on an icon may start dragging it (and the rest of the selection); a press on the empty desktop starts
    /// a selection rectangle, which adds to the selection with Ctrl held.
    /// </summary>
    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(Root);
        _press = null;
        if (!point.Properties.IsLeftButtonPressed || IsOnScrollBar(e.OriginalSource))
            return;
        if (IconAt(e.OriginalSource) is { } icon)
        {
            _press = new IconPress(e.Pointer.PointerId, point.Position, icon);
            return;
        }

        _dragged = false;
        HashSet<DesktopIcon> kept = IsDown(VirtualKey.Control) ? [.. IconGrid.SelectedItems.Cast<DesktopIcon>()] : [];
        if (Root.CapturePointer(e.Pointer))
            _marquee = new MarqueeDrag(e.Pointer.PointerId, point.Position, kept);
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        Point position = e.GetCurrentPoint(Root).Position;
        if (_press is { } press && e.Pointer.PointerId == press.PointerId && IsDrag(press.Start, position))
        {
            _press = null;
            BeginDrag(press.Icon, e);
            return;
        }

        if (_marquee is not { } marquee || e.Pointer.PointerId != marquee.PointerId)
            return;
        if (!_dragged && !IsDrag(marquee.Start, position))
            return;

        _dragged = true;
        var rect = new Rect(marquee.Start, position);
        Canvas.SetLeft(Marquee, rect.X);
        Canvas.SetTop(Marquee, rect.Y);
        Marquee.Width = rect.Width;
        Marquee.Height = rect.Height;
        Marquee.Visibility = Visibility.Visible;

        foreach (DesktopIcon icon in _icons.Icons)
        {
            bool select = marquee.Kept.Contains(icon) || Intersects(icon, rect);
            if (select != IconGrid.SelectedItems.Contains(icon))
            {
                if (select)
                    IconGrid.SelectedItems.Add(icon);
                else
                    IconGrid.SelectedItems.Remove(icon);
            }
        }
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _press = null;
        if (_marquee is not { } marquee || e.Pointer.PointerId != marquee.PointerId)
            return;

        _marquee = null;
        Marquee.Visibility = Visibility.Collapsed;
        Root.ReleasePointerCapture(e.Pointer);
        // So the keys (Enter, Delete, Ctrl+C) act on what was just selected.
        if (_dragged)
            IconGrid.Focus(FocusState.Pointer);
    }

    private bool Intersects(DesktopIcon icon, Rect rect)
    {
        if (IconGrid.ContainerFromItem(icon) is not FrameworkElement container)
            return false;

        Rect bounds = container.TransformToVisual(Root).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        bounds.Intersect(rect);
        return !bounds.IsEmpty;
    }

    private static bool IsOnScrollBar(object source)
    {
        for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ScrollBar)
                return true;
        }
        return false;
    }

    /// <summary>A small wobble while clicking isn't a drag.</summary>
    private static bool IsDrag(Point start, Point position)
    {
        const double threshold = 4;
        return Math.Abs(position.X - start.X) >= threshold || Math.Abs(position.Y - start.Y) >= threshold;
    }

    private sealed record MarqueeDrag(uint PointerId, Point Start, HashSet<DesktopIcon> Kept);

    private sealed record IconPress(uint PointerId, Point Start, DesktopIcon Icon);

    /// <summary>
    /// The desktop's own icons being dragged, and the one under the pointer, with where on it the pointer is: let go,
    /// it goes to the cell nearest to where it was under the pointer, as in Explorer, and the others alongside.
    /// </summary>
    private sealed record OwnDrag(IReadOnlyList<DesktopIcon> Icons, DesktopIcon Grabbed, Point Grab);

    /// <summary>
    /// Lets files be dragged in from other apps onto the desktop and its icons; the shell handles the drop, as on
    /// Explorer's desktop. Native OLE drag and drop rather than WinUI's: the shell needs the drag's own data object.
    /// </summary>
    private void StartDragDrop()
    {
        if (_dragDrop is not null)
            return;
        try
        {
            _dragDrop = new DesktopDragDrop(_hwnd, (x, y) => IconAtScreen(x, y)?.Item);
            _dragDrop.TargetChanged += item =>
            {
                foreach (DesktopIcon icon in _icons.Icons)
                    icon.IsDropTarget = item is not null && icon.Item.ParsingName == item.ParsingName;
            };
            // The icons let go on the desktop itself move there, as do files dropped on it from elsewhere.
            _dragDrop.OwnItemsDropped += (x, y) =>
            {
                if (_ownDrag is not { } drag || IconGrid.ItemsPanelRoot is not DesktopIconPanel panel)
                    return;
                Point pointer = PanelPoint(panel, x, y);
                GridCell cell = panel.NearestCell(new Point(pointer.X - drag.Grab.X, pointer.Y - drag.Grab.Y));
                _icons.Move(drag.Icons, cell.Column - drag.Grabbed.Cell.Column, cell.Row - drag.Grabbed.Cell.Row, _rows, _columns);
                panel.InvalidateMeasure();
            };
            _dragDrop.ItemsDropped += (x, y) =>
            {
                _icons.ExpectNewItemsAt(CellAtScreen(x, y));
                _icons.QueueRefresh();
            };
        }
        catch (Exception ex)
        {
            Log.Error("Could not take drops on the desktop", ex);
        }
    }

    /// <summary>
    /// Drags the icon, with the rest of the selection if it's selected, out to wherever it's let go: another app, an
    /// Explorer window, a folder on the desktop. A drag of an icon that isn't selected selects it first, as in Explorer.
    /// WinUI's drag, with the files as storage items: OLE's own drag loop gets no mouse input in a WinUI app, which
    /// takes the mouse's input as pointer messages. System folders (This PC, the Recycle Bin) only move on the desktop.
    /// </summary>
    private async void BeginDrag(DesktopIcon icon, PointerRoutedEventArgs e)
    {
        if (IconGrid.ContainerFromItem(icon) is not UIElement container)
            return;

        if (!IconGrid.SelectedItems.Contains(icon))
        {
            IconGrid.SelectedItems.Clear();
            IconGrid.SelectedItems.Add(icon);
        }
        IReadOnlyList<DesktopItem> items = Selection;
        _ownDrag = new OwnDrag([.. IconGrid.SelectedItems.Cast<DesktopIcon>()], icon, e.GetCurrentPoint(container).Position);

        TypedEventHandler<UIElement, DragStartingEventArgs> starting = async (_, args) =>
        {
            DragOperationDeferral deferral = args.GetDeferral();
            try
            {
                args.AllowedOperations = DataPackageOperation.Copy | DataPackageOperation.Move | DataPackageOperation.Link;
                // System folders have none, and drag only within the desktop.
                if (await StorageItemsAsync(items) is { Count: > 0 } storageItems)
                    args.Data.SetStorageItems(storageItems);
            }
            catch (Exception ex)
            {
                // An async event handler: anything thrown here would end the app.
                Log.Error("Could not start dragging the desktop's items", ex);
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };
        container.DragStarting += starting;
        _dragDrop?.BeginOwnDrag(items);
        try
        {
            await container.StartDragAsync(e.GetCurrentPoint(container));
        }
        catch (OperationCanceledException)
        {
            // WinUI didn't start the drag; an exception here would end the app (async void).
        }
        finally
        {
            container.DragStarting -= starting;
            _dragDrop?.EndOwnDrag();
            _ownDrag = null;
            IconGrid.Focus(FocusState.Pointer);
        }
    }

    // The desktop's own icons dragged over the desktop: WinUI's drag reaches only WinUI's drop events in its own
    // process, so they're passed on to the shell's targets here. Drags from other apps go to the OLE target instead.

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (_dragDrop?.OwnItems is null)
            return;
        (int x, int y) = ToScreen(e.GetPosition(Root));
        e.AcceptedOperation = Operation(_dragDrop.OwnDragOver(x, y));
    }

    private void Root_DragLeave(object sender, DragEventArgs e) => _dragDrop?.OwnDragLeave();

    private void Root_Drop(object sender, DragEventArgs e)
    {
        if (_dragDrop?.OwnItems is null)
            return;
        (int x, int y) = ToScreen(e.GetPosition(Root));
        e.AcceptedOperation = Operation(_dragDrop.OwnDrop(x, y));
    }

    private static DataPackageOperation Operation(DropEffect effect) =>
        effect.HasFlag(DropEffect.Move) ? DataPackageOperation.Move
        : effect.HasFlag(DropEffect.Copy) ? DataPackageOperation.Copy
        : effect.HasFlag(DropEffect.Link) ? DataPackageOperation.Link
        : DataPackageOperation.None;

    /// <summary>
    /// The items' files and folders as storage items. The storage API won't open anything in a hidden folder, such as
    /// the shortcuts on the public Desktop: those go as copies (all a user can do with them in Explorer too), made in
    /// a folder of NeoShell's own. A streamed file in their place hangs WinUI when it's added to the drag.
    /// </summary>
    private static async Task<IReadOnlyList<IStorageItem>> StorageItemsAsync(IReadOnlyList<DesktopItem> items)
    {
        var result = new List<IStorageItem>();
        string? copies = null;
        foreach (string path in items.Select(item => item.Path).OfType<string>())
        {
            try
            {
                if (Directory.Exists(path))
                {
                    result.Add(await StorageFolder.GetFolderFromPathAsync(path));
                    continue;
                }
                try
                {
                    result.Add(await StorageFile.GetFileFromPathAsync(path));
                }
                catch (UnauthorizedAccessException)
                {
                    copies ??= NewDragCopiesFolder();
                    string copy = Path.Combine(copies, Path.GetFileName(path));
                    await Task.Run(() => File.Copy(path, copy));
                    result.Add(await StorageFile.GetFileFromPathAsync(copy));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Can't drag {path}: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>An empty folder for copies of dragged files; the last drag's copies are deleted.</summary>
    private static string NewDragCopiesFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "NeoShell", "Dragged");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>The icon at a point on the screen (physical pixels).</summary>
    private DesktopIcon? IconAtScreen(int x, int y)
    {
        if (XamlRoot is null)
            return null;
        double scale = XamlRoot.RasterizationScale;
        var point = new Point((x - _monitor.Bounds.X) / scale, (y - _monitor.Bounds.Y) / scale);
        return VisualTreeHelper.FindElementsInHostCoordinates(point, IconGrid)
            .OfType<GridViewItem>()
            .Select(container => container.Content as DesktopIcon)
            .FirstOrDefault(icon => icon is not null);
    }

    private void IconGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IconAt(e.OriginalSource) is { } icon)
            Open(IconGrid.SelectedItems.Contains(icon) ? Selection : [icon.Item]);
    }

    private void IconGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool control = IsDown(VirtualKey.Control);
        bool alt = IsDown(VirtualKey.Menu);
        IReadOnlyList<DesktopItem> selection = Selection;
        e.Handled = true;
        switch (e.Key)
        {
            case VirtualKey.Enter when alt && selection.Count > 0:
                Verb(selection, "properties");
                break;
            case VirtualKey.Enter when selection.Count > 0:
                Open(selection);
                break;
            case VirtualKey.Delete when selection.Count > 0:
                Verb(selection, "delete");
                break;
            case VirtualKey.F2 when IconGrid.SelectedItems.Count == 1:
                BeginRename((DesktopIcon)IconGrid.SelectedItems[0]);
                break;
            case VirtualKey.F5:
                _ = _icons.RefreshAsync();
                break;
            case VirtualKey.C when control && selection.Count > 0:
                Verb(selection, "copy");
                break;
            case VirtualKey.X when control && selection.Count > 0:
                Verb(selection, "cut");
                break;
            case VirtualKey.V when control:
                Paste("paste");
                break;
            case VirtualKey.Left:
                SelectNext(-1, 0);
                break;
            case VirtualKey.Right:
                SelectNext(1, 0);
                break;
            case VirtualKey.Up:
                SelectNext(0, -1);
                break;
            case VirtualKey.Down:
                SelectNext(0, 1);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    /// <summary>
    /// Selects the nearest icon in a direction across the grid from the focused one, keeping to its row or column
    /// where it can (the icons sit anywhere, so the list's own arrow keys, which go by order, won't do).
    /// </summary>
    private void SelectNext(int columns, int rows)
    {
        DesktopIcon? from = (FocusManager.GetFocusedElement(XamlRoot) as GridViewItem)?.Content as DesktopIcon
            ?? IconGrid.SelectedItems.LastOrDefault() as DesktopIcon;
        DesktopIcon? next = from is null
            ? _icons.Icons.OrderBy(icon => icon.Cell.Column).ThenBy(icon => icon.Cell.Row).FirstOrDefault()
            : _icons.Icons
                .Where(icon => columns != 0
                    ? Math.Sign(icon.Cell.Column - from.Cell.Column) == columns
                    : Math.Sign(icon.Cell.Row - from.Cell.Row) == rows)
                .OrderBy(icon =>
                {
                    int along = columns != 0 ? icon.Cell.Column - from.Cell.Column : icon.Cell.Row - from.Cell.Row;
                    int across = columns != 0 ? icon.Cell.Row - from.Cell.Row : icon.Cell.Column - from.Cell.Column;
                    return along * along + 4 * across * across;
                })
                .FirstOrDefault();
        if (next is null)
            return;

        Select(next);
        (IconGrid.ContainerFromItem(next) as Control)?.Focus(FocusState.Keyboard);
    }

    private void Root_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        DesktopIcon? icon = IconAt(e.OriginalSource);
        if (!e.TryGetPosition(Root, out Point point))
        {
            // From the keyboard (Menu key, Shift+F10): the menu is for the selection, under its first icon.
            icon = IconGrid.SelectedItems.FirstOrDefault() as DesktopIcon;
            point = icon is not null && IconGrid.ContainerFromItem(icon) is UIElement container
                ? container.TransformToVisual(Root).TransformPoint(new Point(24, 24))
                : new Point(24, 24);
        }

        if (icon is null)
        {
            IconGrid.SelectedItems.Clear();
        }
        else if (!IconGrid.SelectedItems.Contains(icon))
        {
            IconGrid.SelectedItems.Clear();
            IconGrid.SelectedItems.Add(icon);
        }

        MenuFlyout menu = icon is null ? BackgroundMenu(point) : ItemMenu(Selection, point);
        menu.ShowAt(Root, new FlyoutShowOptions { Position = point });
    }

    /// <summary>The shell's menu for the items: everything Explorer's menu has, "Show more options" included.</summary>
    private MenuFlyout ItemMenu(IReadOnlyList<DesktopItem> items, Point point)
    {
        var menu = new MenuFlyout();
        AddShellItems(menu, ShellMenu.ForItems(_hwnd, items), point, renameTarget: items[0]);
        return menu;
    }

    /// <summary>
    /// The desktop's own menu as Explorer's full one: its view, sort and paste commands, then the shell's (installed
    /// apps' commands, New, Display settings, Personalize).
    /// </summary>
    private MenuFlyout BackgroundMenu(Point point)
    {
        var view = new MenuFlyoutSubItem { Text = "View", Icon = Glyph("") };
        AutomationProperties.SetAutomationId(view, "DesktopViewMenuItem");
        view.Items.Add(SizeItem("Large icons", DesktopViewSettings.LargeIcons));
        view.Items.Add(SizeItem("Medium icons", DesktopViewSettings.MediumIcons));
        view.Items.Add(SizeItem("Small icons", DesktopViewSettings.SmallIcons));
        view.Items.Add(new MenuFlyoutSeparator());
        var autoArrange = new ToggleMenuFlyoutItem { Text = "Auto arrange icons", IsChecked = _icons.View.AutoArrange };
        AutomationProperties.SetAutomationId(autoArrange, "DesktopAutoArrangeMenuItem");
        autoArrange.Click += (_, _) => _icons.SetAutoArrange(autoArrange.IsChecked);
        view.Items.Add(autoArrange);
        var showIcons = new ToggleMenuFlyoutItem { Text = "Show desktop icons", IsChecked = _icons.View.ShowIcons };
        AutomationProperties.SetAutomationId(showIcons, "DesktopShowIconsMenuItem");
        showIcons.Click += (_, _) => _icons.SetShowIcons(showIcons.IsChecked);
        view.Items.Add(showIcons);

        var sort = new MenuFlyoutSubItem { Text = "Sort by", Icon = Glyph("") };
        AutomationProperties.SetAutomationId(sort, "DesktopSortMenuItem");
        sort.Items.Add(SortItem("Name", DesktopSortOrder.Name));
        sort.Items.Add(SortItem("Size", DesktopSortOrder.Size));
        sort.Items.Add(SortItem("Item type", DesktopSortOrder.ItemType));
        sort.Items.Add(SortItem("Date modified", DesktopSortOrder.DateModified));

        bool canPaste = ShellContextMenu.CanPaste();
        MenuFlyoutItem paste = MenuItem("Paste", "", "DesktopPasteMenuItem", () => Paste("paste"));
        MenuFlyoutItem pasteShortcut = MenuItem("Paste shortcut", "", "DesktopPasteShortcutMenuItem", () => Paste("pastelink"));
        paste.IsEnabled = pasteShortcut.IsEnabled = canPaste;

        MenuFlyout menu = Menu(
            [view, sort, MenuItem("Refresh", "", "DesktopRefreshMenuItem", () => _ = _icons.RefreshAsync())],
            [paste, pasteShortcut]);
        AddShellItems(menu, ShellMenu.ForBackground(_hwnd), point, renameTarget: null);
        return menu;
    }

    /// <summary>
    /// Adds the shell's menu in NeoShell's look. The shell runs the chosen command, except Rename (the inline box) and
    /// the Settings pages (<see cref="ClassicSettings"/>).
    /// </summary>
    private void AddShellItems(MenuFlyout menu, ShellMenu? shellMenu, Point point, DesktopItem? renameTarget)
    {
        if (shellMenu is null)
        {
            Log.Warn("The shell gave no context menu");
            return;
        }

        List<MenuFlyoutItemBase> items = ShellItems(shellMenu, shellMenu.Items, point, renameTarget);
        if (menu.Items.Count > 0 && items.Count > 0)
            menu.Items.Add(new MenuFlyoutSeparator());
        foreach (MenuFlyoutItemBase item in items)
            menu.Items.Add(item);
        // The chosen command runs after the menu has closed, so the handlers are released after that.
        menu.Closed += (_, _) => AfterMenuCloses(shellMenu.Dispose);
    }

    private List<MenuFlyoutItemBase> ShellItems(ShellMenu shellMenu, IReadOnlyList<ShellMenuItem> items, Point point, DesktopItem? renameTarget)
    {
        var result = new List<MenuFlyoutItemBase>();
        foreach (ShellMenuItem item in items)
        {
            if (item.IsSeparator)
            {
                if (result.Count > 0 && result[^1] is not MenuFlyoutSeparator)
                    result.Add(new MenuFlyoutSeparator());
                continue;
            }
            if (item.Text.Length == 0)
                continue;

            MenuFlyoutItemBase element;
            if (item.Items.Count > 0)
            {
                List<MenuFlyoutItemBase> children = ShellItems(shellMenu, item.Items, point, renameTarget);
                if (children.Count == 0)
                    continue;
                var submenu = new MenuFlyoutSubItem { Text = item.Text, Icon = ShellIcon(item), IsEnabled = item.IsEnabled };
                foreach (MenuFlyoutItemBase child in children)
                    submenu.Items.Add(child);
                element = submenu;
            }
            else
            {
                MenuFlyoutItem command = item.IsChecked ? new ToggleMenuFlyoutItem { IsChecked = true } : new MenuFlyoutItem();
                command.Text = item.Text;
                command.Icon = ShellIcon(item);
                command.IsEnabled = item.IsEnabled;
                if (item.IsDefault)
                    command.FontWeight = FontWeights.SemiBold;
                command.Click += (_, _) => AfterMenuCloses(() => RunShellCommand(shellMenu, item, point, renameTarget));
                element = command;
            }
            AutomationProperties.SetAutomationId(element, $"DesktopShellMenuItem_{item.Verb ?? item.Text}");
            result.Add(element);
        }
        if (result.Count > 0 && result[^1] is MenuFlyoutSeparator)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    /// <summary>The handler's own image, or one of NeoShell's glyphs for the shell's standard commands.</summary>
    private static IconElement? ShellIcon(ShellMenuItem item) =>
        item.Icon is { } icon ? new ImageIcon { Source = AppIcons.ToImageSource(icon) }
        : item.Verb is { } verb && VerbGlyphs.TryGetValue(verb, out string? glyph) ? Glyph(glyph)
        : null;

    private void RunShellCommand(ShellMenu shellMenu, ShellMenuItem item, Point point, DesktopItem? renameTarget)
    {
        string? verb = item.Verb;
        if (renameTarget is not null && string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase))
        {
            BeginRename(_icons.Find(renameTarget.ParsingName));
            return;
        }
        if (verb is not null && ClassicSettings.TryGetValue(verb, out string? arguments))
        {
            Launcher.Launch(new PinnedApp(item.Text, Path: "rundll32.exe", Arguments: arguments));
            return;
        }

        // New's commands (a folder, ".txt"...) create an item that the user names next, as in Explorer.
        bool creates = string.Equals(verb, "NewFolder", StringComparison.OrdinalIgnoreCase) || verb?.StartsWith('.') == true;
        HashSet<string> before = creates ? [.. _icons.Icons.Select(icon => icon.Item.ParsingName)] : [];

        (int x, int y) = ToScreen(point);
        if (creates)
            _icons.ExpectNewItemsAt(CellAtScreen(x, y));
        if (!shellMenu.Invoke(item, x, y))
            Log.Warn($"The {verb ?? item.Text} command failed");
        else if (creates)
            RenameNewItem(before);
        else if (verb is not null && InPlaceVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase))
            KeepForeground();
    }

    private async void RenameNewItem(HashSet<string> before)
    {
        await _icons.RefreshAsync();
        if (_icons.Icons.FirstOrDefault(icon => !before.Contains(icon.Item.ParsingName)) is { } created)
        {
            Select(created);
            BeginRename(created);
        }
    }

    private RadioMenuFlyoutItem SizeItem(string text, int size)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = "IconSize", IsChecked = _icons.View.IconSize == size };
        AutomationProperties.SetAutomationId(item, $"DesktopSize{size}MenuItem");
        item.Click += (_, _) => _icons.SetIconSize(size);
        return item;
    }

    private RadioMenuFlyoutItem SortItem(string text, DesktopSortOrder order)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = "SortOrder", IsChecked = _icons.SortOrder == order };
        AutomationProperties.SetAutomationId(item, $"DesktopSort{order}MenuItem");
        item.Click += (_, _) => _icons.SetSortOrder(order);
        return item;
    }

    /// <summary>A menu of groups divided by separators; null items and empty groups are left out.</summary>
    private static MenuFlyout Menu(params MenuFlyoutItemBase?[][] groups)
    {
        var menu = new MenuFlyout();
        foreach (MenuFlyoutItemBase[] group in groups.Select(group => group.OfType<MenuFlyoutItemBase>().ToArray()).Where(group => group.Length > 0))
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new MenuFlyoutSeparator());
            foreach (MenuFlyoutItemBase item in group)
                menu.Items.Add(item);
        }
        return menu;
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, string automationId, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = Glyph(glyph) };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => action();
        return item;
    }

    private static FontIcon Glyph(string glyph) => new() { Glyph = glyph };

    private void Open(IReadOnlyList<DesktopItem> items)
    {
        if (!ShellContextMenu.InvokeDefault(_hwnd, items))
            Log.Warn($"Could not open {string.Join(", ", items.Select(item => item.ParsingName))}");
    }

    private void Verb(IReadOnlyList<DesktopItem> items, string verb)
    {
        if (!ShellContextMenu.InvokeVerb(_hwnd, items, verb))
            Log.Warn($"The {verb} command failed for {string.Join(", ", items.Select(item => item.ParsingName))}");
        else if (InPlaceVerbs.Contains(verb))
            KeepForeground();
    }

    private void Paste(string verb)
    {
        if (!ShellContextMenu.CanPaste())
            return;
        if (ShellContextMenu.InvokeBackgroundVerb(_hwnd, verb))
            KeepForeground();
        else
            Log.Warn($"The {verb} command failed on the desktop");
    }

    /// <summary>
    /// Takes the foreground back after a file operation. The shell's operation windows hand it on when they close,
    /// to the next window in the z-order, which is never the desktop: it's at the bottom.
    /// </summary>
    private void KeepForeground() => TopLevelWindows.Activate(_hwnd);

    /// <summary>Edits the icon's name in a box over its label; Enter or clicking elsewhere renames, Esc cancels.</summary>
    private void BeginRename(DesktopIcon? icon)
    {
        if (icon is null || !icon.Item.CanRename)
            return;

        IconGrid.ScrollIntoView(icon);
        IconGrid.UpdateLayout();
        if (IconGrid.ContainerFromItem(icon) is not FrameworkElement container)
            return;

        DesktopItem item = icon.Item;
        var box = new TextBox
        {
            Text = item.EditName,
            Width = Math.Max(120, container.ActualWidth + 24),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(box, "DesktopRenameBox");
        var flyout = new Flyout { Content = box, FlyoutPresenterStyle = (Style)Resources["RenameFlyoutStyle"] };
        bool cancelled = false;
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is VirtualKey.Enter or VirtualKey.Escape)
            {
                e.Handled = true;
                cancelled = e.Key == VirtualKey.Escape;
                flyout.Hide();
            }
        };
        flyout.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            // Like Explorer, select the name without its extension.
            int dot = item.IsFolder ? -1 : box.Text.LastIndexOf('.');
            box.Select(0, dot > 0 ? dot : box.Text.Length);
        };
        flyout.Closed += (_, _) =>
        {
            string name = box.Text.Trim();
            if (!cancelled && name.Length > 0 && name != item.EditName)
                Rename(item, name);
            container.Focus(FocusState.Programmatic);
        };
        // Over the label, which starts under the icon.
        flyout.ShowAt(container, new FlyoutShowOptions
        {
            Position = new Point(container.ActualWidth / 2, icon.Size + 4),
            Placement = FlyoutPlacementMode.Bottom,
        });
    }

    private async void Rename(DesktopItem item, string name)
    {
        if (DesktopFolder.Rename(_hwnd, item, name) is not { } renamed)
            return;
        _icons.Renamed(item.ParsingName, renamed);

        await _icons.RefreshAsync();
        if (_icons.Find(renamed) is { } icon)
            Select(icon);
    }

    private void Select(DesktopIcon icon)
    {
        IconGrid.SelectedItems.Clear();
        IconGrid.SelectedItems.Add(icon);
        IconGrid.ScrollIntoView(icon);
    }

    /// <summary>
    /// Runs an action once the menu it was chosen from has closed: the shell's menus run their own message loop, which
    /// would keep the WinUI menu open behind them.
    /// </summary>
    private void AfterMenuCloses(Action action) => DispatcherQueue.Post(DispatcherQueuePriority.Low, action);

    /// <summary>A point on this view in screen coordinates (physical pixels), for the shell's menus and drop targets.</summary>
    private (int X, int Y) ToScreen(Point point)
    {
        double scale = XamlRoot.RasterizationScale;
        return (_monitor.Bounds.X + (int)(point.X * scale), _monitor.Bounds.Y + (int)(point.Y * scale));
    }

    private static DesktopIcon? IconAt(object source) => (source as FrameworkElement)?.DataContext as DesktopIcon;

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
