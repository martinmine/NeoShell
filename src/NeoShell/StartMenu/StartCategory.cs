using Microsoft.UI.Xaml;

namespace NeoShell.StartMenu;

/// <summary>A category card of All apps: its name and apps, and the four places its card shows them in.</summary>
internal sealed class StartCategory(string name, IReadOnlyList<StartItem> apps)
{
    public string Name { get; } = name;

    public IReadOnlyList<StartItem> Apps { get; } = apps;

    /// <summary>The apps, or with more than four the first three and a place with the next ones' icons that opens the category.</summary>
    public IReadOnlyList<CategoryCell> Cells => Apps.Count <= 4
        ? [.. Apps.Select(app => new CategoryCell(this, app, []))]
        : [.. Apps.Take(3).Select(app => new CategoryCell(this, app, [])), new CategoryCell(this, null, [.. Apps.Skip(3).Take(4)])];

    /// <summary>Explorer's name for the card, which lists its first apps.</summary>
    public string AutomationName => Apps.Count switch
    {
        1 => $"{Name} category with one Item, {Apps[0].Title}",
        2 => $"{Name} category with two Items, {Apps[0].Title} and {Apps[1].Title}",
        3 => $"{Name} category with three Items, {Apps[0].Title}, {Apps[1].Title} and {Apps[2].Title}",
        4 => $"{Name} category with four Items, {Apps[0].Title}, {Apps[1].Title}, {Apps[2].Title} and {Apps[3].Title}",
        _ => $"{Name} category with {Apps.Count} Items, {Apps[0].Title}, {Apps[1].Title}, {Apps[2].Title}, and {Apps.Count - 3} others",
    };
}

/// <summary>A place on a category's card: an app, or the icons of the apps after the first three.</summary>
internal sealed class CategoryCell(StartCategory category, StartItem? app, IReadOnlyList<StartItem> overflow)
{
    public StartCategory Category { get; } = category;

    public StartItem? App { get; } = app;

    public IReadOnlyList<StartItem> Overflow { get; } = overflow;

    public Visibility AppVisibility => App is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility OverflowVisibility => App is null ? Visibility.Visible : Visibility.Collapsed;

    public string Name => App?.Title ?? $"{Category.Name} overflow folder containing all {Category.Apps.Count} items";

    public string AutomationId => App is null ? "CategoryOverflow" : "CategoryApp";

    // ToolTips and UI Automation show the app's name; the overflow place has no tooltip in Explorer.
    public object? ToolTip => App?.Title;
}
