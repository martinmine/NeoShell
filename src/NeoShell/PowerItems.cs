using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;
using NeoShell.Logging;
using Windows.UI;

namespace NeoShell;

/// <summary>
/// The power menus' choices (Start's power button, the Quick Link menu, the Shut Down Windows dialog): their names
/// and descriptions as shutdownux.dll words them, Start's glyphs, and what each does.
/// </summary>
public static class PowerItems
{
    /// <summary>The orange dot Start puts on the update choices and on the power button while an update waits.</summary>
    public const string UpdateDotGlyph = "";

    public static readonly Color UpdateDotColor = Color.FromArgb(0xFF, 0xFF, 0x99, 0x00);

    public static string Name(PowerChoice choice, PowerOptions options) => choice switch
    {
        PowerChoice.SwitchUser => "Switch user",
        PowerChoice.SignOut => "Sign out",
        PowerChoice.Lock => "Lock",
        PowerChoice.Sleep => "Sleep",
        PowerChoice.Hibernate => "Hibernate",
        PowerChoice.UpdateAndShutDown => "Update and shut down" + Estimate(options.EstimateLow, options.EstimateHigh),
        PowerChoice.ShutDown => "Shut down",
        PowerChoice.UpdateAndRestart => "Update and restart" + Estimate(options.EstimateLow, options.EstimateHigh),
        _ => "Restart",
    };

    /// <summary>Windows Update's estimate of the downtime, worded as shutdownux does; empty when there's none.</summary>
    public static string Estimate(int low, int high) => (low, high) switch
    {
        (0, 0) => "",
        (0, _) => $" (estimate: up to {Minutes(high)})",
        (_, 0) => $" (estimate: more than {Minutes(low)})",
        _ when low == high => $" (estimate: {Minutes(low)})",
        _ => high == 60 ? $" (estimate: {low} min - 1 hr)" : $" (estimate: {low} - {high} min)",
    };

    private static string Minutes(int minutes) => minutes == 60 ? "1 hr" : $"{minutes} min";

    /// <summary>What the choice does: Start's tooltip and the Shut Down Windows dialog's line.</summary>
    public static string Description(PowerChoice choice) => choice switch
    {
        PowerChoice.SwitchUser => "Switch users without closing apps.",
        PowerChoice.SignOut => "Closes all apps and signs you out.",
        PowerChoice.Lock => "Locks your account on this PC.",
        PowerChoice.Sleep => "The PC stays on but uses low power. Apps stay open so when the PC wakes up, you’re instantly back to where you left off.",
        PowerChoice.Hibernate => "Turns off the PC but apps stay open. When the PC is turned on, you’re back to where you left off.",
        PowerChoice.UpdateAndShutDown => "Closes all apps, updates the PC and then turns it off.",
        PowerChoice.ShutDown => "Closes all apps and turns off the PC.",
        PowerChoice.UpdateAndRestart => "Closes all apps, updates the PC, turns it off and then turns it on again.",
        _ => "Closes all apps, turns off the PC and then turns it on again.",
    };

    /// <summary>Start's glyph; the update choices' have a gap at the top right for the orange dot.</summary>
    public static string Glyph(PowerChoice choice) => choice switch
    {
        PowerChoice.SwitchUser => "",
        PowerChoice.SignOut => "",
        PowerChoice.Lock => "",
        PowerChoice.Sleep => "",
        PowerChoice.Hibernate => "",
        PowerChoice.UpdateAndShutDown => "",
        PowerChoice.ShutDown => "",
        PowerChoice.UpdateAndRestart => "",
        _ => "",
    };

    public static bool IsUpdate(PowerChoice choice) => choice is PowerChoice.UpdateAndShutDown or PowerChoice.UpdateAndRestart;

    public static string AutomationId(PowerChoice choice) => $"{choice}MenuItem";

    /// <summary>
    /// A Start-style icon for the choice. A menu item takes one icon, so an update choice's dot goes into the glyph's
    /// own grid, over it, once the glyph is in the tree.
    /// </summary>
    public static FontIcon Icon(PowerChoice choice)
    {
        var icon = new FontIcon { Glyph = Glyph(choice) };
        if (IsUpdate(choice))
        {
            icon.Loaded += (_, _) =>
            {
                if (VisualTreeHelper.GetChildrenCount(icon) > 0 && VisualTreeHelper.GetChild(icon, 0) is Grid grid && grid.Children.Count == 1)
                    grid.Children.Add(UpdateDot(icon.FontSize));
            };
        }
        return icon;
    }

    public static FontIcon UpdateDot(double fontSize) =>
        new() { Glyph = UpdateDotGlyph, FontSize = fontSize, Foreground = new SolidColorBrush(UpdateDotColor) };

    /// <param name="shift">Shift was held on the click: Restart goes to the boot options, Shut down skips Fast Startup.</param>
    public static void Run(PowerChoice choice, bool shift)
    {
        Log.Info($"Power: {choice}{(shift ? " (Shift)" : "")}");
        try
        {
            switch (choice)
            {
                case PowerChoice.SwitchUser: Power.SwitchUser(); break;
                case PowerChoice.SignOut: Power.SignOut(); break;
                case PowerChoice.Lock: Power.Lock(); break;
                case PowerChoice.Sleep: Power.Sleep(); break;
                case PowerChoice.Hibernate: Power.Hibernate(); break;
                case PowerChoice.UpdateAndShutDown: Power.ShutDown(installUpdates: true, shift); break;
                case PowerChoice.ShutDown: Power.ShutDown(installUpdates: false, shift); break;
                case PowerChoice.UpdateAndRestart: Power.Restart(installUpdates: true, shift); break;
                case PowerChoice.Restart: Power.Restart(installUpdates: false, shift); break;
            }
        }
        catch (Win32Exception ex)
        {
            Log.Warn($"{choice} failed", ex);
        }
    }

    public static bool IsShiftDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
