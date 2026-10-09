using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NeoShell.Interop.Notifications;

namespace NeoShell.Notifications;

public sealed partial class NotificationCard : UserControl
{
    private readonly bool _isToast;
    // The toast's pictures, inputs and buttons; null when it has none.
    private readonly ToastContentView? _content;

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
            // The title 50 epx from the top, 21 from the bottom (measured with UI Automation on Explorer's, 25H2).
            Root.Padding = new Thickness(16, 4, 8, 21);
            TitleText.Margin = new Thickness(0, 6, 8, 0);
        }
        else
        {
            TimeText.Text = NotificationDisplay.TimeText(toast.Time, DateTime.Now, CultureInfo.CurrentCulture);
        }

        if (toast.Content is { } content)
            _content = ShowContent(content);
    }

    // What the toast's XML adds to its texts. A toast shows it all, its hero picture on top; a notification center
    // card shows its logo, header and attribution, and the rest (the hero under the texts) once expanded with the
    // chevron by its time, as Explorer's; a card with a hero picture starts out expanded.
    private ToastContentView? ShowContent(ToastContent content)
    {
        (_isToast ? UrgentGlyph : TimeUrgentGlyph).Visibility = content.IsUrgent ? Visibility.Visible : Visibility.Collapsed;
        if (content.Header is { } header)
        {
            HeaderText.Text = header;
            HeaderText.Visibility = Visibility.Visible;
            if (!_isToast)
                HeaderText.Margin = new Thickness(0, 4, 8, 4);
        }
        if (content.Attribution is { } attribution)
        {
            AttributionText.Text = attribution;
            AttributionText.Visibility = Visibility.Visible;
        }
        if (content.Logo is { } logo)
        {
            double size = ToastLayout.LogoSize(logo);
            LogoShape.Width = LogoShape.Height = size;
            LogoShape.RadiusX = LogoShape.RadiusY = logo.Circle ? size / 2 : 0;
            LogoShape.Fill = new ImageBrush { ImageSource = new BitmapImage(new Uri(logo.Uri)), Stretch = Stretch.UniformToFill };
            // Its top 4 above the title's; a toast ends 16 under it.
            LogoShape.Margin = LogoShape.Margin with
            {
                Top = Math.Max(0, TitleText.Margin.Top - 4) + (HeaderText.Visibility == Visibility.Visible ? 38 : 0),
                Bottom = -5,
            };
            LogoShape.Visibility = Visibility.Visible;
        }
        foreach (ToastAction action in content.ContextMenu.Reverse())
        {
            var item = new MenuFlyoutItem { Text = action.Content };
            AutomationProperties.SetAutomationId(item, "ToastMenuItem");
            item.Click += (_, _) => ButtonInvoked?.Invoke(this, action, _content?.InputValues() ?? []);
            MoreMenu.Items.Insert(0, item);
        }
        if (content.ContextMenu.Any())
            MoreMenu.Items.Insert(content.ContextMenu.Count(), new MenuFlyoutSeparator());

        if (_isToast && content.Hero is { } hero)
        {
            Hero = new BitmapImage(new Uri(hero.Uri));
            // The header as far under it as from the top of a toast without one.
            HeroImage.Margin = HeroImage.Margin with { Bottom = Root.Padding.Top };
        }
        var view = new ToastContentView(content, hero: !_isToast, edges: Root.Padding with { Right = Root.Padding.Right + 8 })
        {
            Margin = new Thickness(0, 21, _isToast ? 8 : 12, 0),
        };
        if (view.IsEmpty)
            return null;
        view.Invoked += action => ButtonInvoked?.Invoke(this, action, view.InputValues());
        view.InputPressed += () => KeyboardNeeded?.Invoke();
        // Pictures take their height once loaded.
        view.Resized += () => Resized?.Invoke();
        ContentHost.Content = view;
        if (_isToast)
        {
            ContentHost.Visibility = Visibility.Visible;
            Root.Padding = Root.Padding with { Bottom = 16 };
        }
        else
        {
            ExpandButton.Visibility = Visibility.Visible;
            if (content.Hero is not null)
                Expand(true);
        }
        return view;
    }

    /// <summary>One of the toast's buttons or menu items was used; with every input's value.</summary>
    public event Action<NotificationCard, ToastAction, IReadOnlyList<KeyValuePair<string, string>>>? ButtonInvoked;

    /// <summary>A text or selection box was clicked, or the menu opened: the window must take the keyboard.</summary>
    public event Action? KeyboardNeeded;

    /// <summary>Every input's ID and value, sent with a click on the toast as with its buttons.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> InputValues() => _content?.InputValues() ?? [];

    /// <summary>The user is typing in one of the toast's text boxes, or has its menu open: it stays meanwhile.</summary>
    public bool IsInUse => _content?.IsTyping == true || MoreMenu.IsOpen;

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

    /// <summary>
    /// A picture across the toast's top, above its header, as on Snipping Tool's toast with the snip. Toasts only.
    /// </summary>
    public ImageSource? Hero
    {
        set
        {
            HeroImage.Source = value;
            HeroImage.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
            // Edge to edge, over the card's padding.
            HeroImage.Margin = new Thickness(-Root.Padding.Left, -Root.Padding.Top, -Root.Padding.Right, 0);
        }
    }

    /// <summary>A button across the toast under its text, raising <see cref="ActionInvoked"/>; null for none.</summary>
    public string? ActionText
    {
        set
        {
            ActionButton.Content = value;
            ActionButton.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
            if (value is not null)
                Root.Padding = Root.Padding with { Bottom = 16 };
        }
    }

    /// <summary>The toast's button was clicked.</summary>
    public event Action<NotificationCard>? ActionInvoked;

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
        // Not when one of the card's buttons or inputs was clicked.
        for (var element = e.OriginalSource as DependencyObject; element is not null && element != Root; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase or TextBox or ComboBox)
                return;
        }
        Invoked?.Invoke(this);
    }

    // The "…" button's menu (with the toast's own items first), where the pointer is.
    private void Root_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null && element != Root; element = VisualTreeHelper.GetParent(element))
        {
            if (element is TextBox)
                return;
        }
        e.Handled = true;
        KeyboardNeeded?.Invoke();
        // Above the pointer, ending at it, as Explorer's.
        MoreMenu.ShowAt(Root, new FlyoutShowOptions { Position = e.GetPosition(Root), Placement = FlyoutPlacementMode.LeftEdgeAlignedBottom });
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
        Expand(BodyText.MaxLines != 0);
        Resized?.Invoke();
    }

    // All of the texts and the toast's content, or the trimmed texts alone.
    private void Expand(bool expand)
    {
        TitleText.MaxLines = expand ? 0 : 2;
        BodyText.MaxLines = expand ? 0 : 1;
        ContentHost.Visibility = expand && _content is not null ? Visibility.Visible : Visibility.Collapsed;
        ExpandGlyph.Glyph = expand ? "" : "";
        AutomationProperties.SetName(ExpandButton, expand ? "Collapse" : "Expand");
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(MoreButton);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this);

    private void FooterButton_Click(object sender, RoutedEventArgs e) => FooterClicked?.Invoke(this);

    private void ActionButton_Click(object sender, RoutedEventArgs e) => ActionInvoked?.Invoke(this);

    private void TurnOff_Click(object sender, RoutedEventArgs e) => TurnOffRequested?.Invoke(this);

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
