using NeoShell.Settings;

namespace NeoShell.Taskbar;

/// <summary>
/// Dragging things from other apps or the desktop over the taskbar, as in Windows 11: an app (one program or a shortcut to one)
/// is pinned where it's dropped, and the buttons make way for it; anything else can't be dropped, but hovering a
/// button brings its window forward so the drop can go there.
/// </summary>
public static class TaskbarDrop
{
    /// <summary>Hovering a button this long with a drag brings its window forward, or shows its windows' previews (Explorer: ~410 ms).</summary>
    public static readonly TimeSpan ButtonHoverDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Hovering a window's preview this long with a drag brings that window forward. Explorer takes ~1.1 s from the
    /// pointer arriving; WinUI tells the previews' window of the drag ~0.1 s after it does.
    /// </summary>
    public static readonly TimeSpan PreviewHoverDelay = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// The app the dragged files pin, or null when they pin nothing: exactly one file, a program or a shortcut to
    /// one. A shortcut pins its program with its arguments, under the shortcut's name; the program's windows share
    /// its button, as in Explorer.
    /// </summary>
    /// <param name="readShortcut">A shortcut's target and arguments, or null if it can't be read.</param>
    /// <param name="programName">A program's name, as Explorer names a pinned program: its file description.</param>
    public static PinnedApp? AppToPin(
        IReadOnlyList<string> files, Func<string, (string Target, string? Arguments)?> readShortcut, Func<string, string> programName)
    {
        if (files.Count != 1)
            return null;

        string file = files[0];
        if (IsProgram(file))
            return new PinnedApp(programName(file), Path: file);
        if (!Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || readShortcut(file) is not { } shortcut || !IsProgram(shortcut.Target))
            return null;

        return new PinnedApp(
            Path.GetFileNameWithoutExtension(file),
            Path: shortcut.Target,
            Arguments: string.IsNullOrWhiteSpace(shortcut.Arguments) ? null : shortcut.Arguments);
    }

    /// <summary>Where a dragged app goes among the buttons (left edges and widths along the list): before the first one whose middle is past the pointer.</summary>
    public static int InsertionIndex(IReadOnlyList<(double Left, double Width)> slots, double x) =>
        slots.Count(slot => slot.Left + slot.Width / 2 < x);

    /// <summary>How far the button at <paramref name="index"/> moves to open a gap of <paramref name="width"/> at <paramref name="gap"/>: the ones after it make way.</summary>
    public static double GapOffset(int index, int gap, double width) => index >= gap ? width : 0;

    /// <summary>
    /// The buttons' order and the pinned apps' order once <paramref name="app"/> is dropped at <paramref name="index"/>
    /// among <paramref name="buttons"/> (their keys and pinned apps, as shown). An app that's running already moves
    /// there with its windows.
    /// </summary>
    public static (IReadOnlyList<string> Order, IReadOnlyList<PinnedApp> Pinned) Pin(
        IReadOnlyList<(string Key, PinnedApp? Pinned)> buttons, PinnedApp app, int index)
    {
        string key = TaskGrouping.Key(app);
        var order = buttons.ToList();
        int existing = order.FindIndex(button => string.Equals(button.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            order.RemoveAt(existing);
            if (existing < index)
                index--;
        }
        order.Insert(Math.Clamp(index, 0, order.Count), (key, app));
        return (
            [.. order.Select(button => button.Key)],
            [.. order.Select(button => button.Pinned).OfType<PinnedApp>().DistinctBy(TaskGrouping.Key, StringComparer.OrdinalIgnoreCase)]);
    }

    private static bool IsProgram(string path) => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
}
