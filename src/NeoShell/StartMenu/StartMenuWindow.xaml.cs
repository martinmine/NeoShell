using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Principal;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using NeoShell.Interop.Search;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.Taskbar;
using System.Numerics;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Windows.System;

namespace NeoShell.StartMenu;

/// <summary>Items under one heading: a letter in All apps, a kind of result in search.</summary>
internal sealed class StartGroup(string key, IEnumerable<StartItem> items) : List<StartItem>(items)
{
    public string Key { get; } = key;
}

/// <summary>
/// The Start menu: pinned and recent apps, All apps, search over apps and the Windows Search index, and the user, Settings,
/// Switch to Explorer and power buttons. Created once and shown above whichever taskbar opened it.
/// </summary>
internal sealed partial class StartMenuWindow : Window
{
    private const int MaxRecentApps = 6;
    private const int MaxAppResults = 8;
    private const int MaxFileResults = 20;
    private static readonly TimeSpan s_searchDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan s_catalogLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan s_openDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan s_closeDuration = TimeSpan.FromMilliseconds(150);

    private readonly Taskbars _owner;
    private readonly nint _hwnd;
    private readonly ShellBackdrop _backdrop = new(Backdrop.Acrylic);
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly ObservableCollection<StartItem> _pinned = [];
    private readonly ObservableCollection<StartItem> _recent = [];
    private readonly CollectionViewSource _allApps = new() { IsSourceGrouped = true };
    private readonly CollectionViewSource _results = new() { IsSourceGrouped = true };
    private readonly ObservableCollection<StartGroup> _resultGroups = [];
    private List<StartItem> _apps = [];
    private List<StartItem> _resultItems = [];
    private DateTime _appsLoadedAt = DateTime.MinValue;
    private nint _previousForeground;
    private long _deactivatedAt;
    private bool _loadingApps;
    private bool _importingPins;
    private CancellationTokenSource? _search;
    private (DisplayMonitor Monitor, RectInt32 Taskbar, bool Centered) _anchor;
    private ResizeDrag? _resize;
    private (uint PointerId, Point Start, StartItem Item)? _pinPressed;
    private PinDrag? _pinDrag;
    private bool _suppressPinClick;
    private UIElement? _pressedIcon;
    private readonly WindowSlide _slide;
    private bool _closing;

    public StartMenuWindow(Taskbars owner)
    {
        _owner = owner;
        InitializeComponent();
        SystemBackdrop = _backdrop;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        // Out of Alt+Tab; unlike the taskbar it takes focus, for the search box.
        WindowStyles.AddExtended(_hwnd, ExtendedWindowStyles.ToolWindow);
        _frameless = new FramelessWindow(_hwnd, roundedCorners: true);
        _placement = new PinnedWindow(_hwnd, default, PinnedLayer.Topmost);
        _slide = new WindowSlide(_placement);

        PinnedGrid.ItemsSource = _pinned;
        // Handled events too: the item under the pointer takes the press for its click.
        PinnedGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PinnedGrid_PointerPressed), handledEventsToo: true);
        PinnedGrid.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PinnedGrid_PointerMoved), handledEventsToo: true);
        PinnedGrid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PinnedGrid_PointerReleased), handledEventsToo: true);
        PinnedGrid.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PinnedGrid_PointerCaptureLost), handledEventsToo: true);
        foreach (GridView grid in (GridView[])[PinnedGrid, RecentGrid])
        {
            grid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(GridItem_PointerPressed), handledEventsToo: true);
            grid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(GridItem_PointerReleased), handledEventsToo: true);
            grid.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(GridItem_PointerReleased), handledEventsToo: true);
        }
        IconPress.Attach(AllAppsButton, (UIElement)AllAppsButton.Content);
        RecentGrid.ItemsSource = _recent;
        _results.Source = _resultGroups;
        ResultsList.ItemsSource = _results.View;
        SwitchToExplorerButton.Visibility = owner.RunMode == RunMode.Shell ? Visibility.Visible : Visibility.Collapsed;
        ShowUser();

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && IsOpen)
            {
                _deactivatedAt = Environment.TickCount64;
                // Another window is taking the foreground; handing it back would take it away again.
                Hide(restoreForeground: false);
            }
        };
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Root_KeyDown), handledEventsToo: true);
        Root.CharacterReceived += Root_CharacterReceived;
        owner.Icons.Loaded += RefreshIcons;
        Closed += (_, _) =>
        {
            _slide.Stop();
            owner.Icons.Loaded -= RefreshIcons;
            _placement.Dispose();
            _frameless.Dispose();
        };

        LoadAppsIfStale();
    }

    public bool IsOpen { get; private set; }

    private IReadOnlyList<PinnedApp> PinnedStartApps => _owner.Settings.Current.PinnedStartApps;

    /// <summary>
    /// Pressing the Start button deactivates Start before the button's click arrives; that click must not reopen it.
    /// </summary>
    public bool WasJustDeactivated => Environment.TickCount64 - _deactivatedAt < 400;

    /// <summary>
    /// Opens above <paramref name="taskbar"/>: centred on the monitor, or at its left like the taskbar items. It flies
    /// out from behind <paramref name="taskbarWindow"/>.
    /// </summary>
    /// <param name="accent">Start's colour when Windows shows the accent colour on Start and taskbar.</param>
    public void Show(DisplayMonitor monitor, RectInt32 taskbar, nint taskbarWindow, bool centered, ElementTheme theme, Color? accent)
    {
        // Opened again while still flying in: from where it is, as it was.
        bool closing = _closing;
        _closing = false;
        if (closing)
            ResetContent();
        Root.RequestedTheme = accent is { } color ? SystemTheme.ThemeOn(color) : theme;
        _backdrop.Theme = Root.RequestedTheme;
        _backdrop.Tint = accent;
        ShowPinned();
        ShowRecent();
        LoadAppsIfStale();

        _anchor = (monitor, taskbar, centered);
        ShellSettings settings = _owner.Settings.Current;
        RectInt32 shown = BoundsFor(settings.StartMenuWidth, settings.StartMenuHeight);
        // Just below the taskbar in the topmost band, so the taskbar covers it while it slides up from behind.
        _placement.SetLayer(PinnedLayer.Topmost, above: taskbarWindow);
        _placement.Bounds = closing ? shown with { Y = _placement.Bounds.Y } : shown with { Y = taskbar.Y };
        // A left-aligned Start keeps its left edge by the Start button.
        LeftGrip.Visibility = centered ? Visibility.Visible : Visibility.Collapsed;

        IsOpen = true;
        _previousForeground = TopLevelWindows.GetForeground();
        AppWindow.Show();
        // Out of the taskbar, decelerating, as in Windows 11.
        _slide.To(shown, s_openDuration, decelerate: true);
        // Window.Activate alone doesn't take the foreground from the app the user was in; SetForegroundWindow does,
        // because the click on the taskbar (or the Win key) was the last input.
        TopLevelWindows.Activate(_hwnd);
        Activate();
        // Focus once the window is active, so typing goes straight into the search box.
        DispatcherQueue.Post(() => SearchBox.Focus(FocusState.Programmatic));
    }

    public void Hide() => Hide(restoreForeground: true);

    private RectInt32 BoundsFor(double width, double height) =>
        StartMenuLayout.Bounds(_anchor.Monitor.Bounds, _anchor.Taskbar, _anchor.Centered, width, height, _anchor.Monitor.Dpi / 96.0);

    private void Place(double width, double height) => _placement.Bounds = BoundsFor(width, height);

    // The window moves under the pointer while it's resized, so the drag is followed in screen pixels.
    private void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var grip = (ResizeGrip)sender;
        if (e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed && grip.CapturePointer(e.Pointer))
        {
            _resize = new ResizeDrag(e.Pointer.PointerId, Cursor.Position(), _placement.Bounds, grip.IsLeft);
            e.Handled = true;
        }
    }

    private void Grip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_resize is not { } drag || e.Pointer.PointerId != drag.PointerId)
            return;

        PointInt32 now = Cursor.Position();
        (double width, double height) = StartMenuLayout.Resize(
            drag.Bounds, now.X - drag.Start.X, now.Y - drag.Start.Y, drag.LeftCorner, _anchor.Centered, _anchor.Monitor.Dpi / 96.0);
        Place(width, height);
    }

    private void Grip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_resize is not { } drag || e.Pointer.PointerId != drag.PointerId)
            return;

        _resize = null;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        // What the monitor allowed, so a size too big for this one isn't kept for the next.
        double scale = _anchor.Monitor.Dpi / 96.0;
        RectInt32 bounds = _placement.Bounds;
        _owner.Settings.Update(_owner.Settings.Current with { StartMenuWidth = bounds.Width / scale, StartMenuHeight = bounds.Height / scale });
    }

    private sealed record ResizeDrag(uint PointerId, PointInt32 Start, RectInt32 Bounds, bool LeftCorner);

    private void Hide(bool restoreForeground)
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        _search?.Cancel();
        // Hand the foreground back to where the user was. Left to itself, Windows gives it to the next window in
        // z-order, which can be one of Explorer's invisible Start or search windows.
        if (restoreForeground && TopLevelWindows.GetForeground() == _hwnd && _previousForeground != 0 && TopLevelWindows.Exists(_previousForeground))
            TopLevelWindows.Activate(_previousForeground);
        // Back into the taskbar, accelerating; what it showed is reset once it's out of sight.
        _closing = true;
        _slide.To(_placement.Bounds with { Y = _anchor.Taskbar.Y }, s_closeDuration, decelerate: false, () =>
        {
            _closing = false;
            AppWindow.Hide();
            ResetContent();
        });
    }

    // What Start shows when it opens next; changed once it's out of sight.
    private void ResetContent()
    {
        SearchBox.Text = "";
        ShowView(HomeView);
        HomeView.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void ShowView(FrameworkElement view)
    {
        HomeView.Visibility = view == HomeView ? Visibility.Visible : Visibility.Collapsed;
        AllAppsView.Visibility = view == AllAppsView ? Visibility.Visible : Visibility.Collapsed;
        SearchView.Visibility = view == SearchView ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPinned()
    {
        Sync(_pinned, [.. PinnedStartApps.Select(app => new StartItem(app, "App", isApp: true, _owner.Icons))]);
        NoPinnedText.Visibility = _pinned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Windows records each start, by Explorer or by NeoShell, in UserAssist; reading it is quick enough for each open.
    private void ShowRecent()
    {
        try
        {
            DateTime now = DateTime.Now;
            Sync(_recent, [.. StartCatalog.Recent(_apps.Select(item => item.Target), UserAssist.Load(), MaxRecentApps)
                .Select(recent => new StartItem(recent.App, StartCatalog.LastRunText(recent.LastRun, now), isApp: true, _owner.Icons))]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn("Could not read recent apps", ex);
            _recent.Clear();
        }
        RecentSection.Visibility = _recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Refilling a grid replays every item's entrance animation: all the icons vanish and come back, which after a
    // drag looks like the move went wrong. Items that are still there stay.
    private static void Sync(ObservableCollection<StartItem> items, IReadOnlyList<StartItem> wanted)
    {
        static bool Same(StartItem a, StartItem b) => a.Target == b.Target && a.Subtitle == b.Subtitle;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (!wanted.Any(item => Same(item, items[i])))
                items.RemoveAt(i);
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < items.Count && Same(items[i], wanted[i]))
                continue;

            int existing = items.Skip(i).ToList().FindIndex(item => Same(item, wanted[i]));
            if (existing >= 0)
                items.Move(i + existing, i);
            else
                items.Insert(i, wanted[i]);
        }
    }

    // Start begins with the pins the user made in Explorer's Start, once; after that NeoShell's pins are its own.
    private async void ImportExplorerPins()
    {
        _importingPins = true;
        IReadOnlyList<PinnedApp> imported = [];
        try
        {
            IReadOnlyList<ExplorerStartPin> pins = await Task.Run(StartLayout.ReadPinned);
            imported = StartCatalog.FromExplorerPins(_apps.Select(item => item.Target), pins);
            Log.Info($"Imported {imported.Count} of {pins.Count} pins from Explorer's Start");
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read Explorer's Start pins", ex);
        }
        finally
        {
            _importingPins = false;
        }

        // Pins made in NeoShell, before or while importing, stay first.
        ShellSettings settings = _owner.Settings.Current;
        _owner.Settings.Update(settings with
        {
            PinnedStartApps = [.. settings.PinnedStartApps, .. imported.Where(app => !settings.PinnedStartApps.Any(p => TaskGrouping.SameApp(p, app)))],
            ExplorerStartPinsImported = true,
        });
        ShowPinned();
    }

    // The catalog changes only when apps are installed or removed; reloading every few minutes, in the background,
    // keeps opening Start instant.
    private async void LoadAppsIfStale()
    {
        if (_loadingApps || (DateTime.UtcNow - _appsLoadedAt < s_catalogLifetime && _apps.Count > 0))
            return;

        _loadingApps = true;
        try
        {
            IReadOnlyList<AppCatalogEntry> entries = await Task.Run(AppCatalog.Load);
            _apps = [.. entries
                .Select(entry => new StartItem(StartCatalog.ToPinnedApp(entry), "App", isApp: true, _owner.Icons))
                .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)];
            // "#" sorts before the letters, as in Windows.
            _allApps.Source = _apps
                .GroupBy(item => StartCatalog.LetterFor(item.Title))
                .OrderBy(group => group.Key == "#" ? "" : group.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new StartGroup(group.Key, group))
                .ToList();
            // Setting the source makes a new view.
            AllAppsList.ItemsSource = _allApps.View;
            _appsLoadedAt = DateTime.UtcNow;
            Log.Info($"App catalog: {_apps.Count} apps");
            ShowRecent();
            if (!_owner.Settings.Current.ExplorerStartPinsImported && !_importingPins)
                ImportExplorerPins();
        }
        catch (Exception ex)
        {
            Log.Error("Loading the app catalog failed", ex);
        }
        finally
        {
            _loadingApps = false;
        }
    }

    private void RefreshIcons()
    {
        foreach (StartItem item in _pinned.Concat(_recent).Concat(_apps).Concat(_resultItems))
            item.RefreshIcon();
    }

    private void ShowUser()
    {
        string name = CurrentUser.DisplayName() ?? Environment.UserName;
        UserName.Text = name;
        UserPicture.DisplayName = name;
        AutomationProperties.SetName(UserPicture, name);
        LoadUserPicture();
    }

    private async void LoadUserPicture()
    {
        try
        {
            // Windows keeps the account picture's file per user SID.
            string? sid = WindowsIdentity.GetCurrent().User?.Value;
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\{sid}");
            if (key?.GetValue("Image96") is not string path || !File.Exists(path))
                return;

            byte[] bytes = await File.ReadAllBytesAsync(path);
            var picture = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            await picture.SetSourceAsync(stream.AsRandomAccessStream());
            UserPicture.ProfilePicture = picture;
        }
        catch (Exception ex)
        {
            // The initials stay.
            Log.Warn("Could not load the account picture", ex);
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search?.Cancel();
        string query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            ShowView(HomeView);
            return;
        }

        ShowView(SearchView);
        // Apps are ranked straight away; the index is asked once typing pauses, and each keystroke cancels the last ask.
        IReadOnlyList<StartItem> apps = [.. AppSearch.Rank(_apps, item => item.Title, query).Take(MaxAppResults)];
        ShowResults(apps, []);

        var search = _search = new CancellationTokenSource();
        try
        {
            await Task.Delay(s_searchDelay, search.Token);
            IReadOnlyList<IndexResult> files = await Task.Run(() => IndexSearch.Search(query, MaxFileResults, search.Token), search.Token);
            if (!search.IsCancellationRequested)
                ShowResults(apps, files);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // The index can be off or busy; app results still work.
            Log.Warn($"Index search for '{query}' failed", ex);
        }
    }

    private void ShowResults(IReadOnlyList<StartItem> apps, IReadOnlyList<IndexResult> files)
    {
        var groups = new List<StartGroup>();
        if (apps.Count > 0)
            groups.Add(new StartGroup(StartCatalog.Apps, apps));
        foreach (string kind in (string[])[StartCatalog.Documents, StartCatalog.Folders, StartCatalog.Other])
        {
            List<StartItem> items = [.. files
                .Where(file => StartCatalog.GroupFor(file) == kind)
                .Select(file => new StartItem(new PinnedApp(file.Name, Path: file.Path), Path.GetDirectoryName(file.Path) ?? "", isApp: false, _owner.Icons))];
            if (items.Count > 0)
                groups.Add(new StartGroup(kind, items));
        }

        // Replacing the whole list replays every row's entrance animation, so the apps would flash on each keystroke
        // and again when the index answers. Groups that haven't changed stay as they are.
        for (int i = 0; i < groups.Count; i++)
        {
            if (i < _resultGroups.Count && SameItems(_resultGroups[i], groups[i]))
                continue;
            if (i < _resultGroups.Count)
                _resultGroups.RemoveAt(i);
            _resultGroups.Insert(i, groups[i]);
        }
        while (_resultGroups.Count > groups.Count)
            _resultGroups.RemoveAt(_resultGroups.Count - 1);

        _resultItems = [.. _resultGroups.SelectMany(group => group)];
        NoResultsText.Visibility = _resultItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Enter opens the best match.
        ResultsList.SelectedIndex = _resultItems.Count > 0 ? 0 : -1;
    }

    private static bool SameItems(StartGroup a, StartGroup b) =>
        a.Key == b.Key && a.Select(item => item.Target).SequenceEqual(b.Select(item => item.Target));

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (SearchView.Visibility != Visibility.Visible)
            return;

        int count = _resultItems.Count;
        switch (e.Key)
        {
            case VirtualKey.Down when count > 0:
                ResultsList.SelectedIndex = Math.Min(ResultsList.SelectedIndex + 1, count - 1);
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Up when count > 0:
                ResultsList.SelectedIndex = Math.Max(ResultsList.SelectedIndex - 1, 0);
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                if ((ResultsList.SelectedItem ?? _resultItems.FirstOrDefault()) is StartItem item)
                    Open(item);
                e.Handled = true;
                break;
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    // Typing anywhere in Start types into the search box.
    private void Root_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (char.IsControl(e.Character) || ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), SearchBox))
            return;

        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.Text += e.Character;
        SearchBox.SelectionStart = SearchBox.Text.Length;
        e.Handled = true;
    }

    private void Item_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StartItem item && !(ReferenceEquals(sender, PinnedGrid) && _suppressPinClick))
            Open(item);
    }

    private void Open(StartItem item)
    {
        Hide();
        Launcher.Launch(item.Target);
    }

    private void AllAppsButton_Click(object sender, RoutedEventArgs e) => ShowView(AllAppsView);

    private void BackButton_Click(object sender, RoutedEventArgs e) => ShowView(HomeView);

    private void ItemMenu_Opening(object sender, object e)
    {
        var menu = (MenuFlyout)sender;
        menu.Items.Clear();
        if (menu.Target?.DataContext is not StartItem item)
            return;

        menu.Items.Add(MenuItem("Open", "StartOpenMenuItem", () => Open(item)));
        if (!item.IsApp)
            return;

        PinnedApp app = item.Target;
        bool onStart = PinnedStartApps.Any(p => TaskGrouping.SameApp(p, app));
        bool onTaskbar = _owner.Settings.Current.PinnedTaskbarApps.Any(p => TaskGrouping.SameApp(p, app));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(onStart
            ? MenuItem("Unpin from Start", "StartUnpinMenuItem", () => SetPinnedStartApps([.. PinnedStartApps.Where(p => !TaskGrouping.SameApp(p, app))]))
            : MenuItem("Pin to Start", "StartPinMenuItem", () => SetPinnedStartApps([.. PinnedStartApps, app])));
        menu.Items.Add(onTaskbar
            ? MenuItem("Unpin from taskbar", "StartUnpinTaskbarMenuItem", () => _owner.Unpin(app))
            : MenuItem("Pin to taskbar", "StartPinTaskbarMenuItem", () => _owner.Pin(app)));
    }

    // Pinned and recent apps' icons shrink while pressed.
    private void GridItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;

        for (var element = e.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, sender); element = VisualTreeHelper.GetParent(element))
        {
            if (element is GridViewItem item && ItemIcon(item) is { } icon)
            {
                _pressedIcon = icon;
                IconPress.Scale(icon, IconPress.Pressed);
                return;
            }
        }
    }

    private void GridItem_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedIcon is not null)
            IconPress.Scale(_pressedIcon, 1);
        _pressedIcon = null;
    }

    private static UIElement? ItemIcon(DependencyObject container) =>
        (container as ContentControl)?.ContentTemplateRoot is FrameworkElement root ? root.FindName("ItemIcon") as UIElement : null;

    // Reordering is done by hand rather than with the grid's own drag and drop, which keeps the dropped icon hidden
    // until its drag operation has wound down: it vanishes and comes back. Here the icon follows the pointer, the
    // icons in between shift a slot to make room (GridReorder), and the drop is a move that shows no change.
    private void PinnedGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressPinClick = false;
        if (_pinDrag is not null || !e.GetCurrentPoint(PinnedGrid).Properties.IsLeftButtonPressed)
            return;

        for (var element = e.OriginalSource as DependencyObject; element is not null && element != PinnedGrid; element = VisualTreeHelper.GetParent(element))
        {
            if (element is GridViewItem { Content: StartItem item })
            {
                _pinPressed = (e.Pointer.PointerId, e.GetCurrentPoint(PinnedGrid).Position, item);
                return;
            }
        }
    }

    private void PinnedGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pinPressed is not { } pressed || pressed.PointerId != e.Pointer.PointerId)
            return;

        Point position = e.GetCurrentPoint(PinnedGrid).Position;
        double dx = position.X - pressed.Start.X;
        double dy = position.Y - pressed.Start.Y;
        if (_pinDrag is null && (Math.Sqrt(dx * dx + dy * dy) < GridReorder.Threshold || !StartPinDrag(pressed.Item, e.Pointer)))
            return;

        PinDrag drag = _pinDrag!;
        (double X, double Y) slot = drag.Slots[drag.Index];
        drag.Target = GridReorder.TargetIndex(drag.Slots, (slot.X + dx, slot.Y + dy));
        for (int i = 0; i < drag.Containers.Count; i++)
        {
            (double x, double y) = i == drag.Index ? (dx, dy) : GridReorder.MakeWayOffset(drag.Slots, drag.Index, drag.Target, i);
            drag.Containers[i].Translation = new Vector3((float)x, (float)y, 0);
        }
        e.Handled = true;
    }

    private void PinnedGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _pinPressed = null;
        if (_pinDrag is null)
            return;

        // The grid would otherwise take the release as a click on the app.
        _suppressPinClick = true;
        EndPinDrag(drop: true);
        PinnedGrid.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void PinnedGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _pinPressed = null;
        if (_pinDrag is not null)
            EndPinDrag(drop: false);
    }

    private bool StartPinDrag(StartItem item, Pointer pointer)
    {
        int index = _pinned.IndexOf(item);
        var containers = new List<UIElement>();
        var slots = new List<(double X, double Y)>();
        for (int i = 0; i < _pinned.Count; i++)
        {
            if (PinnedGrid.ContainerFromIndex(i) is not UIElement container)
                return false;
            containers.Add(container);
            Point corner = container.TransformToVisual(PinnedGrid).TransformPoint(default);
            slots.Add((corner.X, corner.Y));
        }
        if (index < 0 || !PinnedGrid.CapturePointer(pointer))
            return false;

        foreach (UIElement container in containers)
            container.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(150) };
        containers[index].TranslationTransition = null;
        Canvas.SetZIndex(containers[index], 1);
        if (ItemIcon(containers[index]) is { } icon)
            IconPress.Scale(icon, IconPress.Dragged);
        _pinDrag = new PinDrag(containers, slots, index) { Target = index };
        return true;
    }

    private void EndPinDrag(bool drop)
    {
        PinDrag drag = _pinDrag!;
        _pinDrag = null;
        foreach (UIElement container in drag.Containers)
        {
            container.TranslationTransition = null;
            container.Translation = default;
        }
        Canvas.SetZIndex(drag.Containers[drag.Index], 0);
        if (ItemIcon(drag.Containers[drag.Index]) is { } icon)
            IconPress.Scale(icon, 1);
        if (drop && drag.Target != drag.Index)
        {
            // The icons are already drawn where they belong, so the move shows no change.
            _pinned.Move(drag.Index, drag.Target);
            _owner.Settings.Update(_owner.Settings.Current with { PinnedStartApps = [.. _pinned.Select(item => item.Target)] });
        }
    }

    private sealed class PinDrag(List<UIElement> containers, List<(double X, double Y)> slots, int index)
    {
        public List<UIElement> Containers { get; } = containers;
        public List<(double X, double Y)> Slots { get; } = slots;
        public int Index { get; } = index;
        public int Target { get; set; }
    }

    private void SetPinnedStartApps(IReadOnlyList<PinnedApp> apps)
    {
        _owner.Settings.Update(_owner.Settings.Current with { PinnedStartApps = apps });
        ShowPinned();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        Launcher.OpenSettings(_owner.RunMode, "Settings", "ms-settings:");
    }

    private async void SwitchToExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Switch to Explorer?",
            Content = "NeoShell will close and Explorer will be your shell again, now and the next time you sign in.",
            PrimaryButtonText = "Switch",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Hide();
            _owner.SwitchToExplorer();
        }
    }

    private void Lock_Click(object sender, RoutedEventArgs e) => RunPowerAction("Lock", Power.Lock);

    private void SignOut_Click(object sender, RoutedEventArgs e) => RunPowerAction("Sign out", Power.SignOut);

    private void Sleep_Click(object sender, RoutedEventArgs e) => RunPowerAction("Sleep", Power.Sleep);

    private void Restart_Click(object sender, RoutedEventArgs e) => RunPowerAction("Restart", Power.Restart);

    private void ShutDown_Click(object sender, RoutedEventArgs e) => RunPowerAction("Shut down", Power.ShutDown);

    private void RunPowerAction(string name, Action action)
    {
        Hide();
        Log.Info($"Power: {name}");
        try
        {
            action();
        }
        catch (Win32Exception ex)
        {
            Log.Warn($"{name} failed", ex);
        }
    }

    private static MenuFlyoutItem MenuItem(string text, string automationId, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => onClick();
        return item;
    }
}
