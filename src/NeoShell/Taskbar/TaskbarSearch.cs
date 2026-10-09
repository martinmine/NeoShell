using Microsoft.Win32;
using NeoShell.Logging;

namespace NeoShell.Taskbar;

/// <summary>Settings → Personalization → Taskbar → Search, with Explorer's values.</summary>
public enum TaskbarSearchMode
{
    Hidden = 0,
    Icon = 1,
    Box = 2,
    IconAndLabel = 3,
}

/// <summary>
/// The taskbar's search entry point, as Explorer's (Taskbar.View.dll, <c>SearchItemViewModel</c>): its setting, which
/// Explorer follows as Settings writes it, and the room each look takes.
/// </summary>
public static class TaskbarSearch
{
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string ValueName = "SearchboxTaskbarMode";

    /// <summary>Effective pixels each look takes on the taskbar, margins included.</summary>
    public const double IconWidth = 44;
    public const double BoxWidth = 2 + 220 + 2;
    public const double IconAndLabelWidth = 106;

    public static TaskbarSearchMode Read()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return FromValue(key?.GetValue(ValueName));
    }

    /// <summary>Explorer shows the search box when the value is missing or isn't one of the four.</summary>
    public static TaskbarSearchMode FromValue(object? value) =>
        value is int mode && Enum.IsDefined((TaskbarSearchMode)mode) ? (TaskbarSearchMode)mode : TaskbarSearchMode.Box;

    /// <summary>Saves the setting where Settings keeps it; Explorer, when it runs, follows at once.</summary>
    public static void Save(TaskbarSearchMode mode)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(ValueName, (int)mode, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($@"Could not save HKCU\{KeyPath}\{ValueName}", ex);
        }
    }

    public static double Width(TaskbarSearchMode mode) => mode switch
    {
        TaskbarSearchMode.Icon => IconWidth,
        TaskbarSearchMode.Box => BoxWidth,
        TaskbarSearchMode.IconAndLabel => IconAndLabelWidth,
        _ => 0,
    };

    /// <summary>
    /// The look shown: the box, and the icon with its label, give way to the icon alone when the task buttons, at their
    /// narrowest (<paramref name="taskWidth"/>), don't fit in the room left beside them (<paramref name="available"/>), as
    /// Explorer collapses them once its taskbar is full (<c>SearchItemViewModel::CanCollapse</c>).
    /// </summary>
    public static TaskbarSearchMode Shown(TaskbarSearchMode mode, double taskWidth, double available) =>
        taskWidth > available ? Collapsed(mode) : mode;

    /// <summary>The look on a full taskbar: the box, and the icon with its label, give way to the icon alone.</summary>
    public static TaskbarSearchMode Collapsed(TaskbarSearchMode mode) =>
        mode is TaskbarSearchMode.Box or TaskbarSearchMode.IconAndLabel ? TaskbarSearchMode.Icon : mode;
}
