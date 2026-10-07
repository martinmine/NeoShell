using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using Windows.Graphics;
using Windows.System;

namespace NeoShell.Desktop;

/// <summary>
/// The Shut Down Windows dialog Explorer shows for Alt+F4 on the desktop: what the PC should do, picked from a list
/// that says what each choice means. shell32's own (<c>ExitWindowsDialog</c>) hands the request to Explorer's
/// taskbar, so without Explorer nothing shows.
/// </summary>
internal sealed class ShutDownDialog : Window
{
    private const double DialogWidth = 400;
    private const double DialogHeight = 250;

    private static ShutDownDialog? s_open;

    // What Windows offers as the dialog opens: Switch user, Sign out, the power states and the update choices.
    private readonly PowerOptions _options = PowerOptions.Read();
    private readonly PowerChoice[] _choices;

    private readonly ComboBox _choice = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap };

    /// <summary>Opens the dialog in front, or brings it back to the front when it's open already.</summary>
    public static void Open(ElementTheme theme)
    {
        if (s_open is null)
        {
            s_open = new ShutDownDialog(theme);
            s_open.Closed += (_, _) => s_open = null;
        }
        s_open.Activate();
    }

    private ShutDownDialog(ElementTheme theme)
    {
        Title = "Shut Down Windows";
        SystemBackdrop = new MicaBackdrop();
        AppWindow.TitleBar.PreferredTheme = theme == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);

        _choices = [.. _options.Choices(PowerMenu.ShutDownDialog)];
        foreach (PowerChoice choice in _choices)
            _choice.Items.Add(PowerItems.Name(choice, _options));
        AutomationProperties.SetName(_choice, "What do you want the computer to do?");
        AutomationProperties.SetAutomationId(_choice, "ShutDownChoice");
        _choice.SelectionChanged += (_, _) => _description.Text = PowerItems.Description(Chosen);
        if (_choices.Length > 0)
            _choice.SelectedIndex = Array.IndexOf(_choices, _options.DefaultChoice(_choices));

        var ok = new Button { Content = "OK", MinWidth = 96, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        AutomationProperties.SetAutomationId(ok, "ShutDownOkButton");
        ok.Click += (_, _) => Run();
        var cancel = new Button { Content = "Cancel", MinWidth = 96 };
        AutomationProperties.SetAutomationId(cancel, "ShutDownCancelButton");
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { ok, cancel },
        };
        var root = new Grid
        {
            RequestedTheme = theme,
            Padding = new Thickness(24, 16, 24, 24),
            RowSpacing = 12,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition(),
            },
        };
        root.Children.Add(new TextBlock { Text = "What do you want the computer to do?" });
        root.Children.Add(_choice);
        root.Children.Add(_description);
        root.Children.Add(buttons);
        for (int i = 0; i < root.Children.Count; i++)
            Grid.SetRow((FrameworkElement)root.Children[i], i);
        // Enter is OK and Esc is Cancel, as in a dialog, unless the list is open (it takes them to pick or close) or
        // Enter is for a focused button. Before the list sees them: closed, it would open on Enter.
        root.PreviewKeyDown += (_, e) =>
        {
            if (_choice.IsDropDownOpen || e.Key is not (VirtualKey.Enter or VirtualKey.Escape)
                || (e.Key == VirtualKey.Enter && FocusManager.GetFocusedElement(root.XamlRoot) is Button))
            {
                return;
            }
            e.Handled = true;
            if (e.Key == VirtualKey.Enter)
                Run();
            else
                Close();
        };
        root.Loaded += (_, _) => _choice.Focus(FocusState.Programmatic);
        Content = root;

        // In the middle of the primary monitor's work area.
        DisplayMonitor monitor = DisplayMonitor.GetAll().FirstOrDefault(m => m.IsPrimary) ?? DisplayMonitor.GetAll()[0];
        double scale = monitor.Dpi / 96.0;
        var size = new SizeInt32((int)(DialogWidth * scale), (int)(DialogHeight * scale));
        RectInt32 area = monitor.WorkArea;
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));
    }

    private PowerChoice Chosen => _choices[Math.Max(0, _choice.SelectedIndex)];

    private void Run()
    {
        bool shift = PowerItems.IsShiftDown();
        Close();
        if (_choices.Length > 0)
            PowerItems.Run(Chosen, shift);
    }
}
