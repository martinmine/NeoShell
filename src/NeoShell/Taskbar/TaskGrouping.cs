using NeoShell.Interop.Windowing;
using NeoShell.Settings;

namespace NeoShell.Taskbar;

/// <summary>Which windows belong to the same app, and to which pinned app.</summary>
public static class TaskGrouping
{
    /// <summary>Windows with the same key share a button: the AppUserModelID if any, else the executable.</summary>
    public static string Key(WindowInfo window) =>
        window.AppUserModelId ?? window.ProcessPath ?? $"window:{window.Handle}";

    public static string Key(PinnedApp app) => app.AppUserModelId ?? app.Path ?? app.DisplayName;

    /// <summary>
    /// A window with an AppUserModelID matches only that ID; one without matches by executable path, which is how
    /// a pinned classic app finds its windows.
    /// </summary>
    public static bool Matches(PinnedApp app, WindowInfo window) =>
        window.AppUserModelId is { } id
            ? string.Equals(app.AppUserModelId, id, StringComparison.OrdinalIgnoreCase)
            : app.Path is not null && string.Equals(app.Path, window.ProcessPath, StringComparison.OrdinalIgnoreCase);

    public static bool SameApp(PinnedApp a, PinnedApp b) =>
        string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The app a window belongs to, for launching another instance or pinning it.</summary>
    public static PinnedApp AppFor(WindowInfo window, string displayName) =>
        new(displayName, window.AppUserModelId, window.AppUserModelId is null ? window.ProcessPath : null);
}
