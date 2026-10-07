using System.Collections.ObjectModel;
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
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
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
    private readonly ObservableCollection<DesktopIcon> _list;
    private MarqueeDrag? _marquee;
    private bool _dragged;
    private IconPress? _press;
    private DesktopDragDrop? _dragDrop;
    /// <summary>The grid's selection is being brought in line with the shared one: not the user's doing.</summary>
    private bool _syncing;

    /// <param name="hwnd">The wallpaper window: owner of the shell's menus and dialogs.</param>
    /// <param name="monitor">The monitor this view shows the icons of.</param>
    public DesktopIconsView(DesktopIcons icons, nint hwnd, DisplayMonitor monitor)
    {
        _icons = icons;
        _hwnd = hwnd;
        _monitor = monitor;
        _list = icons.IconsOn(monitor.Handle);
        InitializeComponent();

        IconGrid.ItemsSource = _list;
        Loaded += (_, _) =>
        {
            icons.Refreshed += Apply;
            icons.Arranged += Apply;
            icons.SelectionChanged += SyncSelection;
            icons.MarqueeChanged += ShowMarquee;
            icons.FocusRequested += FocusIcon;
            icons.OwnDragChanged += FollowOwnDrag;
            Apply();
            icons.QueueRefresh();
            StartDragDrop();
        };
        Unloaded += (_, _) =>
        {
            icons.Refreshed -= Apply;
            icons.Arranged -= Apply;
            icons.SelectionChanged -= SyncSelection;
            icons.MarqueeChanged -= ShowMarquee;
            icons.FocusRequested -= FocusIcon;
            icons.OwnDragChanged -= FollowOwnDrag;
            _dragDrop?.Dispose();
            _dragDrop = null;
        };
        // Handled events too: the grid's scroll viewer takes presses on the empty space between and around icons.
        Root.AddHandler(PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), handledEventsToo: true);
        Root.AddHandler(PointerMovedEvent, new PointerEventHandler(Root_PointerMoved), handledEventsToo: true);
        Root.AddHandler(PointerReleasedEvent, new PointerEventHandler(Root_PointerReleased), handledEventsToo: true);
        Root.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Root_PointerReleased), handledEventsToo: true);
    }

    /// <summary>The selected items, on every monitor.</summary>
    private IReadOnlyList<DesktopItem> Selection => [.. _icons.Selection.Select(icon => icon.Item)];

    /// <summary>
    /// Applies the view settings, the system theme (for the menus), and this monitor's grid: its cells, from the top
    /// left corner of its work area (which keeps clear of the taskbar), as Explorer lays them out.
    /// </summary>
    private void Apply()
    {
        RequestedTheme = SystemTheme.Read();
        IconGrid.Visibility = _icons.View.ShowIcons ? Visibility.Visible : Visibility.Collapsed;

        if (IconGrid.ItemsPanelRoot is not DesktopIconPanel panel)
            return;
        if (XamlRoot is not null && _icons.WorkspaceOf(_monitor.Handle) is { } workspace)
        {
            (double width, double height) = workspace.CellSize;
            panel.ItemSize = new Size(width, height);
            double scale = XamlRoot.RasterizationScale;
            RectInt32 bounds = _monitor.Bounds;
            RectInt32 work = workspace.WorkArea;
            IconGrid.Margin = new Thickness(
                (work.X - bounds.X) / scale,
                (work.Y - bounds.Y) / scale,
                Math.Max(0, bounds.X + bounds.Width - work.X - work.Width) / scale,
                Math.Max(0, bounds.Y + bounds.Height - work.Y - work.Height) / scale);
        }
        panel.InvalidateMeasure();
        SyncSelection();
    }

    /// <summary>Shows the shared selection on this monitor's icons.</summary>
    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            foreach (DesktopIcon icon in _list)
            {
                if (icon.IsSelected == IconGrid.SelectedItems.Contains(icon))
                    continue;
                if (icon.IsSelected)
                    IconGrid.SelectedItems.Add(icon);
                else
                    IconGrid.SelectedItems.Remove(icon);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// The user selected icons here: the shared selection follows. A plain click (no Ctrl or Shift) selects only
    /// here, so the other monitors' icons are deselected, as in Explorer's one desktop window.
    /// </summary>
    private void IconGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
            return;
        foreach (DesktopIcon icon in e.AddedItems.OfType<DesktopIcon>())
            icon.IsSelected = true;
        // An icon that moved to another monitor leaves this list, and its selection with it: it stays selected.
        foreach (DesktopIcon icon in e.RemovedItems.OfType<DesktopIcon>().Where(_list.Contains))
            icon.IsSelected = false;
        if (e.AddedItems.Count > 0 && !IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Shift))
            _icons.SelectOnly(IconGrid.SelectedItems.Cast<DesktopIcon>());
    }

    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // A click on the desktop itself clears the selection, as in Explorer.
        if (!_dragged && IconAt(e.OriginalSource) is null)
            _icons.ClearSelection();
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
        HashSet<DesktopIcon> kept = IsDown(VirtualKey.Control) ? [.. _icons.Selection] : [];
        if (Root.CapturePointer(e.Pointer))
            _marquee = new MarqueeDrag(e.Pointer.PointerId, point.Position, kept);
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        Point position = e.GetCurrentPoint(Root).Position;
        if (_press is { } press && e.Pointer.PointerId == press.PointerId && IsDrag(press.Start, position))
        {
            _press = null;
            BeginDrag(press, e);
            return;
        }

        if (_marquee is not { } marquee || e.Pointer.PointerId != marquee.PointerId)
            return;
        if (!_dragged && !IsDrag(marquee.Start, position))
            return;

        // The rectangle goes on across monitors (the pointer is captured): every monitor's view draws its part.
        _dragged = true;
        (int x1, int y1) = ToScreen(marquee.Start);
        (int x2, int y2) = ToScreen(position);
        var rect = new RectInt32(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
        _icons.SetMarquee(rect, marquee.Kept);
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _press = null;
        if (_marquee is not { } marquee || e.Pointer.PointerId != marquee.PointerId)
            return;

        _marquee = null;
        _icons.SetMarquee(null, marquee.Kept);
        Root.ReleasePointerCapture(e.Pointer);
        // So the keys (Enter, Delete, Ctrl+C) act on what was just selected.
        if (_dragged)
            IconGrid.Focus(FocusState.Pointer);
    }

    /// <summary>Draws this monitor's part of a selection rectangle and selects the icons here that it touches.</summary>
    private void ShowMarquee(RectInt32? screenRect, IReadOnlySet<DesktopIcon> kept)
    {
        if (screenRect is not { } screen || XamlRoot is null)
        {
            Marquee.Visibility = Visibility.Collapsed;
            return;
        }

        double scale = XamlRoot.RasterizationScale;
        var rect = new Rect((screen.X - _monitor.Bounds.X) / scale, (screen.Y - _monitor.Bounds.Y) / scale, screen.Width / scale, screen.Height / scale);
        Canvas.SetLeft(Marquee, rect.X);
        Canvas.SetTop(Marquee, rect.Y);
        Marquee.Width = rect.Width;
        Marquee.Height = rect.Height;
        Marquee.Visibility = Visibility.Visible;

        foreach (DesktopIcon icon in _list)
            icon.IsSelected = kept.Contains(icon) || Intersects(icon, rect);
        SyncSelection();
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
                foreach (DesktopIcon icon in _list)
                    icon.IsDropTarget = item is not null && icon.Item.ParsingName == item.ParsingName;
            };
            // The icons let go on the desktop itself move by as much as the pointer did, to whichever monitor that
            // is; files dropped on it from elsewhere go to the cell under the pointer.
            _dragDrop.OwnItemsDropped += (x, y) =>
            {
                if (_icons.OwnDrag is { } drag)
                    _icons.MoveBy(drag.Icons, x - drag.Start.X, y - drag.Start.Y);
            };
            _dragDrop.ItemsDropped += (x, y) =>
            {
                if (_icons.PlaceAt(x, y) is { } place)
                    _icons.ExpectNewItemsAt(place);
                _icons.QueueRefresh();
            };
            FollowOwnDrag();
        }
        catch (Exception ex)
        {
            Log.Error("Could not take drops on the desktop", ex);
        }
    }

    /// <summary>A drag of the desktop's own icons may come over this monitor from another: its drops go to them too.</summary>
    private void FollowOwnDrag()
    {
        if (_icons.OwnDrag is { } drag)
            _dragDrop?.BeginOwnDrag([.. drag.Icons.Select(icon => icon.Item)]);
        else
            _dragDrop?.EndOwnDrag();
    }

    /// <summary>
    /// Drags the icon, with the rest of the selection if it's selected, out to wherever it's let go: another app, an
    /// Explorer window, a folder on the desktop, another monitor. A drag of an icon that isn't selected selects it
    /// first, as in Explorer. WinUI's drag, with the files as storage items: OLE's own drag loop gets no mouse input
    /// in a WinUI app, which takes the mouse's input as pointer messages. System folders (This PC, the Recycle Bin)
    /// only move on the desktop.
    /// </summary>
    private async void BeginDrag(IconPress press, PointerRoutedEventArgs e)
    {
        DesktopIcon icon = press.Icon;
        if (IconGrid.ContainerFromItem(icon) is not UIElement container)
            return;

        if (!icon.IsSelected)
            _icons.SelectOnly([icon]);
        IReadOnlyList<DesktopItem> items = Selection;
        // From where the button went down: the pointer has moved a little since.
        (int x, int y) = ToScreen(press.Start);
        _icons.BeginOwnDrag(new OwnDrag([.. _icons.Selection], icon, new PointInt32(x, y)));

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
            _icons.EndOwnDrag();
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
            Open(icon.IsSelected ? Selection : [icon.Item]);
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
            case VirtualKey.F2 when selection.Count == 1:
                BeginRename(_icons.Selection.First());
                break;
            case VirtualKey.A when control:
                _icons.SelectAll();
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
                _icons.SelectNext(FocusedIcon(), -1, 0);
                break;
            case VirtualKey.Right:
                _icons.SelectNext(FocusedIcon(), 1, 0);
                break;
            case VirtualKey.Up:
                _icons.SelectNext(FocusedIcon(), 0, -1);
                break;
            case VirtualKey.Down:
                _icons.SelectNext(FocusedIcon(), 0, 1);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    /// <summary>The icon with the keyboard focus, or the last selected one.</summary>
    private DesktopIcon? FocusedIcon() =>
        (FocusManager.GetFocusedElement(XamlRoot) as GridViewItem)?.Content as DesktopIcon ?? _icons.Selection.LastOrDefault();

    /// <summary>The keyboard moved to an icon (maybe from another monitor): if it's here, this view takes the focus.</summary>
    private void FocusIcon(DesktopIcon icon)
    {
        if (!_list.Contains(icon))
            return;
        // The arrow keys went to another monitor's window; Explorer's one desktop window has them all.
        if (FocusManager.GetFocusedElement(XamlRoot) is null)
            TopLevelWindows.Activate(_hwnd);
        IconGrid.ScrollIntoView(icon);
        IconGrid.UpdateLayout();
        (IconGrid.ContainerFromItem(icon) as Control)?.Focus(FocusState.Keyboard);
    }

    private void Root_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        DesktopIcon? icon = IconAt(e.OriginalSource);
        if (!e.TryGetPosition(Root, out Point point))
        {
            // From the keyboard (Menu key, Shift+F10): the menu is for the selection, under its first icon here.
            icon = _list.FirstOrDefault(icon => icon.IsSelected);
            point = icon is not null && IconGrid.ContainerFromItem(icon) is UIElement container
                ? container.TransformToVisual(Root).TransformPoint(new Point(24, 24))
                : new Point(24, 24);
        }

        if (icon is null)
            _icons.ClearSelection();
        else if (!icon.IsSelected)
            _icons.SelectOnly([icon]);

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
        if (creates && _icons.PlaceAt(x, y) is { } place)
            _icons.ExpectNewItemsAt(place);
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
        _icons.SelectOnly([icon]);
        if (_list.Contains(icon))
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
