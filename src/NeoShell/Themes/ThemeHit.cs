using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace NeoShell.Themes;

/// <summary>
/// The older Windows' themes draw their surfaces as pictures; each thing that can be clicked gets an invisible button over its place,
/// which takes the pointer, the keyboard and UI Automation (invoke, name, AutomationId) as a real control would.
/// </summary>
internal static class ThemeHit
{
    private static ControlTemplate? s_template;

    public static Button Create(string automationId, string name)
    {
        s_template ??= (ControlTemplate)XamlReader.Load(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
            "<Border Background='Transparent'/></ControlTemplate>");
        var button = new Button { Template = s_template, UseSystemFocusVisuals = false, IsTabStop = false };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>Puts an element over a rectangle of the art (art pixels are the overlay's units).</summary>
    public static void Place(FrameworkElement element, double x, double y, double width, double height)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }
}
