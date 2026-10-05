using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
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

    private static readonly Choice[] s_choices =
    [
        new("Sign out", "Closes all apps and signs you out.", Power.SignOut),
        new("Sleep", "The PC stays on but uses low power. Apps stay open so when the PC wakes up, you're instantly back to where you left off.", Power.Sleep),
        new("Shut down", "Closes all apps and turns off the PC.", Power.ShutDown),
        new("Restart", "Closes all apps, turns off the PC, and then turns it on again.", Power.Restart),
    ];

    private static ShutDownDialog? s_open;

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

        foreach (Choice choice in s_choices)
            _choice.Items.Add(choice.Name);
        AutomationProperties.SetName(_choice, "What do you want the computer to do?");
        AutomationProperties.SetAutomationId(_choice, "ShutDownChoice");
        _choice.SelectionChanged += (_, _) => _description.Text = s_choices[Math.Max(0, _choice.SelectedIndex)].Description;
        _choice.SelectedIndex = Array.FindIndex(s_choices, c => c.Name == "Shut down");

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

    private void Run()
    {
        Choice choice = s_choices[Math.Max(0, _choice.SelectedIndex)];
        Close();
        Log.Info($"Power: {choice.Name}");
        try
        {
            choice.Run();
        }
        catch (Win32Exception ex)
        {
            Log.Warn($"{choice.Name} failed", ex);
        }
    }

    private sealed record Choice(string Name, string Description, Action Run);
}
