using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using NeoShell.Interop.Network;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.StartMenu;
using NeoShell.Taskbar;
using NeoShell.Widgets;
using Windows.Graphics;
using Windows.UI;
using VirtualKey = Windows.System.VirtualKey;

namespace NeoShell.Themes.Concept2088;

/// <summary>
/// The 2088 concept's Start, Nexus (see docs/design/theme-2088.md): an ask line that takes what you want (an app's
/// name, or a command such as "lock" or "restart"), the pinned modules, the machine's telemetry, the most used apps
/// and the power controls, in one panel above the Spine.
/// </summary>
internal sealed class NexusStartMenu : IStartMenuView
{
    private const double PanelWidth = 880;
    private const double PanelHeight = 640;
    private const int ModuleColumns = 5;
    private const int ModuleRows = 3;
    private const int ResultLimit = 9;

    private readonly Taskbars _owner;
    private readonly ThemePopup _popup = new(activates: true);
    private readonly HudFrame _panel = new(28, Hud.Panel, Hud.LineBright) { MarkerLength = 16, Width = PanelWidth, Height = PanelHeight };
    private readonly TextBox _ask = new();
    private readonly TextBlock _operator = Hud.Label("", 15, Hud.Text, spacing: 120, bold: true);
    private readonly Image _picture = new() { Width = 40, Height = 40 };
    private readonly TextBlock _contentTitle = Hud.Label("", 11, Hud.Text, spacing: 200, bold: true);
    private readonly TextBlock _contentCount = Hud.Label("", 9, Hud.Faint, Hud.Mono, spacing: 80);
    private readonly Grid _modules = new() { RowSpacing = 8, ColumnSpacing = 8 };
    private readonly StackPanel _results = new() { Spacing = 4 };
    private readonly ScrollViewer _resultsScroll = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _recent = new() { Spacing = 2 };
    private readonly SegmentBar _cpuBar = new(14, 9, 10);
    private readonly SegmentBar _memoryBar = new(14, 9, 10);
    private readonly TextBlock _cpu = Hud.Label("--", 11, Hud.Text, Hud.Mono);
    private readonly TextBlock _memory = Hud.Label("--", 11, Hud.Text, Hud.Mono);
    private readonly TextBlock _uptime = Hud.Label("", 11, Hud.Text, Hud.Mono);
    private readonly TextBlock _link = Hud.Label("", 11, Hud.Text, Hud.Mono);
    private readonly TextBlock _allLabel = Hud.Label("ALL MODULES", 11, Hud.Text, spacing: 160, bold: true);
    private readonly TextBlock _allJapanese = Hud.Label("全て ›", 10, Hud.Red, Hud.Japanese);
    private readonly Storyboard _open = new();
    private readonly TranslateTransform _slide = new();
    private List<PinnedApp> _catalog = [];
    private DateTime _catalogLoaded;
    private List<Result> _shown = [];
    private int _selected;
    private bool _showAll;
    private ITaskbarView? _taskbar;
    private nint _previousForeground;
    private long _deactivatedAt;

    public NexusStartMenu(Taskbars owner)
    {
        _owner = owner;
        _popup.Root.RequestedTheme = ElementTheme.Dark;
        _popup.Root.Children.Add(_panel);
        _panel.HorizontalAlignment = HorizontalAlignment.Left;
        _panel.VerticalAlignment = VerticalAlignment.Top;
        _panel.RenderTransform = _slide;
        _panel.Children.Add(Body());
        _popup.Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && IsOpen)
            {
                _deactivatedAt = Environment.TickCount64;
                Hide(restoreForeground: false);
            }
        };
        _popup.Root.KeyDown += Root_KeyDown;
        OpeningAnimation();
        _ = LoadCatalog();
    }

    public bool IsOpen { get; private set; }

    public bool WasJustDeactivated => Environment.TickCount64 - _deactivatedAt < 400;

    public void Show(ITaskbarView taskbar)
    {
        _taskbar = taskbar;
        if (DateTime.UtcNow - _catalogLoaded > TimeSpan.FromMinutes(2))
            _ = LoadCatalog();
        _operator.Text = UserAccount.DisplayName.ToUpperInvariant();
        _ = ShowPicture();
        _ask.Text = "";
        _showAll = false;
        FillModules();
        FillRecent();
        ShowContent();
        UpdateTelemetry();
        Hud.Resources.Sampled += UpdateTelemetry;

        // Above the Spine at its left, a gap of 8 between, as a module slid out of it.
        double scale = taskbar.Monitor.Dpi / 96.0;
        RectInt32 bar = taskbar.ScreenBounds;
        int width = (int)Math.Ceiling(PanelWidth * scale), height = (int)Math.Ceiling(PanelHeight * scale);
        int gap = (int)Math.Round(8 * scale);
        _previousForeground = TopLevelWindows.GetForeground();
        IsOpen = true;
        _popup.ShowIn(new RectInt32(bar.X + gap, bar.Y - gap - height, width, height));
        _open.Begin();
        _ask.Focus(FocusState.Programmatic);
        (taskbar as SpineTaskbarWindow)?.ShowStartOpen(true);
    }

    public void Hide() => Hide(restoreForeground: true);

    public void Close()
    {
        Hide(restoreForeground: false);
        _popup.Shut();
    }

    private void Hide(bool restoreForeground)
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        Hud.Resources.Sampled -= UpdateTelemetry;
        if (restoreForeground && TopLevelWindows.GetForeground() == _popup.Handle && _previousForeground != 0 && TopLevelWindows.Exists(_previousForeground))
            TopLevelWindows.Activate(_previousForeground);
        _popup.Hide();
        (_taskbar as SpineTaskbarWindow)?.ShowStartOpen(false);
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
            Log.Warn("Could not read the apps for Nexus", ex);
        }
    }

    private async Task ShowPicture() => _picture.Source = await UserAccount.LoadPictureOrDefaultAsync(48);

    // Rises 14 pixels into place while it fades in, as a part sliding out of the bar.
    private void OpeningAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var rise = new DoubleAnimation { From = 14, To = 0, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = ease };
        Storyboard.SetTarget(rise, _slide);
        Storyboard.SetTargetProperty(rise, "Y");
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(140) };
        Storyboard.SetTarget(fade, _panel);
        Storyboard.SetTargetProperty(fade, "Opacity");
        _open.Children.Add(rise);
        _open.Children.Add(fade);
    }

    private Grid Body()
    {
        var body = new Grid { Margin = new Thickness(28, 22, 28, 22), RowSpacing = 14 };
        foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            body.RowDefinitions.Add(new RowDefinition { Height = height });
        Place(body, Header(), 0);
        Place(body, Divider(), 1);
        Place(body, AskLine(), 2);
        Place(body, Content(), 3);
        Place(body, Footer(), 4);
        return body;
    }

    private static void Place(Grid grid, FrameworkElement element, int row, int column = 0)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private FrameworkElement Header()
    {
        var header = new Grid();
        var title = new StackPanel { Spacing = 8 };
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        name.Children.Add(Hud.Label("NEXUS", 34, Hud.Text, spacing: 300, bold: true));
        name.Children.Add(Hud.Label("起動ノード", 16, Hud.Red, Hud.Japanese));
        title.Children.Add(name);
        title.Children.Add(Hud.Label("LAUNCH NODE · WHAT YOU NEED, NOT WHERE IT IS", 9, Hud.Dim, Hud.Mono, spacing: 120));
        header.Children.Add(title);

        var who = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        caption.Children.Add(Hud.Label("OPERATOR", 8, Hud.Faint, Hud.Mono, spacing: 150));
        caption.Children.Add(Hud.Label("操作者", 8, Hud.Red, Hud.Japanese));
        who.Children.Add(caption);
        _operator.HorizontalAlignment = HorizontalAlignment.Right;
        who.Children.Add(_operator);
        var operatorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        operatorRow.Children.Add(who);
        var pictureFrame = new HudFrame(8, Hud.Void, Hud.Red) { Width = 48, Height = 48, MarkerLength = 0 };
        pictureFrame.Children.Add(_picture);
        operatorRow.Children.Add(pictureFrame);
        Button account = Hud.Plain("UserButton", "Account settings", operatorRow);
        account.HorizontalAlignment = HorizontalAlignment.Right;
        account.Click += (_, _) => Run(() => Launcher.OpenSettings(_owner.RunMode, "Your info", "ms-settings:yourinfo", "nusrmgr.cpl"));
        header.Children.Add(account);
        return header;
    }

    private static FrameworkElement Divider()
    {
        var divider = new Grid { Height = 18 };
        var ruler = new Canvas { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
        divider.SizeChanged += (_, e) => Hud.DrawRuler(ruler, e.NewSize.Width, Hud.Faint);
        divider.Children.Add(new Rectangle { Height = 1, Fill = Hud.Solid(Hud.Line), VerticalAlignment = VerticalAlignment.Top });
        divider.Children.Add(ruler);
        var left = Hud.Label("SYS://NEOSHELL/NEXUS   BUILD 88.10   KERNEL NOMINAL", 8, Hud.Faint, Hud.Mono, spacing: 100);
        left.VerticalAlignment = VerticalAlignment.Bottom;
        divider.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        right.Children.Add(Hud.Label("接続済", 9, Hud.Red, Hud.Japanese));
        right.Children.Add(Hud.Label("LINKED ●", 8, Hud.Red, Hud.Mono, spacing: 100));
        divider.Children.Add(right);
        return divider;
    }

    private FrameworkElement AskLine()
    {
        var frame = new HudFrame(12, Hud.Void, Hud.Red) { Height = 60, MarkerLength = 10 };
        var row = new Grid { Margin = new Thickness(18, 0, 18, 0), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(row, Hud.Label("›", 28, Hud.Red, Hud.Mono, bold: true), 0, 0);

        // Only the text: the box's own plates and outlines would fight the frame around it.
        foreach (string key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            _ask.Resources[key] = Hud.Solid(Microsoft.UI.Colors.Transparent);
        foreach (string key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            _ask.Resources[key] = Hud.Solid(Hud.Text);
        foreach (string key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
            _ask.Resources[key] = Hud.Solid(Hud.Faint);
        _ask.PlaceholderText = "WHAT DO YOU NEED?   何が必要ですか";
        _ask.FontFamily = Hud.Display;
        _ask.FontSize = 22;
        _ask.CharacterSpacing = 60;
        _ask.BorderThickness = new Thickness(0);
        _ask.Padding = new Thickness(0, 6, 0, 0);
        _ask.VerticalAlignment = VerticalAlignment.Center;
        _ask.SelectionHighlightColor = Hud.Solid(Hud.Red);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_ask, "StartSearchBox");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_ask, "Ask Nexus");
        _ask.TextChanged += (_, _) =>
        {
            _selected = 0;
            ShowContent();
        };
        Place(row, _ask, 0, 1);

        var hint = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        hint.Children.Add(Hud.Label("⏎ EXECUTE", 9, Hud.Dim, Hud.Mono, spacing: 100));
        var japanese = Hud.Label("実行 · ESC 閉じる", 9, Hud.Faint, Hud.Japanese);
        hint.Children.Add(japanese);
        Place(row, hint, 0, 2);
        frame.Children.Add(row);
        return frame;
    }

    private FrameworkElement Content()
    {
        var content = new Grid { ColumnSpacing = 28 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(268) });

        var left = new Grid { RowSpacing = 12 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new Grid();
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new Rectangle { Width = 4, Height = 12, Fill = Hud.Solid(Hud.Red) });
        title.Children.Add(_contentTitle);
        heading.Children.Add(title);
        _contentCount.HorizontalAlignment = HorizontalAlignment.Right;
        heading.Children.Add(_contentCount);
        Place(left, heading, 0);
        for (int i = 0; i < ModuleColumns; i++)
            _modules.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < ModuleRows; i++)
            _modules.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Place(left, _modules, 1);
        _resultsScroll.Content = _results;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_resultsScroll, "SearchResults");
        Place(left, _resultsScroll, 1);
        Place(content, left, 0, 0);

        var right = new StackPanel { Spacing = 10 };
        right.Children.Add(Hud.Heading("TELEMETRY", "遠隔測定", Hud.Text));
        right.Children.Add(Meter("CPU", _cpuBar, _cpu));
        right.Children.Add(Meter("MEM", _memoryBar, _memory));
        right.Children.Add(Readout("UPTIME", _uptime));
        right.Children.Add(Readout("LINK", _link));
        right.Children.Add(new Rectangle { Height = 1, Fill = Hud.Solid(Hud.Line), Margin = new Thickness(0, 6, 0, 2) });
        right.Children.Add(Hud.Heading("RECENT", "最近", Hud.Text));
        right.Children.Add(_recent);
        right.Children.Add(SerialPlate());
        Place(content, right, 0, 1);
        return content;
    }

    private static FrameworkElement Meter(string name, SegmentBar bar, TextBlock value)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(row, Hud.Label(name, 9, Hud.Dim, Hud.Mono, spacing: 100), 0, 0);
        Place(row, bar, 0, 1);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        Place(row, value, 0, 2);
        return row;
    }

    private static FrameworkElement Readout(string name, TextBlock value)
    {
        var row = new Grid();
        row.Children.Add(Hud.Label(name, 9, Hud.Dim, Hud.Mono, spacing: 100));
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(value);
        return row;
    }

    // A maker's plate in the corner: a barcode and a serial, the kind of label a machine part carries.
    private static FrameworkElement SerialPlate()
    {
        var plate = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.7 };
        var code = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Height = 18 };
        var random = new Random(2088);
        for (int i = 0; i < 64; i++)
            code.Children.Add(new Rectangle { Width = random.Next(1, 4), Fill = Hud.Solid(random.Next(5) == 0 ? Hud.Red : Hud.Dim) });
        plate.Children.Add(code);
        plate.Children.Add(Hud.Label("NS-2088-ΔK7 · 新東京 第七区 製", 8, Hud.Faint, Hud.Japanese, spacing: 60));
        return plate;
    }

    private FrameworkElement Footer()
    {
        var footer = new Grid();
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(OutlineButton("AllAppsButton", _allLabel, _allJapanese, () =>
        {
            _showAll = !_showAll;
            _ask.Text = "";
            _selected = 0;
            ShowContent();
            _ask.Focus(FocusState.Programmatic);
        }));
        left.Children.Add(OutlineButton("NeoShellSettingsButton", Hud.Label("CONFIG", 11, Hud.Text, spacing: 160, bold: true), Hud.Label("設定", 10, Hud.Red, Hud.Japanese), () => Run(() => _owner.OpenSettings())));
        footer.Children.Add(left);

        var power = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        power.Children.Add(PowerButton("LockButton", "LOCK", "施錠", false, Power.Lock));
        power.Children.Add(PowerButton("SignOutButton", "SIGN OUT", "退出", false, Power.SignOut));
        power.Children.Add(PowerButton("SleepButton", "SLEEP", "休止", false, Power.Sleep));
        power.Children.Add(PowerButton("RestartButton", "REBOOT", "再起動", false, () => Power.Restart(installUpdates: false, shift: false)));
        power.Children.Add(PowerButton("ShutDownButton", "SHUT DOWN", "停止", true, () => Power.ShutDown(installUpdates: false, shift: false)));
        footer.Children.Add(power);
        return footer;
    }

    private static Button OutlineButton(string automationId, TextBlock label, TextBlock japanese, Action click)
    {
        var frame = new HudFrame(8, Hud.Void, Hud.LineBright) { Height = 40, MarkerLength = 0 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(14, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(label);
        row.Children.Add(japanese);
        frame.Children.Add(row);
        Button button = Hud.Plain(automationId, label.Text, frame);
        button.Click += (_, _) => click();
        button.PointerEntered += (_, _) => frame.SetLook(Hud.Raised, Hud.Red);
        button.PointerExited += (_, _) => frame.SetLook(Hud.Void, Hud.LineBright);
        return button;
    }

    private Button PowerButton(string automationId, string text, string japanese, bool primary, Action action)
    {
        Color fill = primary ? Hud.Red : Hud.Void, stroke = primary ? Hud.Red : Hud.LineBright;
        var frame = new HudFrame(7, fill, stroke) { Height = 40, MinWidth = 76, MarkerLength = 0 };
        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Hud.Label(text, 10, Hud.Text, spacing: 140, bold: true));
        stack.Children.Add(Hud.Label(japanese, 9, primary ? Hud.Text : Hud.Red, Hud.Japanese));
        frame.Children.Add(stack);
        Button button = Hud.Plain(automationId, text, frame);
        button.Click += (_, _) => Run(action);
        button.PointerEntered += (_, _) => frame.SetLook(primary ? Hud.RedHot : Hud.Raised, Hud.Red);
        button.PointerExited += (_, _) => frame.SetLook(fill, stroke);
        return button;
    }

    // Closes Nexus first: what's started takes the foreground.
    private void Run(Action action)
    {
        Hide(restoreForeground: false);
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn("A Nexus command failed", ex);
        }
    }

    private void FillModules()
    {
        _modules.Children.Clear();
        List<PinnedApp> pinned = [.. StartPins.Apps(_owner.Settings.Current.StartPins).Take(ModuleColumns * ModuleRows)];
        for (int i = 0; i < pinned.Count; i++)
        {
            Button tile = ModuleTile(pinned[i], i);
            Grid.SetColumn(tile, i % ModuleColumns);
            Grid.SetRow(tile, i / ModuleColumns);
            _modules.Children.Add(tile);
        }
    }

    private Button ModuleTile(PinnedApp app, int index)
    {
        var frame = new HudFrame(12, Hud.Raised, Hud.Line) { MarkerLength = 0 };
        var number = Hud.Label($"{index + 1:00}", 9, Hud.Red, Hud.Mono);
        number.HorizontalAlignment = HorizontalAlignment.Right;
        number.VerticalAlignment = VerticalAlignment.Top;
        number.Margin = new Thickness(0, 8, 10, 0);
        var icon = new Image { Width = 36, Height = 36, IsHitTestVisible = false, Margin = new Thickness(0, 0, 0, 18) };
        _ = SetIcon(icon, app, 48);
        var name = Hud.Label(app.DisplayName.ToUpperInvariant(), 10, Hud.Dim, spacing: 60);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.VerticalAlignment = VerticalAlignment.Bottom;
        name.Margin = new Thickness(8, 0, 8, 12);
        frame.Children.Add(number);
        frame.Children.Add(icon);
        frame.Children.Add(name);

        Button button = Hud.Plain("PinnedApp", app.DisplayName, frame);
        ToolTipService.SetToolTip(button, app.DisplayName);
        button.Click += (_, _) => Run(() => Launcher.Launch(app));
        button.PointerEntered += (_, _) =>
        {
            frame.SetLook(Hud.RedDeep, Hud.Red);
            frame.MarkerLength = 8;
            name.Foreground = Hud.Solid(Hud.Text);
        };
        button.PointerExited += (_, _) =>
        {
            frame.SetLook(Hud.Raised, Hud.Line);
            frame.MarkerLength = 0;
            name.Foreground = Hud.Solid(Hud.Dim);
        };
        return button;
    }

    private static async Task SetIcon(Image image, PinnedApp app, int size) => image.Source = await ThemeIcons.Of(app, size);

    // The most used apps by Windows' launch history, as XP's Start lists them.
    private void FillRecent()
    {
        _recent.Children.Clear();
        IReadOnlyList<AppUsage> usage = [];
        try
        {
            usage = UserAssist.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            Log.Warn("Could not read the launch history", ex);
        }
        foreach (PinnedApp app in AllApps.UsageScores(_catalog, usage).Where(score => score.Value >= 1)
            .OrderByDescending(score => score.Value).Select(score => score.Key).Take(5))
        {
            var row = new Grid { Height = 30, ColumnSpacing = 10, Background = Hud.Solid(Microsoft.UI.Colors.Transparent) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new Image { Width = 20, Height = 20 };
            _ = SetIcon(icon, app, 24);
            Place(row, icon, 0, 0);
            var name = Hud.Label(app.DisplayName.ToUpperInvariant(), 11, Hud.Dim, spacing: 60);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Place(row, name, 0, 1);
            Button button = Hud.Plain("RecentApp", app.DisplayName, row);
            button.Click += (_, _) => Run(() => Launcher.Launch(app));
            button.PointerEntered += (_, _) => name.Foreground = Hud.Solid(Hud.Red);
            button.PointerExited += (_, _) => name.Foreground = Hud.Solid(Hud.Dim);
            _recent.Children.Add(button);
        }
        if (_recent.Children.Count == 0)
            _recent.Children.Add(Hud.Label("NO HISTORY // 履歴なし", 10, Hud.Faint, Hud.Mono));
    }

    private void UpdateTelemetry()
    {
        if (Hud.Resources.Latest is { } sample)
        {
            _cpuBar.SetValue(sample.CpuPercent / 100);
            _cpu.Text = $"{sample.CpuPercent,3:0}%";
            double memory = sample.MemoryTotal == 0 ? 0 : (double)sample.MemoryUsed / sample.MemoryTotal;
            _memoryBar.SetValue(memory);
            _memory.Text = $"{memory * 100,3:0}%";
        }
        _uptime.Text = Clock2088.Uptime(TimeSpan.FromMilliseconds(Environment.TickCount64));
        NetworkState network = _owner.Indicators?.Network ?? NetworkState.Disconnected;
        _link.Text = network.Kind == NetworkKind.Disconnected ? "OFFLINE" : network.HasInternet ? $"{network.Kind} · ONLINE".ToUpperInvariant() : "LOCAL ONLY";
        _link.Foreground = Hud.Solid(network.HasInternet ? Hud.Text : Hud.Red);
    }

    /// <summary>What the panel's left side shows: the modules, everything, or what the ask line matches.</summary>
    private void ShowContent()
    {
        string query = _ask.Text.Trim();
        bool listing = query.Length > 0 || _showAll;
        _modules.Visibility = listing ? Visibility.Collapsed : Visibility.Visible;
        _resultsScroll.Visibility = listing ? Visibility.Visible : Visibility.Collapsed;
        _allLabel.Text = _showAll ? "MODULES" : "ALL MODULES";
        _allJapanese.Text = _showAll ? "‹ モジュール" : "全て ›";
        if (!listing)
        {
            _contentTitle.Text = "MODULES // モジュール";
            _contentCount.Text = $"{_modules.Children.Count:00} PINNED";
            return;
        }

        // A command comes before the apps only when it's asked for by its word: Enter on "re" must not restart the PC.
        IReadOnlyList<(NexusCommand Command, string Text, bool Exact)> commands = NexusIntents.Match(query);
        Result Command((NexusCommand Command, string Text, bool Exact) c) => new(c.Text, "COMMAND // 命令", null, Action(c.Command));
        _shown = query.Length > 0
            ? [.. commands.Where(c => c.Exact).Select(Command),
                .. AppSearch.Rank(_catalog, app => app.DisplayName, query).Take(ResultLimit).Select(app => new Result(app.DisplayName, "APP // アプリ", app, null)),
                .. commands.Where(c => !c.Exact).Select(Command)]
            : [.. _catalog.OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase).Select(app => new Result(app.DisplayName, "APP // アプリ", app, null))];
        _selected = Math.Clamp(_selected, 0, Math.Max(0, _shown.Count - 1));
        _contentTitle.Text = query.Length > 0 ? "RESOLVED // 解決" : "ALL MODULES // 全て";
        _contentCount.Text = $"{_shown.Count:00} MATCH{(_shown.Count == 1 ? "" : "ES")}";
        FillResults();
    }

    private Action Action(NexusCommand command) => command switch
    {
        NexusCommand.Lock => Power.Lock,
        NexusCommand.Sleep => Power.Sleep,
        NexusCommand.Restart => () => Power.Restart(installUpdates: false, shift: false),
        NexusCommand.ShutDown => () => Power.ShutDown(installUpdates: false, shift: false),
        NexusCommand.SignOut => Power.SignOut,
        NexusCommand.Settings => () => _owner.OpenSettings(),
        NexusCommand.Run => _owner.ShowRunDialog,
        _ => _owner.ToggleDesktop,
    };

    private void FillResults()
    {
        _results.Children.Clear();
        for (int i = 0; i < _shown.Count; i++)
            _results.Children.Add(ResultRow(_shown[i], i));
        if (_shown.Count == 0)
            _results.Children.Add(Hud.Label("NO MATCH // 該当なし", 12, Hud.Faint, Hud.Mono, spacing: 100));
    }

    private Button ResultRow(Result result, int index)
    {
        bool selected = index == _selected;
        var frame = new HudFrame(6, selected ? Hud.Raised : Hud.Panel, selected ? Hud.Red : Hud.Panel) { Height = 46, MarkerLength = selected ? 6 : 0 };
        var row = new Grid { Margin = new Thickness(14, 0, 14, 0), ColumnSpacing = 12 };
        foreach (GridLength width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        Place(row, Hud.Label($"{index + 1:00}", 9, selected ? Hud.Red : Hud.Faint, Hud.Mono), 0, 0);
        var icon = new Image { Width = 24, Height = 24 };
        if (result.App is { } app)
            _ = SetIcon(icon, app, 32);
        else
            Place(row, Hud.Label("⌁", 18, Hud.Red, Hud.Mono), 0, 1);
        Place(row, icon, 0, 1);
        var name = Hud.Label(result.Text.ToUpperInvariant(), 14, selected ? Hud.Text : Hud.Dim, spacing: 80);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        Place(row, name, 0, 2);
        Place(row, Hud.Label(result.Kind, 9, result.Action is null ? Hud.Faint : Hud.Red, Hud.Japanese, spacing: 60), 0, 3);
        frame.Children.Add(row);
        if (selected)
            frame.Children.Add(new Rectangle { Width = 3, Fill = Hud.Solid(Hud.Red), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 8) });

        Button button = Hud.Plain("SearchResult", result.Text, frame);
        button.Click += (_, _) => Execute(result);
        return button;
    }

    private void Execute(Result result)
    {
        if (result.App is { } app)
            Run(() => Launcher.Launch(app));
        else if (result.Action is { } action)
            Run(action);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool listing = _resultsScroll.Visibility == Visibility.Visible;
        switch (e.Key)
        {
            case VirtualKey.Escape:
                Hide();
                break;
            case VirtualKey.Down or VirtualKey.Up when listing && _shown.Count > 0:
                _selected = Math.Clamp(_selected + (e.Key == VirtualKey.Down ? 1 : -1), 0, _shown.Count - 1);
                FillResults();
                if (_results.Children[_selected] is FrameworkElement row)
                    row.StartBringIntoView();
                break;
            case VirtualKey.Enter when listing && _shown.Count > 0:
                Execute(_shown[_selected]);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>One line in the list: an app to start, or a command to run.</summary>
    private sealed record Result(string Text, string Kind, PinnedApp? App, Action? Action);
}
