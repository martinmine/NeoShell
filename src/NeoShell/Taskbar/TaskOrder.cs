namespace NeoShell.Taskbar;

/// <summary>
/// The order the user dragged the buttons into, kept for the session as Windows 11 does: a running app that isn't
/// pinned stays where it was dropped instead of falling back in line on the next refresh.
/// </summary>
public static class TaskOrder
{
    /// <summary>
    /// Puts the buttons in <paramref name="order"/>. Buttons it doesn't know go right after the button they follow in
    /// <paramref name="natural"/> (a new window next to its app's others), first if they lead it, and last if they end
    /// it: that's where a newly started app is.
    /// </summary>
    /// <param name="natural">The buttons as <see cref="TaskListBuilder"/> orders them.</param>
    /// <param name="order">Button keys in the order last shown.</param>
    public static IReadOnlyList<TaskButtonModel> Arrange(IReadOnlyList<TaskButtonModel> natural, IReadOnlyList<string> order)
    {
        var byKey = natural.ToDictionary(button => button.Key);
        List<TaskButtonModel> arranged = [.. order.Distinct().Where(byKey.ContainsKey).Select(key => byKey[key])];
        var placed = arranged.Select(button => button.Key).ToHashSet();

        for (int i = 0; i < natural.Count; i++)
        {
            if (placed.Contains(natural[i].Key))
                continue;

            int at = i == 0 ? 0
                : i == natural.Count - 1 ? arranged.Count
                : arranged.FindIndex(button => button.Key == natural[i - 1].Key) + 1;
            arranged.Insert(at, natural[i]);
            placed.Add(natural[i].Key);
        }
        return arranged;
    }
}
