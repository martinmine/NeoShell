using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Notifications;
using NeoShell.Settings;

namespace NeoShell.Notifications;

public sealed partial class NotificationPanel : UserControl
{
    private readonly NotificationCenter _center;
    private readonly RunMode _runMode;
    // Groups the user expanded since the panel opened.
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);

    internal NotificationPanel(NotificationCenter center, RunMode runMode)
    {
        InitializeComponent();
        _center = center;
        _runMode = runMode;
    }

    /// <summary>The content changed height; the window should be measured again.</summary>
    public event Action? ContentResized;

    /// <summary>An app was opened from a notification: the flyout closes.</summary>
    public event Action? CloseRequested;

    public void Opening()
    {
        _expanded.Clear();
        Refresh();
        Scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    public void Refresh()
    {
        DoNotDisturbButton.IsChecked = _center.DoNotDisturb;
        IReadOnlyList<NotificationGroup> groups = NotificationDisplay.Group(_center.Toasts);
        _expanded.IntersectWith(groups.Select(g => g.AppId));

        Groups.Children.Clear();
        foreach (NotificationGroup group in groups)
        {
            Groups.Children.Add(CreateGroupHeader(group));
            bool expanded = _expanded.Contains(group.AppId);
            IEnumerable<ToastInfo> shown = expanded ? group.Toasts : group.Toasts.Take(1);
            foreach (ToastInfo toast in shown)
            {
                var card = new NotificationCard(toast, isToast: false);
                card.Invoked += Card_Invoked;
                card.CloseRequested += c => _center.Remove([c.Toast]);
                card.TurnOffRequested += c => _center.TurnOff(c.Toast.AppId);
                card.SettingsRequested += OpenSettings;
                card.Resized += () => ContentResized?.Invoke();
                Groups.Children.Add(card);
            }

            // Under the last card shown: "+N notifications" while collapsed, "See fewer" once expanded.
            if (group.Toasts.Count > 1 && Groups.Children[^1] is NotificationCard last)
            {
                last.FooterText = expanded ? "See fewer" : NotificationDisplay.MoreText(group.Toasts.Count - 1);
                last.FooterClicked += _ =>
                {
                    if (!_expanded.Remove(group.AppId))
                        _expanded.Add(group.AppId);
                    Refresh();
                };
            }
        }

        bool empty = groups.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Scroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ClearAllButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ContentResized?.Invoke();
    }

    // The app's icon and name; "…" and clear-all-of-the-app's buttons under the pointer.
    private Grid CreateGroupHeader(NotificationGroup group)
    {
        var header = new Grid
        {
            Height = 40,
            Padding = new Thickness(10, 0, 4, 0),
            ColumnSpacing = 8,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        AutomationProperties.SetName(header, group.AppName);
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logo = new Image { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        SetLogo(logo, group.AppId);
        var name = new TextBlock
        {
            Text = group.AppName,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
        Grid.SetColumn(name, 1);

        var more = HeaderButton("");
        AutomationProperties.SetName(more, "More options");
        ToolTipService.SetToolTip(more, "More options");
        var menu = new MenuFlyout();
        var turnOff = new MenuFlyoutItem { Text = $"Turn off all notifications for {group.AppName}", Icon = new FontIcon { Glyph = "" } };
        turnOff.Click += (_, _) => _center.TurnOff(group.AppId);
        var settings = new MenuFlyoutItem { Text = "Go to notification settings", Icon = new FontIcon { Glyph = "" } };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(turnOff);
        menu.Items.Add(settings);
        more.Flyout = menu;
        Grid.SetColumn(more, 2);

        var clear = HeaderButton("");
        AutomationProperties.SetAutomationId(clear, "ClearGroupButton");
        AutomationProperties.SetName(clear, $"Clear all notifications from {group.AppName}");
        ToolTipService.SetToolTip(clear, "Clear");
        clear.Click += (_, _) => _center.Remove(group.Toasts);
        Grid.SetColumn(clear, 3);

        header.Children.Add(logo);
        header.Children.Add(name);
        header.Children.Add(more);
        header.Children.Add(clear);
        header.PointerEntered += (_, _) => more.Opacity = clear.Opacity = 1;
        header.PointerExited += (_, _) => more.Opacity = clear.Opacity = 0;
        return header;
    }

    // Flat until hovered, and hidden until the pointer is on the header.
    private static Button HeaderButton(string glyph) => new()
    {
        Width = 32,
        Height = 32,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(4),
        BorderThickness = new Thickness(0),
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        Opacity = 0,
        Content = new FontIcon { Glyph = glyph, FontSize = 12 },
    };

    private async void SetLogo(Image image, string appId) => image.Source = await _center.GetLogoAsync(appId);

    private void Card_Invoked(NotificationCard card)
    {
        _center.Activate(card.Toast);
        CloseRequested?.Invoke();
    }

    /// <summary>Opens a notification's app as from Start: when its own activation fails.</summary>
    internal static void Open(ToastInfo toast) => Launcher.Launch(new PinnedApp(toast.AppName, AppUserModelId: toast.AppId));

    private void OpenSettings()
    {
        Launcher.OpenSettings(_runMode, "Notifications", "ms-settings:notifications");
        CloseRequested?.Invoke();
    }

    private void DoNotDisturbButton_Click(object sender, RoutedEventArgs e) => _center.SetDoNotDisturb(DoNotDisturbButton.IsChecked == true);

    private void ClearAllButton_Click(object sender, RoutedEventArgs e) => _center.Remove(_center.Toasts);
}
