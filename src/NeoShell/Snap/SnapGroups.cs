namespace NeoShell.Snap;

/// <summary>
/// Snap groups, as Windows 11's: windows snapped together to fill a layout (by Snap Assist, a layout's suggestions,
/// or snapping into the space the others leave). The taskbar's previews and Alt+Tab show a group as one item that
/// brings all of its windows back. A window leaves when it's moved out of its zone or closed; a group of one is no
/// group. Minimizing keeps it.
/// </summary>
public sealed class SnapGroups
{
    private readonly List<List<nint>> _groups = [];

    /// <summary>The groups, each with two windows or more.</summary>
    public IReadOnlyList<IReadOnlyList<nint>> All => _groups;

    public event Action? Changed;

    /// <summary>
    /// A group's name in the taskbar's previews and Alt+Tab, as Explorer's: "Group | " and the first window's title
    /// (the most recently used), and how many others there are.
    /// </summary>
    public static string Title(IReadOnlyList<string> titles) => titles.Count switch
    {
        0 => "Group",
        1 => $"Group | {titles[0]}",
        2 => $"Group | {titles[0]} and 1 other window",
        _ => $"Group | {titles[0]} and {titles.Count - 1} other windows",
    };

    /// <summary>The group the window is in, or null.</summary>
    public IReadOnlyList<nint>? Of(nint window) => _groups.FirstOrDefault(group => group.Contains(window));

    /// <summary>Makes the windows a group of their own, taking them out of any group they were in.</summary>
    public void Join(IReadOnlyCollection<nint> windows)
    {
        if (windows.Distinct().Count() < 2)
            return;
        foreach (nint window in windows)
            Remove(window);
        _groups.Add([.. windows.Distinct()]);
        Changed?.Invoke();
    }

    /// <summary>The window left its zone or closed.</summary>
    public void Leave(nint window)
    {
        if (Remove(window))
            Changed?.Invoke();
    }

    private bool Remove(nint window)
    {
        List<nint>? group = _groups.FirstOrDefault(g => g.Contains(window));
        if (group is null)
            return false;
        group.Remove(window);
        if (group.Count < 2)
            _groups.Remove(group);
        return true;
    }
}
