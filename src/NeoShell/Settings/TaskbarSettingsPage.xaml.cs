using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Taskbar;

namespace NeoShell.Settings;

/// <summary>
/// The taskbar's settings, which its context menu held until this window: NeoShell's own (alignment, auto-hide, all
/// displays, combining, the backdrop, widgets) and Windows' that NeoShell keeps where Explorer does (search, the hidden
/// icon menu).
/// </summary>
internal sealed partial class TaskbarSettingsPage : SettingsPageView
{
    // The search box's choices, in Settings' order.
    private static readonly TaskbarSearchMode[] SearchModes =
        [TaskbarSearchMode.Hidden, TaskbarSearchMode.Icon, TaskbarSearchMode.IconAndLabel, TaskbarSearchMode.Box];

    public TaskbarSettingsPage(Taskbars taskbars)
        : base(taskbars)
    {
        InitializeComponent();
    }

    protected override void Load()
    {
        SearchModeBox.SelectedIndex = Array.IndexOf(SearchModes, Taskbars.SearchMode);
        ShowWidgetsToggle.IsOn = Current.ShowWidgetSidebar;
        TrayExpander.Visibility = Taskbars.Tray is null ? Visibility.Collapsed : Visibility.Visible;
        HiddenIconMenuToggle.IsOn = Taskbars.Tray?.ChevronVisible == true;
        AlignmentBox.SelectedIndex = (int)Current.TaskbarAlignment;
        AutoHideCheckBox.IsChecked = Current.AutoHide;
        AllDisplaysCheckBox.IsChecked = Current.ShowOnAllDisplays;
        CombineBox.SelectedIndex = (int)Current.CombineButtons;
        BackdropBox.SelectedIndex = (int)Current.TaskbarBackdrop;
    }

    private void SearchModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsFilling && SearchModeBox.SelectedIndex >= 0)
            Taskbars.SetSearchMode(SearchModes[SearchModeBox.SelectedIndex]);
    }

    private void ShowWidgetsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsFilling)
            Save(Current with { ShowWidgetSidebar = ShowWidgetsToggle.IsOn });
    }

    private void HiddenIconMenuToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsFilling && Taskbars.Tray is { } tray)
            tray.ChevronVisible = HiddenIconMenuToggle.IsOn;
    }

    private void AlignmentBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsFilling && AlignmentBox.SelectedIndex >= 0)
            Save(Current with { TaskbarAlignment = (TaskbarAlignment)AlignmentBox.SelectedIndex });
    }

    private void AutoHideCheckBox_Click(object sender, RoutedEventArgs e) =>
        Save(Current with { AutoHide = AutoHideCheckBox.IsChecked == true });

    private void AllDisplaysCheckBox_Click(object sender, RoutedEventArgs e) =>
        Save(Current with { ShowOnAllDisplays = AllDisplaysCheckBox.IsChecked == true });

    private void CombineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsFilling && CombineBox.SelectedIndex >= 0)
            Save(Current with { CombineButtons = (CombineButtons)CombineBox.SelectedIndex });
    }

    private void BackdropBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsFilling && BackdropBox.SelectedIndex >= 0)
            Save(Current with { TaskbarBackdrop = (Backdrop)BackdropBox.SelectedIndex });
    }

    private void WindowsTaskbarSettings_Click(object sender, RoutedEventArgs e) =>
        OpenWindowsSettings("Taskbar settings", "ms-settings:taskbar");
}
