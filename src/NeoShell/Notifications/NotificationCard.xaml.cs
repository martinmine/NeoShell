using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Notifications;

namespace NeoShell.Notifications;

public sealed partial class NotificationCard : UserControl
{
    private readonly bool _isToast;

    /// <param name="isToast">A toast: the app's icon and name on top, its buttons always shown.</param>
    internal NotificationCard(ToastInfo toast, bool isToast)
    {
        InitializeComponent();
        Toast = toast;
        _isToast = isToast;
        TitleText.Text = toast.Title;
        BodyText.Text = toast.Body;
        BodyText.Visibility = toast.Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        TurnOffItem.Text = $"Turn off all notifications for {toast.AppName}";
        AutomationProperties.SetName(Root, string.Join(", ", new[] { toast.AppName, toast.Title, toast.Body }.Where(s => s.Length > 0)));

        if (isToast)
        {
            TimeHeader.Visibility = Visibility.Collapsed;
            BodyText.MaxLines = 3;
            AppHeader.Visibility = Visibility.Visible;
            AppNameText.Text = toast.AppName;
            MoreButton.Opacity = CloseButton.Opacity = 1;
            // The toast's own acrylic is its background.
            Root.Background = null;
            Root.BorderThickness = new Thickness(0);
            Root.Padding = new Thickness(16, 4, 8, 20);
            TitleText.Margin = new Thickness(0, 10, 8, 0);
        }
        else
        {
            TimeText.Text = NotificationDisplay.TimeText(toast.Time, DateTime.Now, CultureInfo.CurrentCulture);
        }
    }

    internal ToastInfo Toast { get; }

    public ImageSource? Logo
    {
        set => AppLogo.Source = value;
    }

    /// <summary>Windows' default app glyph in place of a logo, as on its own system toasts (AutoPlay's).</summary>
    public void ShowDefaultLogo()
    {
        AppLogo.Visibility = Visibility.Collapsed;
        DefaultLogo.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// A tray balloon's picture beside the text, as on Explorer's balloon toasts: a 48 effective pixel place right under
    /// the header, the text centred on it, 16 below it. Toasts only.
    /// </summary>
    public ImageSource? Picture
    {
        set
        {
            PictureImage.Source = value;
            PictureImage.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
            if (value is null)
                return;
            TextPanel.VerticalAlignment = VerticalAlignment.Center;
            TitleText.Margin = new Thickness(0, 0, 8, 0);
            Root.Padding = new Thickness(16, 4, 8, 16);
        }
    }

    /// <summary>"+3 notifications" or "See fewer" under the card; null for none.</summary>
    public string? FooterText
    {
        set
        {
            FooterButton.Content = value;
            FooterButton.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>The card was clicked: open its app.</summary>
    public event Action<NotificationCard>? Invoked;

    /// <summary>The close button: clear the notification (in the center) or put the toast away.</summary>
    public event Action<NotificationCard>? CloseRequested;

    public event Action<NotificationCard>? FooterClicked;

    public event Action<NotificationCard>? TurnOffRequested;

    public event Action? SettingsRequested;

    /// <summary>The pointer is over the card (a toast stays up meanwhile).</summary>
    public bool IsPointerOver { get; private set; }

    public event Action? PointerOverChanged;

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e) => SetPointerOver(true);

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e) => SetPointerOver(false);

    private void SetPointerOver(bool over)
    {
        IsPointerOver = over;
        if (!_isToast)
        {
            MoreButton.Opacity = CloseButton.Opacity = over ? 1 : 0;
            HoverPlate.Opacity = over ? 1 : 0;
        }
        PointerOverChanged?.Invoke();
    }

    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Not when one of the card's buttons was clicked.
        for (var element = e.OriginalSource as DependencyObject; element is not null && element != Root; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase)
                return;
        }
        Invoked?.Invoke(this);
    }

    /// <summary>Raised when the card was expanded or collapsed, changing its height.</summary>
    public event Action? Resized;

    private void Text_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (!_isToast && (TitleText.IsTextTrimmed || BodyText.IsTextTrimmed))
            ExpandButton.Visibility = Visibility.Visible;
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        bool expand = BodyText.MaxLines != 0;
        TitleText.MaxLines = expand ? 0 : 2;
        BodyText.MaxLines = expand ? 0 : 1;
        ExpandGlyph.Glyph = expand ? "" : "";
        AutomationProperties.SetName(ExpandButton, expand ? "Collapse" : "Expand");
        Resized?.Invoke();
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(MoreButton);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this);

    private void FooterButton_Click(object sender, RoutedEventArgs e) => FooterClicked?.Invoke(this);

    private void TurnOff_Click(object sender, RoutedEventArgs e) => TurnOffRequested?.Invoke(this);

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
