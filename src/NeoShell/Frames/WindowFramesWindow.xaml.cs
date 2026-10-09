using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI;

namespace NeoShell.Frames;

/// <summary>
/// The window frames settings, opened from the taskbar's menu until NeoShell has a settings window: the feature on or
/// off, the style for all windows, app rules, and the style editor with its presets and a live preview.
/// </summary>
internal sealed partial class WindowFramesWindow : Window
{
    private const string LeaveAlone = "Leave alone";
    private const uint DefaultCustomColor = 0xFF0054E3;

    private static WindowFramesWindow? s_open;

    private readonly SettingsStore _settings;
    private readonly ColorButton _captionColor;
    private readonly ColorButton _textColor;
    private readonly ColorButton _borderColor;
    private readonly DispatcherQueueTimer _colorCommit;
    private FramePreviewWindow? _preview;
    // The style shown in the editor, by name.
    private string _editing;
    private bool _loading;

    /// <summary>Opens the window in front, or brings it back to the front when it's open already.</summary>
    public static void Open(SettingsStore settings)
    {
        if (s_open is null)
        {
            s_open = new WindowFramesWindow(settings);
            s_open.Closed += (_, _) => s_open = null;
        }
        s_open.Activate();
    }

    public static void CloseOpen() => s_open?.Close();

    private WindowFramesWindow(SettingsStore settings)
    {
        _settings = settings;
        _editing = settings.Current.WindowFrameStyle;
        InitializeComponent();
        Title = "Window frames";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        _captionColor = new ColorButton(CaptionColorButton, "CaptionColorPicker", OnColorPicked);
        _textColor = new ColorButton(TextColorButton, "TextColorPicker", OnColorPicked);
        _borderColor = new ColorButton(BorderColorButton, "BorderColorPicker", OnColorPicked);
        // A colour picker reports every step of a drag: the preview follows each, the windows the last.
        _colorCommit = DispatcherQueue.CreateTimer();
        _colorCommit.Interval = TimeSpan.FromMilliseconds(300);
        _colorCommit.IsRepeating = false;
        _colorCommit.Tick += (_, _) => CommitEditor();

        DisplayMonitor monitor = DisplayMonitor.GetAll().FirstOrDefault(m => m.IsPrimary) ?? DisplayMonitor.GetAll()[0];
        double scale = monitor.Dpi / 96.0;
        RectInt32 area = monitor.WorkArea;
        var size = new SizeInt32((int)(860 * scale), Math.Min((int)(920 * scale), area.Height - (int)(40 * scale)));
        AppWindow.MoveAndResize(new RectInt32(area.X + (int)(40 * scale), area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));

        Closed += (_, _) =>
        {
            _colorCommit.Stop();
            _preview?.Close();
        };
        Load();
    }

    private ShellSettings Settings => _settings.Current;

    private IEnumerable<FrameStyle> AllStyles => FramePresets.All.Concat(Settings.WindowFrameStyles);

    private FrameStyle EditedStyle => FramePresets.Find(_editing, Settings.WindowFrameStyles) ?? FramePresets.All[0];

    /// <summary>Fills every control from the settings.</summary>
    private void Load()
    {
        _loading = true;
        EnabledToggle.IsOn = Settings.WindowFramesEnabled;

        string[] names = [.. AllStyles.Select(style => style.Name)];
        Fill(GlobalStyleBox, names, Settings.WindowFrameStyle);
        Fill(EditStyleBox, names, EditedStyle.Name);
        LoadRules(names);
        LoadEditor();
        _loading = false;
    }

    private static void Fill(ComboBox box, IReadOnlyList<string> names, string? selected)
    {
        box.Items.Clear();
        foreach (string name in names)
            box.Items.Add(name);
        box.SelectedItem = selected;
    }

    private void LoadRules(string[] styleNames)
    {
        RuleList.Children.Clear();
        IReadOnlyList<FrameRule> rules = Settings.WindowFrameRules;
        NoRulesText.Visibility = rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < rules.Count; i++)
            RuleList.Children.Add(RuleRow(i, rules[i], styleNames));
    }

    private Grid RuleRow(int index, FrameRule rule, string[] styleNames)
    {
        var process = new TextBlock { Text = rule.ProcessName, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        var className = new TextBox
        {
            Text = rule.ClassName ?? "",
            PlaceholderText = "Any window class",
            Width = 200,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(className, "RuleClassBox");
        AutomationProperties.SetName(className, $"Window class for {rule.ProcessName}");
        className.LostFocus += (_, _) =>
        {
            string? value = className.Text.Trim() is { Length: > 0 } text ? text : null;
            if (value != rule.ClassName)
                UpdateRule(index, rule with { ClassName = value });
        };

        var style = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        Fill(style, [LeaveAlone, .. styleNames], rule.Style ?? LeaveAlone);
        AutomationProperties.SetAutomationId(style, "RuleStyleBox");
        AutomationProperties.SetName(style, $"Style for {rule.ProcessName}");
        style.SelectionChanged += (_, _) =>
        {
            if (!_loading && style.SelectedItem is string name)
                UpdateRule(index, rule with { Style = name == LeaveAlone ? null : name });
        };

        var remove = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(remove, "RemoveRuleButton");
        AutomationProperties.SetName(remove, $"Remove the rule for {rule.ProcessName}");
        ToolTipService.SetToolTip(remove, "Remove");
        remove.Click += (_, _) => SaveRules([.. Settings.WindowFrameRules.Where((_, i) => i != index)]);

        var row = new Grid
        {
            Style = (Style)Root.Resources["CardStyle"],
            MinHeight = 0,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(className, 1);
        Grid.SetColumn(style, 2);
        Grid.SetColumn(remove, 3);
        row.Children.Add(process);
        row.Children.Add(className);
        row.Children.Add(style);
        row.Children.Add(remove);
        AutomationProperties.SetAutomationId(row, "FrameRule");
        AutomationProperties.SetName(row, rule.ProcessName);
        return row;
    }

    private void UpdateRule(int index, FrameRule rule) =>
        SaveRules([.. Settings.WindowFrameRules.Select((existing, i) => i == index ? rule : existing)]);

    private void SaveRules(IReadOnlyList<FrameRule> rules)
    {
        _settings.Update(Settings with { WindowFrameRules = rules });
        // Later, so a combo box isn't rebuilt from inside its own event.
        DispatcherQueue.TryEnqueue(Load);
    }

    private void LoadEditor()
    {
        FrameStyle style = EditedStyle;
        bool preset = FramePresets.IsPreset(style.Name);
        PresetNote.Visibility = preset ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.IsEnabled = !preset;
        foreach (Control control in new Control[] { NameBox, BackdropBox, ThemeBox, CaptionColorBox, TextColorBox, BorderColorBox, CornersBox, BasicFrameToggle })
            control.IsEnabled = !preset;
        // Labels of their own, which don't grey out with their controls as headers do.
        var label = (Brush)Application.Current.Resources[preset ? "TextFillColorDisabledBrush" : "TextFillColorPrimaryBrush"];
        CaptionColorLabel.Foreground = TextColorLabel.Foreground = BorderColorLabel.Foreground = label;

        NameBox.Text = style.Name;
        BackdropBox.SelectedIndex = (int)style.Backdrop;
        ThemeBox.SelectedIndex = (int)style.Theme;
        CornersBox.SelectedIndex = (int)style.Corners;
        BasicFrameToggle.IsOn = style.BasicFrame;
        CaptionColorBox.SelectedIndex = style.CaptionColor switch { null => 0, FrameColors.Accent => 1, _ => 2 };
        TextColorBox.SelectedIndex = style.TextColor switch { null => 0, FrameColors.Contrast => 1, FrameColors.Accent => 2, _ => 3 };
        BorderColorBox.SelectedIndex = style.BorderColor switch { null => 0, FrameColors.Accent => 1, FrameColors.None => 2, _ => 3 };
        _captionColor.Color = FrameColors.Parse(style.CaptionColor) ?? _captionColor.Color ?? DefaultCustomColor;
        _textColor.Color = FrameColors.Parse(style.TextColor) ?? _textColor.Color ?? 0xFFFFFFFF;
        _borderColor.Color = FrameColors.Parse(style.BorderColor) ?? _borderColor.Color ?? DefaultCustomColor;
        UpdateColorButtons(preset);
        _preview?.ShowStyle(style);
    }

    private void UpdateColorButtons(bool preset)
    {
        // Only a custom colour has one to pick.
        CaptionColorButton.Visibility = Shown(CaptionColorBox.SelectedIndex == 2);
        TextColorButton.Visibility = Shown(TextColorBox.SelectedIndex == 3);
        BorderColorButton.Visibility = Shown(BorderColorBox.SelectedIndex == 3);
        CaptionColorButton.IsEnabled = TextColorButton.IsEnabled = BorderColorButton.IsEnabled = !preset;

        static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The edited style as the editor's controls now say.</summary>
    private FrameStyle EditorStyle() => EditedStyle with
    {
        Backdrop = (FrameBackdrop)Math.Max(0, BackdropBox.SelectedIndex),
        Theme = (FrameTheme)Math.Max(0, ThemeBox.SelectedIndex),
        Corners = (FrameCorners)Math.Max(0, CornersBox.SelectedIndex),
        BasicFrame = BasicFrameToggle.IsOn,
        CaptionColor = CaptionColorBox.SelectedIndex switch { 1 => FrameColors.Accent, 2 => _captionColor.Text, _ => null },
        TextColor = TextColorBox.SelectedIndex switch { 1 => FrameColors.Contrast, 2 => FrameColors.Accent, 3 => _textColor.Text, _ => null },
        BorderColor = BorderColorBox.SelectedIndex switch { 1 => FrameColors.Accent, 2 => FrameColors.None, 3 => _borderColor.Text, _ => null },
    };

    private void Editor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || FramePresets.IsPreset(_editing))
            return;
        UpdateColorButtons(preset: false);
        CommitEditor();
    }

    private void OnColorPicked()
    {
        if (_loading || FramePresets.IsPreset(_editing))
            return;
        _preview?.ShowStyle(EditorStyle());
        _colorCommit.Stop();
        _colorCommit.Start();
    }

    private void CommitEditor()
    {
        FrameStyle style = EditorStyle();
        _preview?.ShowStyle(style);
        _settings.Update(Settings with
        {
            WindowFrameStyles = [.. Settings.WindowFrameStyles.Select(existing => existing.Name == _editing ? style : existing)],
        });
    }

    private void NameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
            Rename();
    }

    private void NameBox_LostFocus(object sender, RoutedEventArgs e) => Rename();

    /// <summary>Renames the edited style, and every reference to it; a name that's empty or taken is refused.</summary>
    private void Rename()
    {
        string name = NameBox.Text.Trim();
        if (FramePresets.IsPreset(_editing) || name == _editing)
            return;
        if (name.Length == 0 || FramePresets.Find(name, Settings.WindowFrameStyles) is not null)
        {
            NameBox.Text = _editing;
            return;
        }

        string old = _editing;
        _editing = name;
        _settings.Update(Settings with
        {
            WindowFrameStyle = Settings.WindowFrameStyle == old ? name : Settings.WindowFrameStyle,
            WindowFrameStyles = [.. Settings.WindowFrameStyles.Select(style => style.Name == old ? style with { Name = name } : style)],
            WindowFrameRules = [.. Settings.WindowFrameRules.Select(rule => rule.Style == old ? rule with { Style = name } : rule)],
        });
        Load();
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            _settings.Update(Settings with { WindowFramesEnabled = EnabledToggle.IsOn });
    }

    private void GlobalStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && GlobalStyleBox.SelectedItem is string name)
            _settings.Update(Settings with { WindowFrameStyle = name });
    }

    private void EditStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || EditStyleBox.SelectedItem is not string name)
            return;
        _editing = name;
        _loading = true;
        LoadEditor();
        _loading = false;
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        FrameStyle copy = EditedStyle with { Name = FramePresets.CopyName(EditedStyle.Name, Settings.WindowFrameStyles) };
        _settings.Update(Settings with { WindowFrameStyles = [.. Settings.WindowFrameStyles, copy] });
        _editing = copy.Name;
        Load();
    }

    /// <summary>Deletes the edited style; whatever used it goes back to Windows' own frames.</summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        string old = _editing;
        _settings.Update(Settings with
        {
            WindowFrameStyle = Settings.WindowFrameStyle == old ? FramePresets.WindowsDefault : Settings.WindowFrameStyle,
            WindowFrameStyles = [.. Settings.WindowFrameStyles.Where(style => style.Name != old)],
            WindowFrameRules = [.. Settings.WindowFrameRules.Select(rule => rule.Style == old ? rule with { Style = FramePresets.WindowsDefault } : rule)],
        });
        _editing = Settings.WindowFrameStyle;
        Load();
    }

    private void PreviewToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (PreviewToggle.IsChecked == true)
        {
            if (_preview is not null)
                return;
            _preview = new FramePreviewWindow(PreviewBounds());
            _preview.Closed += (_, _) =>
            {
                _preview = null;
                PreviewToggle.IsChecked = false;
            };
            _preview.ShowStyle(EditorStyle());
            _preview.Activate();
        }
        else
        {
            _preview?.Close();
        }
    }

    /// <summary>Beside this window, right or left, where the monitor has room; else over its top right corner.</summary>
    private RectInt32 PreviewBounds()
    {
        PointInt32 position = AppWindow.Position;
        SizeInt32 size = AppWindow.Size;
        var editor = new RectInt32(position.X, position.Y, size.Width, size.Height);
        DisplayMonitor monitor = DisplayMonitor.GetAll().FirstOrDefault(m => m.Handle == DisplayMonitor.HandleFromRect(editor, nearest: true))
            ?? DisplayMonitor.GetAll()[0];
        double scale = monitor.Dpi / 96.0;
        int width = (int)(440 * scale), height = (int)(240 * scale), gap = (int)(16 * scale);
        RectInt32 area = monitor.WorkArea;
        int x = editor.X + editor.Width + gap + width <= area.X + area.Width ? editor.X + editor.Width + gap
            : editor.X - gap - width >= area.X ? editor.X - gap - width
            : Math.Max(area.X, editor.X + editor.Width - width - gap);
        return new RectInt32(x, editor.Y + gap * 4, width, height);
    }

    /// <summary>The running apps' windows a rule can be added for, by process; picking one adds a rule for it.</summary>
    private void AddRuleMenu_Opening(object sender, object e)
    {
        AddRuleMenu.Items.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (nint hwnd in TopLevelWindows.GetAll())
        {
            WindowFrame.Target window = WindowFrame.Read(hwnd);
            if (window.ProcessName is not { } process || !FrameRules.IsEligible(window, Environment.ProcessId)
                || window.Title.Length == 0 || !seen.Add(process))
            {
                continue;
            }
            var item = new MenuFlyoutItem { Text = $"{process}  ({window.Title})" };
            AutomationProperties.SetAutomationId(item, "AddRuleMenuItem");
            AutomationProperties.SetName(item, process);
            item.Click += (_, _) =>
            {
                if (!Settings.WindowFrameRules.Any(rule => string.Equals(rule.ProcessName, process, StringComparison.OrdinalIgnoreCase) && rule.ClassName is null))
                    SaveRules([.. Settings.WindowFrameRules, new FrameRule(process, Style: Settings.WindowFrameStyle)]);
            };
            AddRuleMenu.Items.Add(item);
        }
        if (AddRuleMenu.Items.Count == 0)
            AddRuleMenu.Items.Add(new MenuFlyoutItem { Text = "No other app has a window open", IsEnabled = false });
    }

    /// <summary>A drop-down button showing a colour, with a colour picker in its flyout.</summary>
    private sealed class ColorButton
    {
        private readonly Border _swatch;
        private readonly TextBlock _hex = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 64 };
        private readonly ColorPicker _picker;
        private readonly Action _picked;
        private uint? _color;
        private bool _setting;

        public ColorButton(DropDownButton button, string pickerId, Action picked)
        {
            _picked = picked;
            _swatch = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            };
            button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _swatch, _hex } };
            _picker = new ColorPicker
            {
                IsAlphaEnabled = false,
                IsColorChannelTextInputVisible = false,
                IsMoreButtonVisible = false,
                ColorSpectrumShape = ColorSpectrumShape.Ring,
            };
            AutomationProperties.SetAutomationId(_picker, pickerId);
            _picker.ColorChanged += (_, args) =>
            {
                if (_setting)
                    return;
                Show(0xFF000000 | ((uint)args.NewColor.R << 16) | ((uint)args.NewColor.G << 8) | args.NewColor.B);
                _picked();
            };
            button.Flyout = new Flyout { Content = _picker, Placement = FlyoutPlacementMode.Bottom };
        }

        public uint? Color
        {
            get => _color;
            set
            {
                if (value is not { } color)
                    return;
                Show(color);
                _setting = true;
                _picker.Color = ToColor(color);
                _setting = false;
            }
        }

        public string? Text => _color is { } color ? FrameColors.Format(color) : null;

        private void Show(uint color)
        {
            _color = color;
            _swatch.Background = new SolidColorBrush(ToColor(color));
            _hex.Text = FrameColors.Format(color);
        }

        private static Color ToColor(uint argb) => ColorHelper.FromArgb(255, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    }
}
