using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Input;
using NeoShell.Interop.Shell;
using Windows.System;
using Windows.UI.Core;
using Windows.UI;

namespace NeoShell.AutoPlay;

/// <summary>The flyout's content: the drive, the question and the choices, grouped as Explorer groups them.</summary>
public sealed partial class AutoPlayChooser : UserControl
{
    private const double IconSize = 32;
    private readonly List<Button> _choices = [];

    /// <param name="headers">The group headers' texts: a disc's program, an enhanced disc's, and "Other choices".</param>
    /// <param name="tileColor">The tiles behind the icons, the immersive colour Explorer uses.</param>
    /// <param name="selected">The choice the keyboard starts on (the one last picked), else the first.</param>
    internal AutoPlayChooser(
        string title, string detail, IReadOnlyList<AutoPlayGroup> groups, (string Program, string Enhanced, string Other) headers,
        Color tileColor, string root, double scale, string? selected)
    {
        InitializeComponent();
        // Explorer wraps the name in left-to-right marks, which show in its accessible name too.
        TitleText.Text = $"‪{title}‬";
        DetailText.Text = detail;

        bool afterProgram = false;
        foreach (AutoPlayGroup group in groups)
        {
            if (group.Kind is AutoPlayGroupKind.Program or AutoPlayGroupKind.EnhancedContent)
            {
                ChoicesPanel.Children.Add(Header(group.Kind == AutoPlayGroupKind.Program ? headers.Program : headers.Enhanced));
                afterProgram = true;
            }
            else if (afterProgram)
            {
                ChoicesPanel.Children.Add(Header(headers.Other));
                afterProgram = false;
            }
            foreach (AutoPlayHandler handler in group.Handlers)
                ChoicesPanel.Children.Add(Choice(handler, tileColor, root, scale));
        }

        // As twinui's _CalculateScrollViewerHeight: up to five choices show whole; more scroll, four and a half
        // showing; the headers add 80.
        int count = _choices.Count;
        Scroller.Height = (count <= 5 ? 60 * count + 30 : 280) + (groups.Any(g => g.Kind is AutoPlayGroupKind.Program or AutoPlayGroupKind.EnhancedContent) ? 80 : 0);

        Button? start = _choices.FirstOrDefault(c => ((AutoPlayHandler)c.Tag).Name == selected) ?? _choices.FirstOrDefault();
        Loaded += (_, _) => start?.Focus(FocusState.Programmatic);
        // As Explorer's, the arrow keys and Tab stop at the ends.
        ChoicesPanel.KeyDown += ChoicesPanel_KeyDown;
    }

    public event Action<AutoPlayHandler>? Chosen;

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        Margin = new Thickness(20, 10, 20, 10),
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 14.667,
        FontWeight = FontWeights.SemiBold,
        LineHeight = 20,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        CharacterSpacing = 9,
        Foreground = new SolidColorBrush(Colors.Black),
    };

    private Button Choice(AutoPlayHandler handler, Color tileColor, string root, double scale)
    {
        var icon = new Image { Width = IconSize, Height = IconSize };
        LoadIcon(icon, handler.Icon, root, (int)Math.Round(IconSize * scale));
        var tile = new Border
        {
            Width = 40,
            Height = 40,
            Margin = new Thickness(20, 10, 0, 10),
            Background = new SolidColorBrush(tileColor),
            Child = icon,
        };

        var texts = new StackPanel { Margin = new Thickness(73, 10, 20, 0) };
        var style = (Style)Resources["ChoiceTextStyle"];
        texts.Children.Add(new TextBlock { Text = handler.Action, Style = style, Foreground = new SolidColorBrush(Colors.Black) });
        // The provider takes the row's colour: grey, black under the pointer.
        if (handler.Provider is { Length: > 0 } provider)
            texts.Children.Add(new TextBlock { Text = provider, Style = style });

        var content = new Grid { Height = 60 };
        content.Children.Add(tile);
        content.Children.Add(texts);
        tile.HorizontalAlignment = HorizontalAlignment.Left;

        var button = new Button { Content = content, Style = (Style)Resources["ChoiceStyle"], Tag = handler };
        AutomationProperties.SetName(button, handler.Action);
        AutomationProperties.SetAutomationId(button, "AutoPlayChoice" + handler.Name);
        button.Click += (_, _) => Chosen?.Invoke(handler);
        _choices.Add(button);
        return button;
    }

    // As twinui's AutoPlayTile: an icon location, else an image file (a packaged app's logo); none at all is the drive's.
    private static void LoadIcon(Image image, string? location, string root, int size)
    {
        if (location is null)
        {
            AppIcons.Load(() => ShellItems.GetIcon(root, size), source => image.Source = source);
            return;
        }
        if (Path.GetExtension(location).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" && File.Exists(location))
            image.Source = new BitmapImage(new Uri(location)) { DecodePixelWidth = size };
        else
            AppIcons.Load(() => AutoPlayHandlers.ExtractIcon(location, size), source => image.Source = source);
    }

    private void ChoicesPanel_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        int index = _choices.FindIndex(c => c.FocusState != FocusState.Unfocused);
        bool back = e.Key == VirtualKey.Up
            || (e.Key == VirtualKey.Tab && InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down));
        if (index < 0 || e.Key is not (VirtualKey.Down or VirtualKey.Up or VirtualKey.Tab))
            return;

        int next = back ? index - 1 : index + 1;

        e.Handled = true;
        if (next >= 0 && next < _choices.Count)
            _choices[next].Focus(FocusState.Keyboard);
    }
}
