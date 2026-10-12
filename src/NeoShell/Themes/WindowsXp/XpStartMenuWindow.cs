using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.StartMenu;
using NeoShell.Taskbar;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using static NeoShell.Themes.WindowsXp.XpStartMenuPainter;

namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// Windows XP's Start menu (see docs/design/theme-xp.md): the user at the top, Start's pinned apps and the most used
/// programs on the left above All Programs (the Start Menu folders as cascading menus), the places on the right, Log Off
/// and Turn Off Computer at the bottom. It opens at once above the Start button, as XP's did.
/// </summary>
internal sealed class XpStartMenuWindow : IStartMenuView
{
    private const int MostUsedCount = 6;
    private static readonly TimeSpan s_menuDelay = TimeSpan.FromMilliseconds(400);

    // XP's right column: the places, each opening the real folder or tool.
    private static readonly (string Key, string Text, bool Bold, string Target, string Icon, string Tip)[][] s_places =
    [
        [
            ("documents", "My Documents", true, "shell:Personal", "shell:Personal", "Opens the My Documents folder, where you can store letters, reports, notes, and other kinds of documents."),
            ("pictures", "My Pictures", true, "shell:My Pictures", "shell:My Pictures", "Opens the My Pictures folder, where you can store digital photos, images, and graphics files."),
            ("music", "My Music", true, "shell:My Music", "shell:My Music", "Opens the My Music folder, where you can store music and other audio files."),
            ("computer", "My Computer", true, "shell:MyComputerFolder", "shell:MyComputerFolder", "Gives access to, and information about, the disk drives, cameras, scanners, and other hardware connected to your computer."),
        ],
        [
            ("controlpanel", "Control Panel", false, "shell:ControlPanelFolder", "shell:ControlPanelFolder", "Provides options for you to customize the appearance and functionality of your computer, add or remove programs, and set up network connections and user accounts."),
            ("printers", "Printers and Faxes", false, "shell:PrintersFolder", "shell:PrintersFolder", "Shows installed printers and fax printers and helps you add new ones."),
        ],
        [
            ("help", "Help and Support", false, "ms-contact-support:", @"%SystemRoot%\System32\shell32.dll,23", "Opens a central location for Help topics, tutorials, troubleshooting, and other support services."),
            ("search", "Search", false, "search-ms:", @"%SystemRoot%\System32\shell32.dll,22", "Opens a window where you can pick search options and work with search results."),
            ("run", "Run...", false, "", @"%SystemRoot%\System32\shell32.dll,24", "Opens a program, folder, document, or Web site."),
        ],
    ];

    private readonly Taskbars _owner;
    private readonly ThemePopup _popup = new(activates: true);
    private readonly XpToolTip _toolTip = new();
    private readonly DispatcherQueueTimer _menuTimer;
    private readonly Dictionary<string, Button> _hits = [];
    private List<PinnedApp> _catalog = [];
    private DateTime _catalogLoaded = DateTime.MinValue;
    private List<PinnedApp> _pinned = [];
    private List<PinnedApp> _mostUsed = [];
    private XpStartLayout? _layout;
    private ITaskbarView? _taskbar;
    private XpMenu? _programs;
    private string? _hot;
    private nint _previousForeground;
    private long _deactivatedAt;
    private string _userName = "";

    public XpStartMenuWindow(Taskbars owner)
    {
        _owner = owner;
        _menuTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _menuTimer.IsRepeating = false;
        _menuTimer.Interval = s_menuDelay;
        _menuTimer.Tick += (_, _) =>
        {
            if (_hot == "allprograms")
                OpenAllPrograms();
        };
        _popup.Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && IsOpen)
            {
                _deactivatedAt = Environment.TickCount64;
                Hide(restoreForeground: false);
            }
        };
        _popup.Root.KeyDown += Root_KeyDown;
        _popup.Root.PointerExited += (_, _) => SetHot(_programs is { IsOpen: true } ? "allprograms" : null);
        _ = LoadCatalog();
    }

    public bool IsOpen { get; private set; }

    public bool WasJustDeactivated => Environment.TickCount64 - _deactivatedAt < 400;

    public void Show(ITaskbarView taskbar)
    {
        _taskbar = taskbar;
        if (DateTime.UtcNow - _catalogLoaded > TimeSpan.FromMinutes(2))
            _ = LoadCatalog();
        _userName = UserAccount.DisplayName;
        _hot = null;
        Build();
        Paint();

        double scale = taskbar.Monitor.Dpi / 96.0;
        RectInt32 bar = taskbar.ScreenBounds;
        int height = (int)Math.Round(_layout!.Height * scale);
        _previousForeground = TopLevelWindows.GetForeground();
        IsOpen = true;
        _popup.ShowAt(new PointInt32(bar.X, bar.Y - height), scale);
        (taskbar as XpTaskbarWindow)?.ShowStartOpen(true);
        _ = ShowPicture();
    }

    public void Hide() => Hide(restoreForeground: true);

    public void Close()
    {
        Hide(restoreForeground: false);
        _toolTip.Shut();
        _popup.Shut();
    }

    private void Hide(bool restoreForeground)
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        _menuTimer.Stop();
        _toolTip.Cancel();
        _programs?.CloseAll();
        _programs = null;
        if (restoreForeground && TopLevelWindows.GetForeground() == _popup.Handle && _previousForeground != 0 && TopLevelWindows.Exists(_previousForeground))
            TopLevelWindows.Activate(_previousForeground);
        _popup.Hide();
        (_taskbar as XpTaskbarWindow)?.ShowStartOpen(false);
    }

    private async Task LoadCatalog()
    {
        _catalogLoaded = DateTime.UtcNow;
        try
        {
            _catalog = await Task.Run(() => AppCatalog.Load().Select(StartCatalog.ToPinnedApp).ToList());
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the apps for XP's Start", ex);
        }
    }

    // The left column: Start's pinned apps, then the most used of the others by Windows' launch history.
    private void Build()
    {
        _pinned = [.. _owner.Settings.Current.StartPins.Where(pin => pin.App is not null).Select(pin => pin.App!).Take(4)];
        IReadOnlyList<AppUsage> usage = [];
        try
        {
            usage = UserAssist.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            Log.Warn("Could not read the launch history", ex);
        }
        IReadOnlyDictionary<PinnedApp, double> scores = AllApps.UsageScores(_catalog, usage);
        _mostUsed = [.. scores.Where(score => score.Value >= 1 && !_pinned.Any(pin => TaskGrouping.SameApp(pin, score.Key)))
            .OrderByDescending(score => score.Value).Select(score => score.Key).Take(MostUsedCount)];

        _layout = Layout(
            [.. _pinned.Select(app => (app.DisplayName, (string?)null))],
            [.. _mostUsed.Select(app => app.DisplayName)],
            [.. s_places.Select(group => (IReadOnlyList<(string, string, bool)>)[.. group.Select(place => ($"place:{place.Key}", place.Text, place.Bold))])]);
        PlaceElements();
    }

    private void PlaceElements()
    {
        _popup.Overlay.Children.Clear();
        _hits.Clear();
        XpStartLayout layout = _layout!;
        foreach (XpStartItem item in layout.Items)
        {
            bool left = item.Column == XpStartColumn.Left;
            string automationId = item.Key.StartsWith("pinned:", StringComparison.Ordinal) ? "StartPinnedItem"
                : item.Key.StartsWith("mfu:", StringComparison.Ordinal) ? "StartMostUsedItem"
                : $"{item.Key[6..]}PlaceButton";
            AddHit(item.Key, automationId, item.Text, left ? LeftHighlightX : RightHighlightX, item.Top,
                left ? LeftHighlightWidth : RightHighlightWidth, item.Height);
            Task<ImageSource?>? icon = left ? ThemeIcons.Of(AppOf(item.Key)!, 32) : PlaceIcon(item.Key);
            if (icon is not null)
                _ = AddIcon(icon, left ? LeftIconX : RightIconX, left ? item.Top + 2 : item.Top + 1, left ? 32 : 24);
        }
        AddHit("allprograms", "AllProgramsButton", "All Programs", LeftHighlightX, layout.AllProgramsTop, AllProgramsWidth, 24);
        AddHit("logoff", "LogOffButton", "Log Off", 172, layout.BottomTop + 4, 70, 32);
        AddHit("turnoff", "TurnOffButton", "Turn Off Computer", 247, layout.BottomTop + 4, 126, 32);
    }

    private Task<ImageSource?>? PlaceIcon(string key)
    {
        var place = s_places.SelectMany(group => group).First(place => $"place:{place.Key}" == key);
        return place.Icon.Contains(',') ? ThemeIcons.FromResource(place.Icon, 24) : ThemeIcons.Of(place.Icon, 24);
    }

    private async Task AddIcon(Task<ImageSource?> icon, int x, int y, int size)
    {
        if (await icon is not { } source)
            return;
        var image = new Image { Source = source, IsHitTestVisible = false };
        ThemeHit.Place(image, x, y, size, size);
        _popup.Overlay.Children.Insert(0, image);
    }

    private async Task ShowPicture()
    {
        if (await UserAccount.LoadPictureOrDefaultAsync(48) is not { } picture)
            return;
        var image = new Image { Source = picture, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
        ThemeHit.Place(image, 9, 9, 48, 48);
        _popup.Overlay.Children.Insert(0, image);
    }

    private void AddHit(string key, string automationId, string name, int x, int y, int width, int height)
    {
        Button hit = ThemeHit.Create(automationId, name);
        ThemeHit.Place(hit, x, y, width, height);
        hit.PointerEntered += (_, _) => Enter(key);
        hit.PointerExited += (_, _) => _toolTip.Cancel();
        hit.Click += (_, _) => Choose(key);
        _hits[key] = hit;
        _popup.Overlay.Children.Add(hit);
    }

    private PinnedApp? AppOf(string key)
    {
        int colon = key.IndexOf(':');
        if (colon < 0 || !int.TryParse(key[(colon + 1)..], out int index))
            return null;
        List<PinnedApp> list = key.StartsWith("pinned:", StringComparison.Ordinal) ? _pinned : key.StartsWith("mfu:", StringComparison.Ordinal) ? _mostUsed : [];
        return index < list.Count ? list[index] : null;
    }

    private void Enter(string key)
    {
        SetHot(key);
        _menuTimer.Stop();
        if (key == "allprograms")
        {
            _menuTimer.Start();
        }
        else if (_programs is { IsOpen: true })
        {
            _programs.CloseAll();
            _programs = null;
        }
        string? tip = key switch
        {
            "logoff" => "Provides options for closing your programs and logging off, or for leaving your programs running and switching to another user.",
            "turnoff" => "Provides options for turning off or restarting your computer, or for activating Stand By or Hibernate modes.",
            _ => s_places.SelectMany(group => group).Where(place => $"place:{place.Key}" == key).Select(place => place.Tip).FirstOrDefault(),
        };
        if (tip is not null && _taskbar is { } taskbar)
        {
            _toolTip.Request(tip, () =>
            {
                PointInt32 pointer = Cursor.Position();
                return (new PointInt32(pointer.X, pointer.Y + (int)(20 * taskbar.Monitor.Dpi / 96.0)), false, taskbar.Monitor);
            });
        }
    }

    private void SetHot(string? key)
    {
        if (_hot == key)
            return;
        _hot = key;
        Paint();
    }

    private void Paint()
    {
        if (_layout is not null)
            _popup.Draw(XpStartMenuPainter.Paint(_layout, _userName, _hot));
    }

    private void Choose(string key)
    {
        _toolTip.Cancel();
        if (key == "allprograms")
        {
            OpenAllPrograms();
            return;
        }
        DisplayMonitor? monitor = _taskbar?.Monitor;
        Hide(restoreForeground: false);
        if (AppOf(key) is { } app)
        {
            Launcher.Launch(app);
        }
        else if (key == "logoff" || key == "turnoff")
        {
            if (monitor is not null)
                XpPowerDialog.Open(key == "turnoff", monitor);
        }
        else if (key == "place:run")
        {
            _owner.ShowRunDialog();
        }
        else if (s_places.SelectMany(group => group).FirstOrDefault(place => $"place:{place.Key}" == key) is { Target.Length: > 0 } place)
        {
            Open(place.Target);
        }
    }

    private static void Open(string target)
    {
        try
        {
            ShellLaunch.Open(target);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {target}", ex);
        }
    }

    // All Programs: the Start Menu folders' own shortcuts, a line, then the Programs folders, cascading.
    private void OpenAllPrograms()
    {
        _menuTimer.Stop();
        if (_programs is { IsOpen: true } || _taskbar is not { } taskbar || _layout is not { } layout)
            return;

        (IReadOnlyList<StartMenuEntry> top, IReadOnlyList<StartMenuEntry> programs) = StartMenuFolders.Load();
        List<XpMenuItem> items = [.. top.Select(Item)];
        if (items.Count > 0)
            items.Add(XpMenuItem.Separator);
        items.AddRange(programs.Select(Item));
        double scale = taskbar.Monitor.Dpi / 96.0;
        RectInt32 bounds = _popup.ScreenBounds;
        var anchor = new RectInt32(bounds.X + (int)Math.Round(LeftHighlightX * scale), bounds.Y + (int)Math.Round(layout.AllProgramsTop * scale),
            (int)Math.Round(AllProgramsWidth * scale), (int)Math.Round(24 * scale));
        _programs = XpMenu.Show(items, XpMenuStyle.Programs, taskbar.Monitor, anchor, XpMenuPlacement.Beside, catchClicks: false);
    }

    private XpMenuItem Item(StartMenuEntry entry) => entry.IsFolder
        ? new XpMenuItem
        {
            Text = entry.Name,
            Submenu = () => [.. entry.Children.Select(Item)],
            Icon = () => ThemeIcons.Of(@"C:\Windows", 16),
            AutomationId = "AllProgramsFolder",
        }
        : new XpMenuItem
        {
            Text = entry.Name,
            Icon = () => ThemeIcons.Of(entry.Path!, 16),
            Invoke = () =>
            {
                Hide(restoreForeground: false);
                Open(entry.Path!);
            },
            AutomationId = "AllProgramsItem",
        };

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_programs is { IsOpen: true } programs && programs.HandleKey(e.Key))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }
}
