namespace NeoShell.Taskbar;

/// <summary>Which of a button's windows Win+1…9 and Win+Ctrl+1…9 bring to the front.</summary>
public static class TaskActivation
{
    /// <summary>
    /// Win+Ctrl+1…9: the window the user had in front last, the highest in <paramref name="zOrder"/> (topmost
    /// first); when one of them is in front already, the next one round, as <see cref="NextWindow"/>.
    /// </summary>
    public static nint LastActiveWindow(IReadOnlyList<nint> windows, IReadOnlyList<nint> zOrder, nint foreground)
    {
        if (windows.Contains(foreground))
            return NextWindow(windows, foreground);
        return zOrder.FirstOrDefault(windows.Contains, windows.Count > 0 ? windows[0] : 0);
    }

    /// <summary>
    /// The window after the one in front, wrapping around, so pressing the key again goes through all of them;
    /// the first window when none of them is in front.
    /// </summary>
    public static nint NextWindow(IReadOnlyList<nint> windows, nint foreground)
    {
        if (windows.Count == 0)
            return 0;

        int current = -1;
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i] == foreground)
                current = i;
        }
        return windows[(current + 1) % windows.Count];
    }
}
