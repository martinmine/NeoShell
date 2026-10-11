using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Frames;
using NeoShell.Interop.Windowing;
using NeoShell.Taskbar;
using Windows.Graphics;
using Windows.UI;

namespace NeoShell.Settings;

/// <summary>
/// NeoShell's own settings, in the layout of Windows 11's Settings app: pages in a navigation pane on the left, each
/// of cards that change and save their setting as they're used. Opened from Start's NeoShell settings button, the
/// taskbar's "Taskbar settings" and the widget sidebar's "Widget settings"; one at a time.
/// </summary>
internal sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? s_open;

    private readonly Taskbars _taskbars;

    /// <summary>Opens the window on <paramref name="page"/>, or brings it to the front (on that page, if given).</summary>
    public static void Open(Taskbars taskbars, SettingsPage? page)
    {
        if (s_open is null)
        {
            s_open = new SettingsWindow(taskbars);
            s_open.Closed += (_, _) => s_open = null;
            s_open.ShowPage(page ?? SettingsPage.Taskbar);
            s_open.Activate();
        }
        else if (page is { } shown)
        {
            s_open.ShowPage(shown);
        }
        TopLevelWindows.Activate(Win32Interop.GetWindowFromWindowId(s_open.AppWindow.Id));
    }

    public static void CloseOpen() => s_open?.Close();

    private SettingsWindow(Taskbars taskbars)
    {
        _taskbars = taskbars;
        InitializeComponent();
        Title = "NeoShell settings";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        // WinUI colours the caption buttons for the theme the window opened in only; Windows' mode can change while it's open.
        Root.ActualThemeChanged += (_, _) => ColorCaptionButtons();
        ColorCaptionButtons();

        foreach (SettingsPage page in Enum.GetValues<SettingsPage>())
        {
            var item = new NavigationViewItem
            {
                Content = SettingsPages.Title(page),
                Icon = new FontIcon { Glyph = SettingsPages.Glyph(page) },
                Tag = page,
            };
            AutomationProperties.SetAutomationId(item, $"{page}NavigationItem");
            // About sits apart at the bottom, as Settings' Windows Update does.
            (page == SettingsPage.About ? Navigation.FooterMenuItems : Navigation.MenuItems).Add(item);
        }

        // As big as Settings opens, centred on the primary monitor's work area.
        DisplayMonitor monitor = DisplayMonitor.GetAll().FirstOrDefault(m => m.IsPrimary) ?? DisplayMonitor.GetAll()[0];
        double scale = monitor.Dpi / 96.0;
        RectInt32 area = monitor.WorkArea;
        var size = new SizeInt32(Math.Min((int)(1100 * scale), area.Width), Math.Min((int)(820 * scale), area.Height - (int)(40 * scale)));
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));
    }

    private void ShowPage(SettingsPage page) =>
        Navigation.SelectedItem = Navigation.MenuItems.Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .First(item => (SettingsPage)item.Tag == page);

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: SettingsPage page })
            return;

        PageTitle.Text = SettingsPages.Title(page);
        PageHost.Content = page switch
        {
            SettingsPage.Taskbar => new TaskbarSettingsPage(_taskbars),
            SettingsPage.Start => new StartSettingsPage(_taskbars),
            SettingsPage.Desktop => new DesktopSettingsPage(_taskbars),
            SettingsPage.Widgets => new WidgetSettingsPage(_taskbars),
            SettingsPage.WindowFrames => new WindowFramesPage(_taskbars),
            _ => new AboutSettingsPage(_taskbars),
        };
        PageScroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    // Settings' caption buttons: black or white glyphs, greyed while inactive, on a faint plate when hovered or pressed.
    private void ColorCaptionButtons()
    {
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        byte ink = dark ? (byte)255 : (byte)0;
        AppWindowTitleBar bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = bar.ButtonHoverForegroundColor = bar.ButtonPressedForegroundColor = Color.FromArgb(255, ink, ink, ink);
        bar.ButtonInactiveForegroundColor = Color.FromArgb(0x5D, ink, ink, ink);
        bar.ButtonHoverBackgroundColor = Color.FromArgb(0x0F, ink, ink, ink);
        bar.ButtonPressedBackgroundColor = Color.FromArgb(0x0A, ink, ink, ink);
    }

    // Settings shows the pane's toggle only once the window is too narrow for the pane to stay open.
    private void Navigation_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) =>
        sender.IsPaneToggleButtonVisible = args.DisplayMode != NavigationViewDisplayMode.Expanded;
}
