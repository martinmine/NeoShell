using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace NeoShell.QuickSettings;

internal sealed partial class ShortcutKeys : UserControl
{
    private string _shortcut = "";

    public ShortcutKeys()
    {
        InitializeComponent();
    }

    /// <summary>The keys after the Windows key, separated by spaces: "Ctrl V".</summary>
    public string Shortcut
    {
        get => _shortcut;
        set
        {
            _shortcut = value;
            AutomationProperties.SetName(Keys, $"Windows key + {value.Replace(" ", " + ")}");
            while (Keys.Children.Count > 1)
                Keys.Children.RemoveAt(1);
            foreach (string key in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Keys.Children.Add(new Border
                {
                    Style = (Style)Resources["KeyCapStyle"],
                    Child = new TextBlock
                    {
                        FontSize = 10,
                        Text = key.ToUpperInvariant(),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                });
            }
        }
    }
}
