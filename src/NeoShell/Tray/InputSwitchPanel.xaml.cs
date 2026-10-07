using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Interop.Input;

namespace NeoShell.Tray;

/// <summary>An input method in the switcher's list.</summary>
public sealed class InputMethodItem(InputMethod method)
{
    public InputMethod Method { get; } = method;

    public string? Glyph { get; } = IndicatorDisplay.InputMethodGlyph(method);

    public string Code => Method.LanguageCode;

    public string Language => Method.Language;

    public string Keyboard => Method.Keyboard;

    public string Name => $"{Method.Language} {Method.Keyboard}";

    public string AutomationId => $"InputMethod{Method.Id}";

    // Explorer's items with letters are 6 taller than those with a glyph, the extra above the text.
    public Thickness ContentMargin => new(0, Glyph is null ? 14 : 8, 0, 9);

    public Visibility CodeVisibility => Glyph is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GlyphVisibility => Glyph is null ? Visibility.Collapsed : Visibility.Visible;
}

public sealed partial class InputSwitchPanel : UserControl
{
    private IReadOnlyList<InputMethodItem> _items = [];

    public InputSwitchPanel()
    {
        InitializeComponent();
    }

    /// <summary>An input method was clicked: switch to it.</summary>
    public event Action<InputMethod>? MethodChosen;

    /// <summary>"More keyboard settings" was clicked.</summary>
    public event Action? SettingsRequested;

    /// <summary>The input method chosen in the list; null when there is none.</summary>
    public InputMethod? Selected => MethodList.SelectedIndex >= 0 ? _items[MethodList.SelectedIndex].Method : null;

    public int SelectedIndex => MethodList.SelectedIndex;

    /// <summary>
    /// Lists the input methods with <paramref name="selected"/> chosen; <paramref name="compact"/> leaves the header
    /// and footer out, as Win+Space's switcher does.
    /// </summary>
    public void Show(IReadOnlyList<InputMethod> methods, int selected, bool compact)
    {
        _items = [.. methods.Select(method => new InputMethodItem(method))];
        MethodList.ItemsSource = _items;
        MethodList.SelectedIndex = selected;
        // Explorer keeps the line above the footer.
        Header.Visibility = Footer.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Select(int index) => MethodList.SelectedIndex = index;

    /// <summary>Moves the focus to the chosen input method as a click would: without the keyboard's focus rectangle.</summary>
    public void HideFocusVisual()
    {
        if (MethodList.ContainerFromIndex(MethodList.SelectedIndex) is Control item)
            item.Focus(FocusState.Pointer);
    }

    private void MethodList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is InputMethodItem item)
            MethodChosen?.Invoke(item.Method);
    }

    private void Footer_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
