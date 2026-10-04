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
using Windows.Foundation;
using Windows.Graphics;
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

    /// <summary>Verbs that work on the files without opening a window.</summary>
    private static readonly string[] InPlaceVerbs = ["cut", "copy", "delete", "copyaspath"];

    private readonly DesktopIcons _icons;
    private readonly nint _hwnd;
    private readonly DisplayMonitor _monitor;
    private MarqueeDrag? _marquee;
    private bool _dragged;

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
        };
        Unloaded += (_, _) => icons.Refreshed -= Apply;
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
        RequestedTheme = SystemTheme.Read();
        IconGrid.Visibility = _icons.View.ShowIcons ? Visibility.Visible : Visibility.Collapsed;

        if (IconGrid.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            int size = _icons.View.IconSize;
            panel.ItemWidth = Math.Max(80, size + 36);
            panel.ItemHeight = size + 52;
        }

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
    }

    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // A click on the desktop itself clears the selection, as in Explorer.
        if (!_dragged && IconAt(e.OriginalSource) is null)
            IconGrid.SelectedItems.Clear();
    }

    /// <summary>A press on the empty desktop starts a selection rectangle; with Ctrl it adds to the selection.</summary>
    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed || IconAt(e.OriginalSource) is not null || IsOnScrollBar(e.OriginalSource))
            return;

        _dragged = false;
        HashSet<DesktopIcon> kept = IsDown(VirtualKey.Control) ? [.. IconGrid.SelectedItems.Cast<DesktopIcon>()] : [];
        if (Root.CapturePointer(e.Pointer))
            _marquee = new MarqueeDrag(e.Pointer.PointerId, point.Position, kept);
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_marquee is not { } marquee || e.Pointer.PointerId != marquee.PointerId)
            return;

        // A small wobble while clicking isn't a drag.
        const double threshold = 4;
        Point position = e.GetCurrentPoint(Root).Position;
        if (!_dragged && Math.Abs(position.X - marquee.Start.X) < threshold && Math.Abs(position.Y - marquee.Start.Y) < threshold)
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

    private sealed record MarqueeDrag(uint PointerId, Point Start, HashSet<DesktopIcon> Kept);

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
            default:
                e.Handled = false;
                break;
        }
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

    /// <summary>Windows 11's short menu for icons; "Show more options" opens the shell's full one.</summary>
    private MenuFlyout ItemMenu(IReadOnlyList<DesktopItem> items, Point point)
    {
        DesktopItem first = items[0];
        bool single = items.Count == 1;
        bool runnable = single && first.Path is { } path && RunnableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        MenuFlyoutItem open = MenuItem("Open", "", "DesktopOpenMenuItem", () => Open(items));
        open.FontWeight = FontWeights.SemiBold;

        return Menu(
            [
                open,
                runnable ? MenuItem("Run as administrator", "", "DesktopRunAsMenuItem", () => Verb(items, "runas")) : null,
                single && !first.IsFolder && !first.IsLink && first.Path is not null
                    ? MenuItem("Open with…", "", "DesktopOpenWithMenuItem", () => Verb(items, "openas"))
                    : null,
                single && DesktopContents.IsRecycleBin(first)
                    ? MenuItem("Empty Recycle Bin", "", "DesktopEmptyRecycleBinMenuItem", () => ShellContextMenu.EmptyRecycleBin(_hwnd))
                    : null,
            ],
            [
                items.All(item => item.CanMove) ? MenuItem("Cut", "", "DesktopCutMenuItem", () => Verb(items, "cut")) : null,
                items.All(item => item.CanCopy) ? MenuItem("Copy", "", "DesktopCopyMenuItem", () => Verb(items, "copy")) : null,
                single && first.CanRename
                    ? MenuItem("Rename", "", "DesktopRenameMenuItem", () => BeginRename(_icons.Find(first.ParsingName)))
                    : null,
                items.All(item => item.CanDelete) ? MenuItem("Delete", "", "DesktopDeleteMenuItem", () => Verb(items, "delete")) : null,
            ],
            [
                items.All(item => item.Path is not null)
                    ? MenuItem("Copy as path", "", "DesktopCopyPathMenuItem", () => Verb(items, "copyaspath"))
                    : null,
                items.All(item => item.HasProperties)
                    ? MenuItem("Properties", "", "DesktopPropertiesMenuItem", () => Verb(items, "properties"))
                    : null,
            ],
            [
                MenuItem("Show more options", "", "DesktopMoreOptionsMenuItem", () => AfterMenuCloses(() =>
                {
                    (int x, int y) = ToScreen(point);
                    if (ShellContextMenu.Show(_hwnd, items, x, y))
                        BeginRename(_icons.Find(first.ParsingName));
                })),
            ]);
    }

    /// <summary>The desktop's own menu: Explorer's view and sort choices, paste and new items.</summary>
    private MenuFlyout BackgroundMenu(Point point)
    {
        var view = new MenuFlyoutSubItem { Text = "View", Icon = Glyph("") };
        AutomationProperties.SetAutomationId(view, "DesktopViewMenuItem");
        view.Items.Add(SizeItem("Large icons", DesktopViewSettings.LargeIcons));
        view.Items.Add(SizeItem("Medium icons", DesktopViewSettings.MediumIcons));
        view.Items.Add(SizeItem("Small icons", DesktopViewSettings.SmallIcons));
        view.Items.Add(new MenuFlyoutSeparator());
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

        var create = new MenuFlyoutSubItem { Text = "New", Icon = Glyph("") };
        AutomationProperties.SetAutomationId(create, "DesktopNewMenuItem");
        create.Items.Add(MenuItem("Folder", "", "DesktopNewFolderMenuItem", () => CreateNew("New folder", "", isFolder: true)));
        create.Items.Add(MenuItem("Text Document", "", "DesktopNewTextMenuItem", () => CreateNew("New Text Document", ".txt", isFolder: false)));

        bool canPaste = ShellContextMenu.CanPaste();
        MenuFlyoutItem paste = MenuItem("Paste", "", "DesktopPasteMenuItem", () => Paste("paste"));
        MenuFlyoutItem pasteShortcut = MenuItem("Paste shortcut", "", "DesktopPasteShortcutMenuItem", () => Paste("pastelink"));
        paste.IsEnabled = pasteShortcut.IsEnabled = canPaste;

        return Menu(
            [view, sort, MenuItem("Refresh", "", "DesktopRefreshMenuItem", () => _ = _icons.RefreshAsync())],
            [paste, pasteShortcut, create],
            [
                // Explorer's Personalize and Display settings open the Settings app, which can't run without Explorer.
                MenuItem("Desktop icon settings", "", "DesktopIconSettingsMenuItem", () => Launcher.Launch(
                    new PinnedApp("Desktop icon settings", Path: "rundll32.exe", Arguments: "shell32.dll,Control_RunDLL desk.cpl,,0"))),
            ],
            [
                MenuItem("Show more options", "", "DesktopMoreOptionsMenuItem", () => AfterMenuCloses(() =>
                {
                    (int x, int y) = ToScreen(point);
                    ShellContextMenu.ShowBackground(_hwnd, x, y);
                })),
            ]);
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

    /// <summary>Creates a new folder or empty file on the user's desktop and lets the user name it, as Explorer does.</summary>
    private async void CreateNew(string baseName, string extension, bool isFolder)
    {
        string folder = DesktopLocations.Current().UserDesktop;
        string path = Path.Combine(folder, DesktopContents.NewItemName(baseName, extension, name => Path.Exists(Path.Combine(folder, name))));
        try
        {
            if (isFolder)
                Directory.CreateDirectory(path);
            else
                File.Create(path).Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not create {path}", ex);
            return;
        }

        await _icons.RefreshAsync();
        if (_icons.Find(path) is { } icon)
        {
            Select(icon);
            BeginRename(icon);
        }
    }

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

    /// <summary>A point on this view in screen coordinates (physical pixels), for the shell's own menus.</summary>
    private (int X, int Y) ToScreen(Point point)
    {
        double scale = XamlRoot.RasterizationScale;
        return (_monitor.Bounds.X + (int)(point.X * scale), _monitor.Bounds.Y + (int)(point.Y * scale));
    }

    private static DesktopIcon? IconAt(object source) => (source as FrameworkElement)?.DataContext as DesktopIcon;

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
