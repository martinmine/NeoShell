using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Shell;
using NeoShell.Logging;

namespace NeoShell.Taskbar;

/// <summary>
/// The entries of an app's jump list in a menu, as Explorer's taskbar and Start show them (T42): each with its icon and
/// tooltip, a pin button while hovered, and a menu of its own (Open, Open file location, Pin to / Unpin from this list,
/// Remove from this list, Properties). Pinning and removing change the list at once, through the shell's own lists, so
/// the menu is filled afresh (<paramref name="changed"/>) and stays open, as Explorer's does.
/// </summary>
/// <param name="opening">Called before an entry opens (Start closes first).</param>
internal sealed class JumpListMenu(string appId, nint owner, Action opening, Action changed)
{
    private const string PinGlyph = "";
    private const string UnpinGlyph = "";

    /// <summary>The menu item for the entry, its icon <paramref name="iconSize"/> pixels.</summary>
    public MenuFlyoutItem Entry(JumpListItem entry, int iconSize)
    {
        var icon = new ImageIcon();
        var item = new MenuFlyoutItem { Text = entry.Title, Icon = icon };
        item.Resources["MenuFlyoutItemTextTrimming"] = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetAutomationId(item, "JumpListItem");
        if (entry.ToolTip is { } toolTip)
            ToolTipService.SetToolTip(item, toolTip);
        item.Click += (_, _) => Open(entry);
        item.ContextFlyout = EntryMenu(entry);
        // Tasks can't be pinned: no button, and Open is all their menu has.
        if (!entry.IsTask)
            item.Loaded += (_, _) => AddPinButton(item, entry);
        AppIcons.Load(() => JumpLists.GetIcon(entry, iconSize), source => icon.Source = source);
        return item;
    }

    // Explorer's right-click menu of an entry: Open for a task; an app's own link adds pinning and removing; a file adds
    // its location and properties, a web page its location (left out here: it has none to open).
    private MenuFlyout EntryMenu(JumpListItem entry)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Command("Open", "", "JumpListOpenMenuItem", () => Open(entry)));
        if (entry.IsTask)
            return menu;

        if (entry.IsFileSystem)
            menu.Items.Add(Command("Open file location", "", "JumpListOpenLocationMenuItem", () => Run(entry, "open the location of", () => JumpLists.OpenLocation(entry))));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(entry.IsPinned
            ? Command("Unpin from this list", UnpinGlyph, "JumpListUnpinMenuItem", () => Change(entry, () => JumpLists.Unpin(appId, entry)))
            : Command("Pin to this list", PinGlyph, "JumpListPinMenuItem", () => Change(entry, () => JumpLists.Pin(appId, entry))));
        if (!entry.IsPinned)
            menu.Items.Add(Command("Remove from this list", "", "JumpListRemoveMenuItem", () => Change(entry, () => JumpLists.Remove(appId, entry))));
        if (entry.IsFileSystem)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Command("Properties", "", "JumpListPropertiesMenuItem", () => Run(entry, "show the properties of", () => JumpLists.ShowProperties(entry, owner))));
        }
        return menu;
    }

    // Explorer's pin button: at the right end of the hovered entry, as tall as it, 45 wide, the pin (or unpin) glyph.
    // Put into the item's own template (its root grid's last column, where a shortcut's text would go) rather than a
    // template of NeoShell's, so the item keeps WinUI's look and states.
    private void AddPinButton(MenuFlyoutItem item, JumpListItem entry)
    {
        if (VisualTreeHelper.GetChildrenCount(item) == 0 || VisualTreeHelper.GetChild(item, 0) is not Grid root
            || root.Children.OfType<Button>().Any())
        {
            return;
        }

        string text = entry.IsPinned ? "Unpin from this list" : "Pin to this list";
        Thickness padding = root.Padding;
        var button = new Button
        {
            Content = new FontIcon { Glyph = entry.IsPinned ? UnpinGlyph : PinGlyph, FontSize = 16 },
            Width = 45,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CornerRadius = item.CornerRadius,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, -padding.Top, -padding.Right, -padding.Bottom),
            Visibility = Visibility.Collapsed,
        };
        button.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///FlatButtons.xaml") });
        ToolTipService.SetToolTip(button, text);
        AutomationProperties.SetName(button, text);
        AutomationProperties.SetAutomationId(button, "JumpListPinButton");
        button.Click += (_, _) => Change(entry, () =>
        {
            if (entry.IsPinned)
                JumpLists.Unpin(appId, entry);
            else
                JumpLists.Pin(appId, entry);
        });
        Grid.SetColumn(button, Math.Max(0, root.ColumnDefinitions.Count - 1));
        root.Children.Add(button);

        item.PointerEntered += (_, _) => button.Visibility = Visibility.Visible;
        item.PointerExited += (_, _) => button.Visibility = Visibility.Collapsed;
    }

    private void Open(JumpListItem entry)
    {
        opening();
        Run(entry, "open", () => JumpLists.Open(entry, owner));
    }

    private void Change(JumpListItem entry, Action change)
    {
        if (Run(entry, "change", change))
            changed();
    }

    private bool Run(JumpListItem entry, string what, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not {what} the jump list item {entry.Title} of {appId}", ex);
            return false;
        }
    }

    private static MenuFlyoutItem Command(string text, string glyph, string automationId, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => onClick();
        return item;
    }
}
