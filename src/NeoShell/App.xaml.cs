using Microsoft.UI.Xaml;

namespace NeoShell;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Placeholder until the taskbar lands in milestone 3.
        _window = new Window { Title = "NeoShell" };
        _window.Activate();
    }
}
