using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using NeoShell.Interop.Notifications;
using Windows.System;

namespace NeoShell.Notifications;

/// <summary>
/// What a toast shows under its texts, as Explorer's toasts and expanded notification center cards: inline pictures,
/// the progress bar, text and selection boxes (a text box with its send button beside it) and rows of buttons, 16
/// effective pixels apart.
/// </summary>
internal sealed partial class ToastContentView : StackPanel
{
    private const double Gap = 16;
    private readonly List<(string Id, Func<string> Value)> _inputs = [];

    /// <param name="hero">Also the hero picture, first and edge to edge over <paramref name="edges"/>: the notification
    /// center shows it here, under the texts; a toast has it on top instead.</param>
    public ToastContentView(ToastContent content, bool hero, Thickness edges)
    {
        Content = content;
        if (hero && content.Hero is { } heroImage)
            Children.Add(Picture(heroImage, double.PositiveInfinity, new Thickness(-edges.Left, 0, -edges.Right, 0)));
        foreach (ToastImage image in content.Images)
            Children.Add(image.Circle ? Circle(image, 96) : Picture(image, 204, default));
        foreach (Image picture in Children.OfType<Image>())
            picture.ImageOpened += (_, _) => Resized?.Invoke();
        if (content.Progress is { } progress)
            Children.Add(Progress(progress));
        foreach (ToastInput input in content.Inputs)
            Children.Add(Input(input));
        foreach (IReadOnlyList<ToastAction> row in ToastLayout.ButtonRows(content))
            Children.Add(ButtonRow(row));

        for (int i = 1; i < Children.Count; i++)
            ((FrameworkElement)Children[i]).Margin = ((FrameworkElement)Children[i]).Margin with { Top = Gap };
    }

    public ToastContent Content { get; }

    public bool IsEmpty => Children.Count == 0;

    /// <summary>A button (or a text box's Enter) was used.</summary>
    public event Action<ToastAction>? Invoked;

    /// <summary>A picture loaded: the content's height changed.</summary>
    public event Action? Resized;

    /// <summary>A text or selection box was clicked: the toast's window must take the keyboard.</summary>
    public event Action? InputPressed;

    /// <summary>Each input's ID and value, as Explorer's toasts send them with any button: every text box's text
    /// (empty too) and every selection box's chosen ID.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> InputValues() =>
        [.. _inputs.Select(i => KeyValuePair.Create(i.Id, i.Value()))];

    /// <summary>Whether a text box has the keyboard (the user is typing a reply: the toast stays).</summary>
    public bool IsTyping => _inputs.Count > 0 && FocusManager.GetFocusedElement(XamlRoot) is TextBox box && IsAncestorOf(box);

    private bool IsAncestorOf(DependencyObject element)
    {
        for (DependencyObject? e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e == this)
                return true;
        }
        return false;
    }

    // A picture across the content's width, as tall as its shape asks up to maxHeight, centred.
    private static Image Picture(ToastImage image, double maxHeight, Thickness margin) => new()
    {
        Source = new BitmapImage(new Uri(image.Uri)),
        Stretch = Stretch.Uniform,
        MaxHeight = maxHeight,
        Margin = margin,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static Ellipse Circle(ToastImage image, double size) => new()
    {
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        Fill = new ImageBrush { ImageSource = new BitmapImage(new Uri(image.Uri)), Stretch = Stretch.UniformToFill },
    };

    // The title over the bar, the status under it with the value at its right; 8 apart.
    private static StackPanel Progress(ToastProgress progress)
    {
        var panel = new StackPanel { Spacing = 8 };
        if (progress.Title is { } title)
            panel.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
        // A 4 epx track in the accent colour over a dim one, as Explorer's.
        var bar = new ProgressBar
        {
            MinHeight = 4,
            Maximum = 1,
            Value = progress.Value ?? 0,
            IsIndeterminate = progress.Value is null,
            Foreground = (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"],
            Background = (Brush)Application.Current.Resources["ControlStrongFillColorDisabledBrush"],
        };
        bar.Resources["ProgressBarTrackHeight"] = 4.0;
        bar.Resources["ProgressBarMinHeight"] = 4.0;
        panel.Children.Add(bar);
        // Explorer leaves 21 under it rather than 16.
        var status = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        status.ColumnDefinitions.Add(new ColumnDefinition());
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.Children.Add(Secondary(new TextBlock { Text = progress.Status, TextTrimming = TextTrimming.CharacterEllipsis }));
        var value = Secondary(new TextBlock { Text = ToastLayout.ProgressValueText(progress), Margin = new Thickness(12, 0, 0, 0) });
        Grid.SetColumn(value, 1);
        status.Children.Add(value);
        panel.Children.Add(status);
        return panel;
    }

    private static TextBlock Secondary(TextBlock text)
    {
        text.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        return text;
    }

    private FrameworkElement Input(ToastInput input)
    {
        FrameworkElement box = input.IsText ? TextInput(input) : Selection(input);
        if (input.Title is null)
            return box;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = input.Title });
        panel.Children.Add(box);
        return panel;
    }

    // A text box, with the button whose hint-inputId names it beside it (a reply's send button: its icon if it has
    // one); Enter in the box presses that button.
    private FrameworkElement TextInput(ToastInput input)
    {
        var box = new TextBox
        {
            PlaceholderText = input.Placeholder ?? "",
            Text = input.Default ?? "",
            MinHeight = 32,
            AcceptsReturn = false,
        };
        AutomationProperties.SetAutomationId(box, "ToastInput" + input.Id);
        AutomationProperties.SetName(box, input.Placeholder ?? input.Title ?? input.Id);
        box.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => InputPressed?.Invoke()), handledEventsToo: true);
        _inputs.Add((input.Id, () => box.Text));

        if (Content.ButtonBeside(input) is not { } send)
            return box;

        var button = Button(send, iconAbove: false);
        button.Width = 49;
        if (send.ImageUri is { } icon)
            button.Content = new Image { Source = new BitmapImage(new Uri(icon)), Width = 32, Height = 30, Stretch = Stretch.Uniform };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                Invoked?.Invoke(send);
            }
        };
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(box);
        Grid.SetColumn(button, 1);
        row.Children.Add(button);
        return row;
    }

    private ComboBox Selection(ToastInput input)
    {
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 32 };
        AutomationProperties.SetAutomationId(box, "ToastInput" + input.Id);
        AutomationProperties.SetName(box, input.Title ?? "Select");
        foreach (ToastChoice choice in input.Choices)
            box.Items.Add(new ComboBoxItem { Content = choice.Content, Tag = choice.Id });
        box.SelectedIndex = Math.Max(0, input.Choices.ToList().FindIndex(c => c.Id == input.Default));
        box.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => InputPressed?.Invoke()), handledEventsToo: true);
        _inputs.Add((input.Id, () => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? ""));
        return box;
    }

    // Equal widths, 8 apart; taller with icons above the text.
    private Grid ButtonRow(IReadOnlyList<ToastAction> row)
    {
        bool iconAbove = ToastLayout.IconsAbove(Content) && row.Any(b => b.ImageUri is not null);
        var grid = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < row.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Button button = Button(row[i], iconAbove);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(button, i);
            grid.Children.Add(button);
        }
        return grid;
    }

    private Button Button(ToastAction action, bool iconAbove)
    {
        var button = new Button
        {
            Height = iconAbove ? 61 : 32,
            Padding = new Thickness(8, 0, 8, 0),
            Content = ButtonContent(action, iconAbove),
        };
        AutomationProperties.SetAutomationId(button, "ToastButton");
        AutomationProperties.SetName(button, action.Content);
        if (ToastLayout.IsAccent(Content, action))
            button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        else if (ToastLayout.Style(Content, action) is { } style)
        {
            string fill = string.Equals(style, "Success", StringComparison.OrdinalIgnoreCase) ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush";
            button.Resources["ButtonBackground"] = Application.Current.Resources[fill];
            button.Resources["ButtonBackgroundPointerOver"] = Application.Current.Resources[fill];
            button.Resources["ButtonBackgroundPressed"] = Application.Current.Resources[fill];
            var foreground = Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
            button.Resources["ButtonForeground"] = foreground;
            button.Resources["ButtonForegroundPointerOver"] = foreground;
            button.Resources["ButtonForegroundPressed"] = foreground;
        }
        button.Click += (_, _) => Invoked?.Invoke(action);
        return button;
    }

    // The text, with the icon over it (12 pt text) or beside it.
    private static object ButtonContent(ToastAction action, bool iconAbove)
    {
        if (action.ImageUri is not { } icon)
            return new TextBlock { Text = action.Content, TextTrimming = TextTrimming.CharacterEllipsis };

        var image = new Image { Source = new BitmapImage(new Uri(icon)), Width = 16, Height = 16 };
        var text = new TextBlock { Text = action.Content, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (iconAbove)
        {
            text.Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
            text.HorizontalAlignment = HorizontalAlignment.Center;
            return new StackPanel { Spacing = 8, Children = { image, text } };
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { image, text } };
    }
}
