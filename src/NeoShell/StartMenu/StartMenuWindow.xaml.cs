using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
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
/// The Start menu: pinned apps and folders of them, recent apps, All apps, search over apps and the Windows Search index,
/// and the user, Switch to Explorer, the folders chosen for Start and the power button. Created once and shown above
/// whichever taskbar opened it.
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
    // Explorer's folder panel grows out of the folder's tile over a third of a second, sharply decelerating, and
    // shrinks back into it in 150 ms; an app held over another shrinks into the folder plate in 150 ms.
    private static readonly TimeSpan s_folderOpenDuration = TimeSpan.FromMilliseconds(333);
    private static readonly TimeSpan s_folderCloseDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan s_groupPreviewDuration = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// The folders Start can show beside the power button, as Explorer draws them, in its order, and the folder each
    /// opens in File Explorer (Settings opens as Win+I does, File Explorer as Win+E).
    /// </summary>
    private static readonly (StartPlace Place, string Name, string Glyph, string AutomationId, string? Folder)[] s_places =
    [
        (StartPlace.Documents, "Documents", "\uE8A5", "DocumentsPlaceButton", "shell:Personal"),
        (StartPlace.Downloads, "Downloads", "\uE896", "DownloadsPlaceButton", "shell:Downloads"),
        (StartPlace.Music, "Music", "\uEC4F", "MusicPlaceButton", "shell:My Music"),
        (StartPlace.Pictures, "Pictures", "\uEB9F", "PicturesPlaceButton", "shell:My Pictures"),
        (StartPlace.Videos, "Videos", "\uE714", "VideosPlaceButton", "shell:My Video"),
        (StartPlace.Network, "Network", "\uEC27", "NetworkPlaceButton", "shell:NetworkPlacesFolder"),
        (StartPlace.PersonalFolder, "Personal folder", "\uEC25", "PersonalFolderPlaceButton", "shell:UsersFilesFolder"),
        (StartPlace.FileExplorer, "File Explorer", "\uEC50", "FileExplorerPlaceButton", null),
        (StartPlace.Settings, "Settings", "\uE713", "SettingsButton", null),
    ];

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
    private (uint PointerId, Point Start, GridView Grid, StartItem Item)? _pinPressed;
    private PinDrag? _pinDrag;
    private bool _suppressPinClick;
    private readonly ObservableCollection<StartItem> _folderApps = [];
    // The open folder's id, and its tile in the grid, hidden while the folder is open.
    private string? _folderId;
    private UIElement? _folderTile;
    private Storyboard? _folderAnimation;
    private readonly Dictionary<StartPlace, Button> _placeButtons = [];
    private UIElement? _pressedIcon;
    // The open app menu's commands from Windows, kept until another menu opens: they run through it.
    private ShellMenu? _appShellMenu;
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
        FolderGrid.ItemsSource = _folderApps;
        foreach (GridView grid in (GridView[])[PinnedGrid, FolderGrid])
        {
            // Handled events too: the item under the pointer takes the press for its click.
            grid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PinGrid_PointerPressed), handledEventsToo: true);
            grid.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PinGrid_PointerMoved), handledEventsToo: true);
            grid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PinGrid_PointerReleased), handledEventsToo: true);
            grid.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PinGrid_PointerCaptureLost), handledEventsToo: true);
        }
        foreach (GridView grid in (GridView[])[PinnedGrid, FolderGrid, RecentGrid])
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
        AddPlaceButtons();
        ShowUser();
        // Start's pins were apps only before folders; once, they become pins of their own.
        if (owner.Settings.Current.PinnedStartApps is { } apps)
        {
            owner.Settings.Update(owner.Settings.Current with
            {
                StartPins = [.. owner.Settings.Current.StartPins, .. apps.Select(app => new StartPin(app))],
                PinnedStartApps = null,
            });
        }

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
            // Closing an open Start deactivates it afterwards; hiding it then would slide a window that's gone.
            IsOpen = false;
            _slide.Stop();
            owner.Icons.Loaded -= RefreshIcons;
            _placement.Dispose();
            _frameless.Dispose();
        };

        LoadAppsIfStale();

        // Shown once, cut off entirely, so WinUI draws it: the first opening would otherwise slide up a black window
        // until the first frame is ready.
        _placement.Bounds = new RectInt32(0, 0, 640, 640);
        _placement.VisibleBottom = int.MinValue;
        AppWindow.Show(activateWindow: false);
        Root.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!IsOpen)
                AppWindow.Hide();
        });
    }

    public bool IsOpen { get; private set; }

    private IReadOnlyList<StartPin> Pins => _owner.Settings.Current.StartPins;

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
        FolderPanel.Background = FolderPanelBrush(accent, Root.RequestedTheme);
        ShowPinned();
        ShowRecent();
        ShowPlaces();
        ShowPowerButton();
        LoadAppsIfStale();

        _anchor = (monitor, taskbar, centered);
        ShellSettings settings = _owner.Settings.Current;
        RectInt32 shown = BoundsFor(settings.StartMenuWidth, settings.StartMenuHeight);
        // Just below the taskbar in the topmost band, and cut off at its edge: it slides up from behind the taskbar,
        // whatever's in front of the screen's bottom and however see-through the taskbar is.
        _placement.SetLayer(PinnedLayer.Topmost, above: taskbarWindow);
        _placement.VisibleBottom = taskbar.Y;
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

    // An open folder's panel has an acrylic of its own: what's behind it in Start blurred, under a shade a little
    // darker than Start (measured on Explorer's: about six sevenths of Start's colour). In-app acrylic can't see the
    // window's backdrop, only Start's content, so it starts from Start's tint; an accent is greyed a little, as
    // Start's own acrylic greys it with what's behind the window.
    private static AcrylicBrush FolderPanelBrush(Color? accent, ElementTheme theme)
    {
        Color tint = accent is { } color ? Mix(Shade(color, 0.72), Color.FromArgb(255, 0x40, 0x40, 0x40), 0.15)
            : theme == ElementTheme.Light ? Shade(Color.FromArgb(255, 0xF3, 0xF3, 0xF3), 0.95)
            : Shade(Color.FromArgb(255, 0x20, 0x20, 0x20), 0.86);
        return new AcrylicBrush { TintColor = tint, TintOpacity = 0.85, TintLuminosityOpacity = 0.85, FallbackColor = tint };
    }

    private static Color Shade(Color color, double factor) =>
        Color.FromArgb(255, (byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));

    private static Color Mix(Color color, Color with, double amount) => Color.FromArgb(
        255,
        (byte)(color.R + (with.R - color.R) * amount),
        (byte)(color.G + (with.G - color.G) * amount),
        (byte)(color.B + (with.B - color.B) * amount));

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
        CloseFolder(animate: false);
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
        Sync(_pinned, [.. Pins.Select(pin => pin.Folder is { } folder
            ? new StartItem(folder, _owner.Icons)
            : new StartItem(pin.App!, "App", isApp: true, _owner.Icons))]);
        NoPinnedText.Visibility = _pinned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_folderId is not null)
            ShowFolderApps();
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
        static bool Same(StartItem a, StartItem b) => a.Target == b.Target && a.Subtitle == b.Subtitle && a.Folder == b.Folder;

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

    // Start and the taskbar begin with the pins the user made in Explorer, once; after that NeoShell's pins are its
    // own. Start does it for both, as matching a pin to an app needs Start's catalog. Explorer's export lists a folder's
    // apps in the folder's place without the folder, so they come over as apps of their own (see design.md).
    private async void ImportExplorerPins()
    {
        _importingPins = true;
        ShellSettings before = _owner.Settings.Current;
        IReadOnlyList<PinnedApp> start = before.ExplorerStartPinsImported ? [] : await ImportPins("Start", StartLayout.ReadPinned);
        IReadOnlyList<PinnedApp> taskbar = before.ExplorerTaskbarPinsImported ? [] : await ImportPins("taskbar", TaskbarFavorites.ReadPinned);
        _importingPins = false;

        ShellSettings settings = _owner.Settings.Current;
        _owner.Settings.Update(settings with
        {
            StartPins = StartPins.AddImported(settings.StartPins, start),
            PinnedTaskbarApps = StartCatalog.AddImported(settings.PinnedTaskbarApps, taskbar),
            ExplorerStartPinsImported = true,
            ExplorerTaskbarPinsImported = true,
        });
        ShowPinned();
    }

    private async Task<IReadOnlyList<PinnedApp>> ImportPins(string place, Func<IReadOnlyList<ExplorerPin>> read)
    {
        try
        {
            IReadOnlyList<ExplorerPin> pins = await Task.Run(read);
            IReadOnlyList<PinnedApp> imported = StartCatalog.FromExplorerPins(_apps.Select(item => item.Target), pins);
            Log.Info($"Imported {imported.Count} of {pins.Count} pins from Explorer's {place}");
            return imported;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read Explorer's {place} pins", ex);
            return [];
        }
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
            if (_owner.Settings.Current is not { ExplorerStartPinsImported: true, ExplorerTaskbarPinsImported: true } && !_importingPins)
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
        foreach (StartItem item in _pinned.Concat(_folderApps).Concat(_recent).Concat(_apps).Concat(_resultItems))
            item.RefreshIcon();
    }

    private async void ShowUser()
    {
        string name = UserAccount.DisplayName;
        UserName.Text = name;
        UserPicture.DisplayName = name;
        AutomationProperties.SetName(UserPicture, name);
        // Without a picture, the initials stay.
        if (await UserAccount.LoadPictureAsync(96) is { } picture)
            UserPicture.ProfilePicture = picture;
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
            // An open folder closes first, then Start.
            if (_folderId is not null)
                CloseFolder(animate: true);
            else
                Hide();
            e.Handled = true;
        }
    }

    // Typing anywhere in Start types into the search box, unless a folder is being renamed.
    private void Root_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (char.IsControl(e.Character) || FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox)
            return;

        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.Text += e.Character;
        SearchBox.SelectionStart = SearchBox.Text.Length;
        e.Handled = true;
    }

    private void Item_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not StartItem item || ((ReferenceEquals(sender, PinnedGrid) || ReferenceEquals(sender, FolderGrid)) && _suppressPinClick))
            return;

        if (item.IsFolder)
            OpenFolder(_pinned.IndexOf(item));
        else
            Open(item);
    }

    private void Open(StartItem item)
    {
        Hide();
        Launcher.Launch(item.Target);
    }

    private void AllAppsButton_Click(object sender, RoutedEventArgs e) => ShowView(AllAppsView);

    private void BackButton_Click(object sender, RoutedEventArgs e) => ShowView(HomeView);

    // An app's menu in All apps and Recent.
    private void ItemMenu_Opening(object sender, object e)
    {
        var menu = (MenuFlyout)sender;
        menu.Items.Clear();
        if (menu.Target?.DataContext is StartItem item)
            FillAppMenu(menu, item.Target, [], search: false);
    }

    // Search results: an app's commands in the order Windows' search lists them, without separators or jump list; a
    // file only opens.
    private void ResultMenu_Opening(object sender, object e)
    {
        var menu = (MenuFlyout)sender;
        menu.Items.Clear();
        if (menu.Target?.DataContext is not StartItem item)
            return;

        if (item.IsApp)
            FillAppMenu(menu, item.Target, [], search: true);
        else
            menu.Items.Add(MenuItem("Open", "", "StartOpenMenuItem", () => Open(item)));
    }

    // A pin's menu, as Explorer's: moves within the grid or the folder and the folder commands, then the app's own.
    // The grid and the folder change under an open menu only through it, so the indexes it was opened with hold.
    private void PinMenu_Opening(object sender, object e)
    {
        var menu = (MenuFlyout)sender;
        menu.Items.Clear();
        if (menu.Target?.DataContext is not StartItem item)
            return;

        var commands = new Dictionary<AppCommand, MenuFlyoutItemBase>();
        int inFolder = _folderApps.IndexOf(item);
        if (inFolder >= 0)
        {
            int folder = OpenFolderIndex;
            if (inFolder > 0)
                commands[AppCommand.MoveLeft] = Command(AppCommand.MoveLeft, () => SetPins(StartPins.MoveInFolder(Pins, folder, inFolder, inFolder - 1)));
            if (inFolder < _folderApps.Count - 1)
                commands[AppCommand.MoveRight] = Command(AppCommand.MoveRight, () => SetPins(StartPins.MoveInFolder(Pins, folder, inFolder, inFolder + 1)));
            commands[AppCommand.RemoveFromFolder] = Command(AppCommand.RemoveFromFolder, () => TakeOutOfFolder(inFolder, folder + 1));
            FillAppMenu(menu, item.Target, commands, search: false);
            return;
        }

        int index = _pinned.IndexOf(item);
        if (index < 0)
            return;
        // Move to front only from the third place on: from the second, Move left does the same.
        if (index > 1)
            commands[AppCommand.MoveToFront] = Command(AppCommand.MoveToFront, () => SetPins(StartPins.Move(Pins, index, 0)));
        if (index > 0)
            commands[AppCommand.MoveLeft] = Command(AppCommand.MoveLeft, () => SetPins(StartPins.Move(Pins, index, index - 1)));
        if (index < _pinned.Count - 1)
            commands[AppCommand.MoveRight] = Command(AppCommand.MoveRight, () => SetPins(StartPins.Move(Pins, index, index + 1)));
        if (item.IsFolder)
        {
            FillAppMenu(menu, null, commands, search: false);
            return;
        }

        commands[AppCommand.NewFolder] = Command(AppCommand.NewFolder, () => SetPins(StartPins.NewFolder(Pins, index)));
        if (MoveToFolderItem(index) is { } moveToFolder)
            commands[AppCommand.MoveToFolder] = moveToFolder;
        FillAppMenu(menu, item.Target, commands, search: false);
    }

    // With one folder, one command naming it; with more, a submenu of them by name (text only, as Explorer's).
    private MenuFlyoutItemBase? MoveToFolderItem(int index)
    {
        int[] folders = [.. Enumerable.Range(0, _pinned.Count).Where(i => _pinned[i].IsFolder)];
        if (folders.Length == 1)
        {
            MenuFlyoutItem item = Command(AppCommand.MoveToFolder, () => SetPins(StartPins.Group(Pins, index, folders[0])));
            item.Text = $"Move to app folder \"{_pinned[folders[0]].Title}\"";
            return item;
        }
        if (folders.Length == 0)
            return null;

        (string text, string glyph, string automationId) = s_commands[AppCommand.MoveToFolder];
        var submenu = new MenuFlyoutSubItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        AutomationProperties.SetAutomationId(submenu, automationId);
        foreach (int folder in folders)
            submenu.Items.Add(MenuItem(_pinned[folder].Title, "", "StartFolderMenuItem", () => SetPins(StartPins.Group(Pins, index, folder))));
        return submenu;
    }

    /// <summary>
    /// Fills an app's menu, as Explorer's Start does: <paramref name="commands"/> (the moves), pinning to Start and to
    /// the taskbar, what Windows offers for the app in shell:AppsFolder (Run as administrator, Open file location,
    /// Uninstall…), App settings for a packaged app, in Explorer's order and groups; then the app's jump list.
    /// </summary>
    private void FillAppMenu(MenuFlyout menu, PinnedApp? app, Dictionary<AppCommand, MenuFlyoutItemBase> commands, bool search)
    {
        _appShellMenu?.Dispose();
        _appShellMenu = null;
        if (app is not null)
        {
            if (StartPins.Contains(Pins, app))
                commands[AppCommand.UnpinFromStart] = Command(AppCommand.UnpinFromStart, () => SetPins(StartPins.Unpin(Pins, app)));
            else
                commands[AppCommand.PinToStart] = Command(AppCommand.PinToStart, () => SetPins(StartPins.Pin(Pins, app)));
            if (OnTaskbar(app))
                commands[AppCommand.UnpinFromTaskbar] = Command(AppCommand.UnpinFromTaskbar, () => _owner.Unpin(app));
            else
                commands[AppCommand.PinToTaskbar] = Command(AppCommand.PinToTaskbar, () => _owner.Pin(app));
            AddShellCommands(commands, app);
            // Settings can't show without Explorer, so as the shell there's no page to open.
            if (IsPackaged(app) && _owner.RunMode == RunMode.AlongsideExplorer)
                commands[AppCommand.AppSettings] = Command(AppCommand.AppSettings, () => OpenAppSettings(app));
        }
        if (search && commands.TryGetValue(AppCommand.OpenFileLocation, out MenuFlyoutItemBase? location) && location is MenuFlyoutItem locationItem)
            locationItem.Icon = new FontIcon { Glyph = "\uE8B7" };

        HashSet<AppCommand> present = [.. commands.Keys];
        if (search)
        {
            foreach (AppCommand command in StartAppMenu.SearchLayout(present))
                menu.Items.Add(commands[command]);
            return;
        }
        foreach (AppCommand? command in StartAppMenu.Layout(present))
            menu.Items.Add(command is { } c ? commands[c] : new MenuFlyoutSeparator());
        if (app is not null)
            AddJumpList(menu, app);
    }

    private bool OnTaskbar(PinnedApp app) => _owner.Settings.Current.PinnedTaskbarApps.Any(p => TaskGrouping.SameApp(p, app));

    private static bool IsPackaged(PinnedApp app) => app.AppUserModelId is { } id && PackagedApps.IsPackagedAppId(id);

    // The verbs Windows gives the app in shell:AppsFolder, run through the folder's own menu, as Explorer's Start does:
    // it starts a packaged app elevated and finds a shortcut's folder, which NeoShell can't on its own. A packaged
    // app's Uninstall asks first and removes the package, as Explorer's; a desktop app's opens Installed apps (as the
    // shell, where Settings can't show, the folder's own Programs and Features).
    private void AddShellCommands(Dictionary<AppCommand, MenuFlyoutItemBase> commands, PinnedApp app)
    {
        // The folder knows apps by AppUserModelID, else by path: in full, or from a known folder (System32's apps).
        _appShellMenu = ((string?[])[app.AppUserModelId, app.Path, app.Path is { } path ? JumpLists.ImplicitAppId(path) : null])
            .OfType<string>()
            .Select(id => ShellMenu.ForApp(_hwnd, id))
            .FirstOrDefault(shellMenu => shellMenu is not null);
        if (_appShellMenu is not { } shellMenu)
            return;

        foreach (ShellMenuItem shellItem in shellMenu.Items)
        {
            if (StartAppMenu.FromVerb(shellItem.Verb) is not { } command || commands.ContainsKey(command))
                continue;
            // The folder elevates a packaged app through Explorer: as the shell, that fails.
            if (command == AppCommand.RunAsAdministrator && IsPackaged(app) && _owner.RunMode == RunMode.Shell)
                continue;

            Action run = () =>
            {
                Hide();
                if (!shellMenu.Invoke(shellItem))
                    Log.Warn($"{shellItem.Verb} failed for {app.DisplayName}");
            };
            if (command == AppCommand.Uninstall && IsPackaged(app))
                run = () => ConfirmUninstall(app);
            else if (command == AppCommand.Uninstall && _owner.RunMode == RunMode.AlongsideExplorer)
                run = () => OpenSettingsPage(app, PackagedApps.OpenInstalledApps);

            MenuFlyoutItem item = Command(command, run);
            // Explorer's own names for its commands; the others (File Explorer's Manage, Properties…) keep the shell's.
            if (!s_commands.ContainsKey(command))
                item.Text = shellItem.Text;
            commands[command] = item;
        }
    }

    private void OpenAppSettings(PinnedApp app) => OpenSettingsPage(app, () => PackagedApps.OpenAppSettings(app.AppUserModelId!));

    private void OpenSettingsPage(PinnedApp app, Action open)
    {
        Hide();
        // Starting Settings waits for it to start.
        Task.Run(() =>
        {
            try
            {
                open();
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not open Settings for {app.DisplayName}", ex);
            }
        });
    }

    // Explorer asks in Start itself, Cancel the default, and removes the package; its pins go with it.
    private async void ConfirmUninstall(PinnedApp app)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = $"Uninstall \"{app.DisplayName}\"?",
            Content = "This app and its related information will be removed.",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            // Explorer's has an accent edge and doesn't darken Start behind it.
            BorderBrush = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]),
        };
        // WinUI moves the dialog's smoke layer out into a popup of its own as it opens.
        dialog.Loaded += (_, _) =>
        {
            foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot))
            {
                if (popup.Child is FrameworkElement { Name: "SmokeLayerBackground" } smoke)
                    smoke.Visibility = Visibility.Collapsed;
            }
        };
        AutomationProperties.SetAutomationId(dialog, "UninstallDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            await PackagedApps.UninstallAsync(app.AppUserModelId!);
            Log.Info($"Uninstalled {app.DisplayName}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not uninstall {app.DisplayName}", ex);
            return;
        }
        if (StartPins.Contains(Pins, app))
            SetPins(StartPins.Unpin(Pins, app));
        if (OnTaskbar(app))
            _owner.Unpin(app);
        _appsLoadedAt = DateTime.MinValue;
        LoadAppsIfStale();
    }

    // The app's jump list below its commands, as in Explorer's Start: its categories (Recent, the app's own), then its
    // tasks, under headings. Read each time the menu opens: apps change them whenever they like.
    private void AddJumpList(MenuFlyout menu, PinnedApp app)
    {
        string? appId = app.AppUserModelId ?? (app.Path is { } path ? JumpLists.ImplicitAppId(path) : null);
        if (appId is null)
            return;

        IReadOnlyList<JumpListCategory> categories;
        try
        {
            categories = JumpLists.Load(appId);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the jump list of {appId}", ex);
            return;
        }

        var headerStyle = (Style)Root.Resources["JumpListHeaderStyle"];
        int iconSize = (int)Math.Round(16 * Root.XamlRoot.RasterizationScale);
        foreach (JumpListCategory category in categories)
        {
            var header = new MenuFlyoutItem { Text = category.Title, Style = headerStyle };
            AutomationProperties.SetAutomationId(header, "JumpListHeader");
            menu.Items.Add(header);
            foreach (JumpListItem entry in category.Items.Where(entry => entry.Kind != JumpListItemKind.Separator))
            {
                var icon = new ImageIcon();
                // Explorer's menu grows to 290 at most (an item 289 wide makes it at most that here); longer names end
                // in an ellipsis.
                var item = new MenuFlyoutItem { Text = entry.Title, Icon = icon, MaxWidth = 289 };
                item.Resources["MenuFlyoutItemTextTrimming"] = TextTrimming.CharacterEllipsis;
                AutomationProperties.SetAutomationId(item, "JumpListItem");
                item.Click += (_, _) => OpenJumpListItem(entry);
                menu.Items.Add(item);
                AppIcons.Load(() => JumpLists.GetIcon(entry, iconSize), source => icon.Source = source);
            }
        }
    }

    private void OpenJumpListItem(JumpListItem item)
    {
        Hide();
        try
        {
            JumpLists.Open(item, _hwnd);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open the jump list item {item.Title}", ex);
        }
    }

    // Explorer's names, glyphs (Segoe Fluent Icons) and, for testing, NeoShell's automation ids of the commands.
    private static readonly Dictionary<AppCommand, (string Text, string Glyph, string AutomationId)> s_commands = new()
    {
        [AppCommand.MoveToFront] = ("Move to front", "\uE1AA", "StartMoveToFrontMenuItem"),
        [AppCommand.MoveLeft] = ("Move left", "\uE64E", "StartMoveLeftMenuItem"),
        [AppCommand.MoveRight] = ("Move right", "\uE64D", "StartMoveRightMenuItem"),
        [AppCommand.NewFolder] = ("Create a new app folder", "\uE8F4", "StartNewFolderMenuItem"),
        [AppCommand.MoveToFolder] = ("Move to app folder", "\uE8DE", "StartMoveToFolderMenuItem"),
        [AppCommand.RemoveFromFolder] = ("Remove from app folder", "\uE8DA", "StartRemoveFromFolderMenuItem"),
        [AppCommand.PinToStart] = ("Pin to Start", "\uE718", "StartPinMenuItem"),
        [AppCommand.UnpinFromStart] = ("Unpin from Start", "\uE77A", "StartUnpinMenuItem"),
        [AppCommand.RunAsAdministrator] = ("Run as administrator", "\uE7EF", "StartRunAsMenuItem"),
        [AppCommand.OpenFileLocation] = ("Open file location", "\uED43", "StartOpenFileLocationMenuItem"),
        [AppCommand.PinToTaskbar] = ("Pin to taskbar", "\uE718", "StartPinTaskbarMenuItem"),
        [AppCommand.UnpinFromTaskbar] = ("Unpin from taskbar", "\uE77A", "StartUnpinTaskbarMenuItem"),
        [AppCommand.AppSettings] = ("App settings", "\uE713", "StartAppSettingsMenuItem"),
        [AppCommand.Uninstall] = ("Uninstall", "\uE74D", "StartUninstallMenuItem"),
    };

    // A command's item: Explorer's name and glyph; the shell's verbs Explorer shows without a glyph get none.
    private static MenuFlyoutItem Command(AppCommand command, Action onClick)
    {
        (string text, string glyph, string automationId) = s_commands.TryGetValue(command, out var known)
            ? known
            : ("", "", "StartShellVerbMenuItem");
        return MenuItem(text, glyph, automationId, onClick);
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
                IconPress.Press(icon);
                return;
            }
        }
    }

    private void GridItem_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedIcon is not null)
            IconPress.Release(_pressedIcon);
        _pressedIcon = null;
    }

    private static UIElement? ItemIcon(DependencyObject container) =>
        (container as ContentControl)?.ContentTemplateRoot is FrameworkElement root ? root.FindName("ItemIcon") as UIElement : null;

    // Pins are dragged by hand rather than with the grid's own drag and drop, which keeps the dropped icon hidden
    // until its drag operation has wound down: it vanishes and comes back. Here a copy of the pin follows the pointer
    // (above everything, so it can leave a folder's panel), the pins in between shift a slot to make room
    // (GridReorder), and the drop is a move that shows no change. Held over the middle of another app or a folder,
    // an app is grouped with it instead, as in Explorer; an app dragged out of a folder's panel goes into the grid.
    private void PinGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var grid = (GridView)sender;
        _suppressPinClick = false;
        if (_pinDrag is not null || !e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed)
            return;

        for (var element = e.OriginalSource as DependencyObject; element is not null && element != grid; element = VisualTreeHelper.GetParent(element))
        {
            if (element is GridViewItem { Content: StartItem item })
            {
                _pinPressed = (e.Pointer.PointerId, e.GetCurrentPoint(Root).Position, grid, item);
                return;
            }
        }
    }

    private void PinGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pinPressed is not { } pressed || pressed.PointerId != e.Pointer.PointerId || !ReferenceEquals(sender, pressed.Grid))
            return;

        Point position = e.GetCurrentPoint(Root).Position;
        double dx = position.X - pressed.Start.X;
        double dy = position.Y - pressed.Start.Y;
        if (_pinDrag is null && (Math.Sqrt(dx * dx + dy * dy) < GridReorder.Threshold || !StartPinDrag(pressed.Grid, pressed.Item, pressed.Start, e.Pointer)))
            return;

        PinDrag drag = _pinDrag!;
        Canvas.SetLeft(drag.Copy, position.X - drag.Grab.X);
        Canvas.SetTop(drag.Copy, position.Y - drag.Grab.Y);
        AimPinDrag(drag, position);
        e.Handled = true;
    }

    private void PinGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _pinPressed = null;
        if (_pinDrag is null)
            return;

        // The grid would otherwise take the release as a click on the app.
        _suppressPinClick = true;
        // Released before the drop: a drop that closes the folder makes the grid holding the capture unreachable,
        // and a capture released after that stays with the window, which then takes no more pointer input.
        PinDrag drag = _pinDrag;
        _pinDrag = null;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        EndPinDrag(drag, drop: true);
        e.Handled = true;
    }

    private void PinGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _pinPressed = null;
        if (_pinDrag is { } drag)
        {
            _pinDrag = null;
            EndPinDrag(drag, drop: false);
        }
    }

    private bool StartPinDrag(GridView grid, StartItem item, Point start, Pointer pointer)
    {
        ObservableCollection<StartItem> items = grid == PinnedGrid ? _pinned : _folderApps;
        int index = items.IndexOf(item);
        if (index < 0 || Containers(grid, items.Count) is not { } containers || !grid.CapturePointer(pointer))
            return false;

        UIElement dragged = containers[index];
        Point corner = dragged.TransformToVisual(Root).TransformPoint(default);
        var copy = new ContentControl
        {
            ContentTemplate = (DataTemplate)Root.Resources["PinTemplate"],
            Content = item,
            IsHitTestVisible = false,
        };
        copy.Loaded += (_, _) =>
        {
            if (ItemIcon(copy) is { } icon)
                IconPress.Scale(icon, IconPress.Dragged);
        };
        Canvas.SetLeft(copy, corner.X);
        Canvas.SetTop(copy, corner.Y);
        DragLayer.Children.Add(copy);
        dragged.Opacity = 0;
        foreach (UIElement container in containers)
            container.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(150) };

        _pinDrag = new PinDrag(grid, containers, [.. containers.Select(c => Corner(c, grid))], index, copy, new Point(start.X - corner.X, start.Y - corner.Y))
        {
            Target = index,
            CanGroup = grid == PinnedGrid && !item.IsFolder,
            GridCentres = grid == FolderGrid && Containers(PinnedGrid, _pinned.Count) is { } pins ? [.. pins.Select(c => Centre(c, PinnedGrid))] : [],
        };
        return true;
    }

    // Where the pin would go if dropped now, shown by the pins making way, or by the app it would be grouped with.
    private void AimPinDrag(PinDrag drag, Point position)
    {
        Point panel = FolderPanel.TransformToVisual(Root).TransformPoint(default);
        drag.Outside = drag.Grid == FolderGrid && !new Rect(panel, FolderPanel.ActualSize.ToSize()).Contains(position);
        drag.GroupWith = -1;
        if (drag.Outside)
        {
            // Out of the folder's panel, into the grid behind it; the folder's apps stay as they are.
            Point point = Root.TransformToVisual(PinnedGrid).TransformPoint(position);
            drag.Target = drag.GridCentres.Count == 0 ? 0 : GridReorder.DropTarget(drag.GridCentres, (point.X, point.Y), -1, _ => false).Index;
        }
        else
        {
            Point point = Root.TransformToVisual(drag.Grid).TransformPoint(position);
            (int target, bool group) = GridReorder.DropTarget(
                [.. drag.Slots.Zip(drag.Containers, (slot, c) => (slot.X + c.ActualSize.X / 2, slot.Y + c.ActualSize.Y / 2))],
                (point.X, point.Y), drag.Index, _ => drag.CanGroup);
            drag.Target = group ? drag.Index : target;
            drag.GroupWith = group ? target : -1;
        }

        UIElement? previewed = drag.GroupWith >= 0 ? drag.Containers[drag.GroupWith] : null;
        if (previewed != drag.Previewed)
        {
            if (drag.Previewed is not null)
                PreviewGroup(drag.Previewed, on: false, animate: true);
            if (previewed is not null)
                PreviewGroup(previewed, on: true, animate: true);
            drag.Previewed = previewed;
        }
        int makeWayFor = drag.Outside ? drag.Index : drag.Target;
        for (int i = 0; i < drag.Containers.Count; i++)
        {
            (double x, double y) = i == drag.Index ? (0, 0) : GridReorder.MakeWayOffset(drag.Slots, drag.Index, makeWayFor, i);
            drag.Containers[i].Translation = new Vector3((float)x, (float)y, 0);
        }
    }

    private void EndPinDrag(PinDrag drag, bool drop)
    {
        foreach (UIElement container in drag.Containers)
        {
            container.TranslationTransition = null;
            container.Translation = default;
        }
        drag.Containers[drag.Index].Opacity = 1;
        if (drag.Previewed is not null)
            PreviewGroup(drag.Previewed, on: false, animate: false);
        DragLayer.Children.Remove(drag.Copy);
        if (!drop)
            return;

        // The pins are already drawn where they belong, so the change shows none.
        if (drag.Grid == PinnedGrid)
        {
            if (drag.GroupWith >= 0)
                SetPins(StartPins.Group(Pins, drag.Index, drag.GroupWith));
            else if (drag.Target != drag.Index)
                SetPins(StartPins.Move(Pins, drag.Index, drag.Target));
        }
        else if (OpenFolderIndex is var folder and >= 0)
        {
            if (drag.Outside)
                TakeOutOfFolder(drag.Index, drag.Target);
            else if (drag.Target != drag.Index)
                SetPins(StartPins.MoveInFolder(Pins, folder, drag.Index, drag.Target));
        }
    }

    /// <summary>
    /// An app held over another shows it as the folder about to be made, as Explorer does: the folder's plate, the
    /// app's icon shrunk into the plate's first place, its name gone.
    /// </summary>
    private static void PreviewGroup(UIElement container, bool on, bool animate)
    {
        if ((container as ContentControl)?.ContentTemplateRoot is not FrameworkElement { DataContext: StartItem { IsFolder: false } } root
            || root.FindName("FolderPlate") is not UIElement plate
            || root.FindName("AppIcon") is not UIElement icon
            || root.FindName("ItemLabel") is not UIElement label)
            return;

        plate.OpacityTransition = animate ? new ScalarTransition { Duration = s_groupPreviewDuration } : null;
        label.OpacityTransition = animate ? new ScalarTransition { Duration = s_groupPreviewDuration } : null;
        icon.ScaleTransition = animate ? new Vector3Transition { Duration = s_groupPreviewDuration } : null;
        icon.TranslationTransition = animate ? new Vector3Transition { Duration = s_groupPreviewDuration } : null;
        icon.CenterPoint = new Vector3(16, 16, 0);
        plate.Opacity = on ? 1 : 0;
        label.Opacity = on ? 0 : 1;
        // Half size, its centre on the first of the plate's four places: 9 left of and 8 above the icon's centre.
        icon.Scale = on ? new Vector3(0.5f, 0.5f, 1) : Vector3.One;
        icon.Translation = on ? new Vector3(-9, -8, 0) : Vector3.Zero;
    }

    private static List<UIElement>? Containers(GridView grid, int count)
    {
        var containers = new List<UIElement>();
        for (int i = 0; i < count; i++)
        {
            if (grid.ContainerFromIndex(i) is not UIElement container)
                return null;
            containers.Add(container);
        }
        return containers;
    }

    private static (double X, double Y) Corner(UIElement element, UIElement relativeTo)
    {
        Point corner = element.TransformToVisual(relativeTo).TransformPoint(default);
        return (corner.X, corner.Y);
    }

    private static (double X, double Y) Centre(UIElement element, UIElement relativeTo)
    {
        (double x, double y) = Corner(element, relativeTo);
        return (x + element.ActualSize.X / 2, y + element.ActualSize.Y / 2);
    }

    private sealed class PinDrag(GridView grid, List<UIElement> containers, List<(double X, double Y)> slots, int index, ContentControl copy, Point grab)
    {
        public GridView Grid { get; } = grid;
        public List<UIElement> Containers { get; } = containers;
        public List<(double X, double Y)> Slots { get; } = slots;
        public int Index { get; } = index;
        /// <summary>What follows the pointer, and where on it the pointer took hold.</summary>
        public ContentControl Copy { get; } = copy;
        public Point Grab { get; } = grab;
        /// <summary>Whether the pin may be grouped with another: an app in the grid (folders don't nest).</summary>
        public bool CanGroup { get; init; }
        /// <summary>For an app of a folder: the grid's cells, for when it's taken out of the panel.</summary>
        public List<(double X, double Y)> GridCentres { get; init; } = [];
        public int Target { get; set; }
        public int GroupWith { get; set; } = -1;
        public bool Outside { get; set; }
        public UIElement? Previewed { get; set; }
    }

    private void SetPins(IReadOnlyList<StartPin> pins)
    {
        _owner.Settings.Update(_owner.Settings.Current with { StartPins = pins });
        ShowPinned();
    }

    // The open folder, found by its id: its place changes as pins move around it.
    private int OpenFolderIndex => _folderId is null ? -1 : StartPins.IndexOf(Pins, _folderId);

    private void OpenFolder(int index)
    {
        if (index < 0 || Pins[index].Folder is not { } folder)
            return;

        _folderId = folder.Id;
        FolderNameBox.Text = StartPins.DisplayName(folder);
        ShowFolderApps();
        FolderLayer.Visibility = Visibility.Visible;
        FolderLayer.IsHitTestVisible = true;
        AnimateFolder(opening: true, null);
    }

    // The open folder's apps, and its tile hidden while the panel stands for it.
    private void ShowFolderApps()
    {
        int index = OpenFolderIndex;
        if (index < 0)
        {
            CloseFolder(animate: false);
            return;
        }

        StartFolder folder = Pins[index].Folder!;
        Sync(_folderApps, [.. folder.Apps.Select(app => new StartItem(app, "App", isApp: true, _owner.Icons))]);
        if (FolderNameBox.FocusState == FocusState.Unfocused)
            FolderNameBox.Text = StartPins.DisplayName(folder);
        PinnedGrid.UpdateLayout();
        if (_folderTile is not null)
            _folderTile.Opacity = 1;
        _folderTile = PinnedGrid.ContainerFromIndex(index) as UIElement;
        if (_folderTile is not null)
            _folderTile.Opacity = 0;
    }

    private void CloseFolder(bool animate)
    {
        if (_folderId is null)
            return;

        CommitFolderName();
        _folderId = null;
        FolderLayer.IsHitTestVisible = false;
        UIElement? tile = _folderTile;
        void Closed()
        {
            FolderLayer.Visibility = Visibility.Collapsed;
            _folderApps.Clear();
            if (tile is not null)
                tile.Opacity = 1;
            if (_folderTile == tile)
                _folderTile = null;
        }

        if (animate)
        {
            AnimateFolder(opening: false, Closed);
        }
        else
        {
            _folderAnimation?.Stop();
            Closed();
        }
    }

    // The panel grows out of the folder's tile, from the tile's size, and shrinks back into it.
    private void AnimateFolder(bool opening, Action? done)
    {
        _folderAnimation?.Stop();
        double fromX = 0, fromY = 0;
        if (_folderTile is not null && ItemIcon(_folderTile) is FrameworkElement icon)
        {
            Point tile = icon.TransformToVisual(Root).TransformPoint(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2));
            fromX = tile.X - Root.ActualWidth / 2;
            fromY = tile.Y - Root.ActualHeight / 2;
        }
        double small = 38 / FolderPanel.Width;
        // Each key frame needs a curve of its own.
        KeySpline Curve() => opening
            ? new KeySpline { ControlPoint1 = new Point(0, 0), ControlPoint2 = new Point(0, 1) }
            : new KeySpline { ControlPoint1 = new Point(0.4, 0), ControlPoint2 = new Point(1, 1) };
        TimeSpan duration = opening ? s_folderOpenDuration : s_folderCloseDuration;

        var story = new Storyboard();
        foreach ((string property, double collapsed, double open) in (ReadOnlySpan<(string, double, double)>)
            [("ScaleX", small, 1), ("ScaleY", small, 1), ("TranslateX", fromX, 0), ("TranslateY", fromY, 0)])
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = opening ? collapsed : open });
            animation.KeyFrames.Add(new SplineDoubleKeyFrame { KeyTime = duration, Value = opening ? open : collapsed, KeySpline = Curve() });
            Storyboard.SetTarget(animation, FolderTransform);
            Storyboard.SetTargetProperty(animation, property);
            story.Children.Add(animation);
        }
        if (done is not null)
            story.Completed += (_, _) => done();
        _folderAnimation = story;
        story.Begin();
    }

    private void FolderLayer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // A click beside the panel closes it, and does nothing else.
        if (ReferenceEquals(e.OriginalSource, FolderLayer))
        {
            CloseFolder(animate: true);
            e.Handled = true;
        }
    }

    /// <summary>An app of the open folder put in the grid at <paramref name="to"/>; the folder closes once it's empty.</summary>
    private void TakeOutOfFolder(int index, int to)
    {
        int folder = OpenFolderIndex;
        if (_folderApps.Count == 1)
            CloseFolder(animate: true);
        SetPins(StartPins.TakeOut(Pins, folder, index, to));
    }

    private void FolderNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitFolderName();
            FolderGrid.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape && OpenFolderIndex is var index and >= 0)
        {
            // Back to the name it had; Start's own Esc handling then closes the folder.
            FolderNameBox.Text = StartPins.DisplayName(Pins[index].Folder!);
        }
    }

    private void FolderNameBox_LostFocus(object sender, RoutedEventArgs e) => CommitFolderName();

    private void CommitFolderName()
    {
        int index = OpenFolderIndex;
        if (index < 0)
            return;

        StartFolder folder = Pins[index].Folder!;
        string name = FolderNameBox.Text.Trim();
        if (name == folder.Name || name == StartPins.DisplayName(folder))
            FolderNameBox.Text = StartPins.DisplayName(folder);
        else
            SetPins(StartPins.Rename(Pins, index, name));
    }

    private void AddPlaceButtons()
    {
        foreach ((StartPlace place, string name, string glyph, string automationId, _) in s_places)
        {
            var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 16 }, Visibility = Visibility.Collapsed };
            AutomationProperties.SetAutomationId(button, automationId);
            AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, name);
            button.Click += (_, _) => OpenPlace(place);
            PlacesPanel.Children.Add(button);
            _placeButtons[place] = button;
        }
    }

    // Read each time Start opens, so a change made in Settings shows the next time, as in Explorer.
    private void ShowPlaces()
    {
        IReadOnlyList<StartPlace> shown;
        try
        {
            shown = StartPlaces.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn("Could not read Start's folders", ex);
            shown = [];
        }
        foreach ((StartPlace place, Button button) in _placeButtons)
            button.Visibility = shown.Contains(place) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenPlace(StartPlace place)
    {
        (_, string name, _, _, string? folder) = s_places.First(p => p.Place == place);
        Hide();
        if (place == StartPlace.Settings)
            Launcher.OpenSettings(_owner.RunMode, name, "ms-settings:");
        else if (place == StartPlace.FileExplorer)
            Launcher.OpenFileExplorer();
        else
            Launcher.Launch(new PinnedApp(name, Path: folder));
    }

    private void PersonalizePlaces_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        Launcher.OpenSettings(_owner.RunMode, "Start folders", "ms-settings:personalization-start-places");
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

    // Read each time Start opens, as Explorer's: the power button hides by policy and has a dot while an update waits.
    private void ShowPowerButton()
    {
        PowerOptions options = PowerOptions.Read();
        PowerButton.Visibility = options.PowerButtonHidden ? Visibility.Collapsed : Visibility.Visible;
        bool update = options.RebootRequired
            && options.Choices(PowerMenu.Start).Any(c => c is PowerChoice.UpdateAndShutDown or PowerChoice.ShutDown or PowerChoice.UpdateAndRestart or PowerChoice.Restart);
        PowerGlyph.Glyph = update ? PowerItems.Glyph(PowerChoice.UpdateAndShutDown) : PowerItems.Glyph(PowerChoice.ShutDown);
        PowerUpdateDot.Visibility = update ? Visibility.Visible : Visibility.Collapsed;
    }

    // Filled as it opens from what Windows offers now: Lock, the power states, the update choices.
    private void PowerMenu_Opening(object sender, object e)
    {
        PowerOptions options = PowerOptions.Read();
        IReadOnlyList<PowerChoice> choices = options.Choices(PowerMenu.Start);
        PowerFlyout.Items.Clear();
        foreach (PowerChoice choice in choices)
        {
            var item = new MenuFlyoutItem { Text = PowerItems.Name(choice, options), Icon = PowerItems.Icon(choice) };
            AutomationProperties.SetAutomationId(item, PowerItems.AutomationId(choice));
            // Explorer's Lock is Start's own item, without shutdownux's description.
            if (choice != PowerChoice.Lock)
                ToolTipService.SetToolTip(item, PowerItems.Description(choice));
            item.Click += (_, _) =>
            {
                bool shift = PowerItems.IsShiftDown();
                Hide();
                PowerItems.Run(choice, shift);
            };
            PowerFlyout.Items.Add(item);
        }
        if (choices.Count == 0)
            PowerFlyout.Items.Add(new MenuFlyoutItem { Text = "There are currently no power options available.", IsEnabled = false });
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, string automationId, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = glyph.Length > 0 ? new FontIcon { Glyph = glyph } : null };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => onClick();
        return item;
    }
}
