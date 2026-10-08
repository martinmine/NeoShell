using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NeoShell.Capture;

/// <summary>A snipping mode as the toolbar's list shows it (Snipping Tool's names and glyphs).</summary>
public sealed record SnipModeChoice(SnipMode Mode, string Name, string Glyph)
{
    public override string ToString() => Name;
}

public sealed partial class SnipToolbar : UserControl
{
    private static readonly SnipModeChoice[] s_choices =
    [
        new(SnipMode.Rectangle, "Rectangle", ""),
        new(SnipMode.Window, "Window", ""),
        new(SnipMode.FullScreen, "Full screen", ""),
        new(SnipMode.Freeform, "Freeform", ""),
    ];

    public SnipToolbar(SnipMode mode)
    {
        InitializeComponent();
        ModeBox.ItemsSource = s_choices;
        ModeBox.SelectedItem = s_choices.First(c => c.Mode == mode);
    }

    /// <summary>A mode was picked from the list.</summary>
    public event Action<SnipMode>? ModeChosen;

    public event Action? CloseRequested;

    /// <summary>Puts the keyboard on the toolbar, as Snipping Tool's overlay opens.</summary>
    public void FocusFirst() => ModeBox.Focus(FocusState.Programmatic);

    // Snipping Tool's chevron sits further in than a ComboBox's own, in a narrower column that leaves room for the
    // mode's glyph.
    private void ModeBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (FindByName(ModeBox, "DropDownGlyph") is { } glyph && VisualTreeHelper.GetParent(glyph) is Grid { ColumnDefinitions.Count: 2 } layout)
        {
            glyph.Margin = new Thickness(0, 0, 16, 0);
            layout.ColumnDefinitions[1].Width = new GridLength(28);
        }
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 0 && ModeBox.SelectedItem is SnipModeChoice choice)
            ModeChosen?.Invoke(choice.Mode);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private static FrameworkElement? FindByName(DependencyObject parent, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.Name == name)
                return element;
            if (FindByName(child, name) is { } found)
                return found;
        }
        return null;
    }
}
