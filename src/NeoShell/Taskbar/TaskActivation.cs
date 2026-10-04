namespace NeoShell.Taskbar;

/// <summary>Which of a button's windows Win+1…9 brings to the front.</summary>
public static class TaskActivation
{
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
