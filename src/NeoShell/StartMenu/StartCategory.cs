using Microsoft.UI.Xaml;

namespace NeoShell.StartMenu;

/// <summary>
/// A category card of All: its name and items (apps and folders), and the four places its card shows them in. The card
/// stands for a folder by the folder's first app, as Explorer's (<c>AddAppOrFirstAppOfSuiteToGroup</c>).
/// </summary>
internal sealed class StartCategory(string name, IReadOnlyList<StartItem> items)
{
    public string Name { get; } = name;

    /// <summary>What the open category shows: its apps and folders.</summary>
    public IReadOnlyList<StartItem> Items { get; } = items;

    /// <summary>What the card shows: the apps, a folder by its first app.</summary>
    public IReadOnlyList<StartItem> Apps { get; } = [.. items.Select(item => item.SuiteApps?[0] ?? item)];

    /// <summary>Every app in it, those in folders too, as Explorer counts them.</summary>
    public int AppCount { get; } = items.Sum(item => item.SuiteApps?.Count ?? 1);

    /// <summary>The apps, or with more than four the first three and a place with the next ones' icons that opens the category.</summary>
    public IReadOnlyList<CategoryCell> Cells => Apps.Count <= 4
        ? [.. Apps.Select(app => new CategoryCell(this, app, []))]
        : [.. Apps.Take(3).Select(app => new CategoryCell(this, app, [])), new CategoryCell(this, null, [.. Apps.Skip(3).Take(4)])];

    /// <summary>Explorer's name for the card, which lists its first apps.</summary>
    public string AutomationName => (AppCount, Apps.Count) switch
    {
        (1, _) => $"{Name} category with one Item, {Apps[0].Title}",
        (2, 2) => $"{Name} category with two Items, {Apps[0].Title} and {Apps[1].Title}",
        (3, 3) => $"{Name} category with three Items, {Apps[0].Title}, {Apps[1].Title} and {Apps[2].Title}",
        (4, 4) => $"{Name} category with four Items, {Apps[0].Title}, {Apps[1].Title}, {Apps[2].Title} and {Apps[3].Title}",
        _ => $"{Name} category with {AppCount} Items, {string.Join(", ", Apps.Take(3).Select(app => app.Title))}, and {AppCount - Math.Min(3, Apps.Count)} others",
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

    public string Name => App?.AutomationName ?? $"{Category.Name} overflow folder containing all {Category.AppCount} items";

    public string AutomationId => App is null ? "CategoryOverflow" : "CategoryApp";

    // ToolTips and UI Automation show the app's name; the overflow place has no tooltip in Explorer.
    public object? ToolTip => App?.Title;
}
