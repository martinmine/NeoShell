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
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using NeoShell.Interop.Search;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.Taskbar;
using Windows.Graphics;
using Windows.System;

namespace NeoShell.StartMenu;

/// <summary>Items under one heading: a letter in All apps, a kind of result in search.</summary>
internal sealed class StartGroup(string key, IEnumerable<StartItem> items) : List<StartItem>(items)
{
    public string Key { get; } = key;
}

/// <summary>
/// The Start menu: pinned apps, All apps, search over apps and the Windows Search index, and the user, Settings,
/// Switch to Explorer and power buttons. Created once and shown above whichever taskbar opened it.
/// </summary>
internal sealed partial class StartMenuWindow : Window
{
    // Effective pixels.
    private const double MenuWidth = 640;
    private const double MenuHeight = 720;
    private const double Gap = 12;

    private const int MaxAppResults = 8;
    private const int MaxFileResults = 20;
    private static readonly TimeSpan s_searchDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan s_catalogLifetime = TimeSpan.FromMinutes(2);

    private readonly Taskbars _owner;
    private readonly nint _hwnd;
    private readonly AcrylicBackdrop _backdrop = new();
    private readonly FramelessWindow _frameless;
    private readonly PinnedWindow _placement;
    private readonly ObservableCollection<StartItem> _pinned = [];
    private readonly CollectionViewSource _allApps = new() { IsSourceGrouped = true };
    private readonly CollectionViewSource _results = new() { IsSourceGrouped = true };
    private readonly ObservableCollection<StartGroup> _resultGroups = [];
    private List<StartItem> _apps = [];
    private List<StartItem> _resultItems = [];
    private DateTime _appsLoadedAt = DateTime.MinValue;
    private nint _previousForeground;
    private long _deactivatedAt;
    private bool _loadingApps;
    private CancellationTokenSource? _search;

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

        PinnedGrid.ItemsSource = _pinned;
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
            owner.Icons.Loaded -= RefreshIcons;
            _placement.Dispose();
            _frameless.Dispose();
        };

        LoadAppsIfStale();
    }

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Pressing the Start button deactivates Start before the button's click arrives; that click must not reopen it.
    /// </summary>
    public bool WasJustDeactivated => Environment.TickCount64 - _deactivatedAt < 400;

    /// <summary>Opens above <paramref name="taskbar"/>: centred on the monitor, or at its left like the taskbar items.</summary>
    public void Show(DisplayMonitor monitor, RectInt32 taskbar, bool centered, ElementTheme theme)
    {
        Root.RequestedTheme = theme;
        _backdrop.Theme = theme;
        ShowPinned();
        LoadAppsIfStale();

        double scale = monitor.Dpi / 96.0;
        int gap = (int)(Gap * scale);
        int width = (int)(MenuWidth * scale);
        int height = Math.Min((int)(MenuHeight * scale), taskbar.Y - monitor.Bounds.Y - 2 * gap);
        int x = centered ? monitor.Bounds.X + (monitor.Bounds.Width - width) / 2 : taskbar.X + gap;
        _placement.Bounds = new RectInt32(x, taskbar.Y - height - gap, width, height);

        IsOpen = true;
        _previousForeground = TopLevelWindows.GetForeground();
        AppWindow.Show();
        // Window.Activate alone doesn't take the foreground from the app the user was in; SetForegroundWindow does,
        // because the click on the taskbar (or the Win key) was the last input.
        TopLevelWindows.Activate(_hwnd);
        Activate();
        // Focus once the window is active, so typing goes straight into the search box.
        DispatcherQueue.Post(() => SearchBox.Focus(FocusState.Programmatic));
    }

    public void Hide() => Hide(restoreForeground: true);

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
        AppWindow.Hide();
        SearchBox.Text = "";
        ShowView(HomeView);
    }

    private void ShowView(FrameworkElement view)
    {
        HomeView.Visibility = view == HomeView ? Visibility.Visible : Visibility.Collapsed;
        AllAppsView.Visibility = view == AllAppsView ? Visibility.Visible : Visibility.Collapsed;
        SearchView.Visibility = view == SearchView ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPinned()
    {
        _pinned.Clear();
        foreach (PinnedApp app in _owner.Settings.Current.PinnedStartApps)
            _pinned.Add(new StartItem(app, "App", isApp: true, _owner.Icons));
        NoPinnedText.Visibility = _pinned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        foreach (StartItem item in _pinned.Concat(_apps).Concat(_resultItems))
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
        if (e.ClickedItem is StartItem item)
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
        bool onStart = _owner.Settings.Current.PinnedStartApps.Any(p => TaskGrouping.SameApp(p, app));
        bool onTaskbar = _owner.Settings.Current.PinnedTaskbarApps.Any(p => TaskGrouping.SameApp(p, app));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(onStart
            ? MenuItem("Unpin from Start", "StartUnpinMenuItem", () => SetPinnedStartApps([.. _owner.Settings.Current.PinnedStartApps.Where(p => !TaskGrouping.SameApp(p, app))]))
            : MenuItem("Pin to Start", "StartPinMenuItem", () => SetPinnedStartApps([.. _owner.Settings.Current.PinnedStartApps, app])));
        menu.Items.Add(onTaskbar
            ? MenuItem("Unpin from taskbar", "StartUnpinTaskbarMenuItem", () => _owner.Unpin(app))
            : MenuItem("Pin to taskbar", "StartPinTaskbarMenuItem", () => _owner.Pin(app)));
    }

    private void PinnedGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        SetPinnedStartApps([.. _pinned.Select(item => item.Target)]);

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
