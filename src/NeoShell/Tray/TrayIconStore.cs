using NeoShell.Interop.Tray;

namespace NeoShell.Tray;

/// <summary>A tray icon as the latest <c>Shell_NotifyIcon</c> calls left it.</summary>
/// <param name="IconHandle">The app's HICON; its pixels are copied when it arrives, as the app may destroy it.</param>
public sealed record TrayIconState(
    nint Window,
    uint Id,
    Guid? Guid,
    uint CallbackMessage,
    nint IconHandle,
    string Tip,
    bool IsHidden,
    uint Version,
    bool ShowTip)
{
    /// <summary>Identity: the GUID when the app gave one, else its window and ID.</summary>
    public string Key => Guid is { } guid ? guid.ToString() : $"{Window:X}:{Id}";

    /// <summary>
    /// One of Windows' own system icons that the taskbar draws itself (the volume service's classic speaker, for one):
    /// accepted but never shown as a tray icon, as in Explorer.
    /// </summary>
    public bool IsSystemIcon => Guid is { } guid && TrayIconStore.SystemIconGuids.Contains(guid);
}

/// <summary>The tray icons in the order they were added, kept the way Explorer does.</summary>
public sealed class TrayIconStore
{
    /// <summary>
    /// The system control area icons Explorer's taskbar shows with its own buttons (<c>c_scaidToResourceMap</c> in
    /// Taskbar.dll): volume, network, power, microphone and Meet Now. The shell service objects still add some of them.
    /// </summary>
    public static readonly IReadOnlySet<Guid> SystemIconGuids = new HashSet<Guid>
    {
        new("7820ae73-23e3-4229-82c1-e41cb67d5b9c"), // Volume
        new("7820ae74-23e3-4229-82c1-e41cb67d5b9c"), // Network
        new("7820ae75-23e3-4229-82c1-e41cb67d5b9c"), // Power
        new("7820ae82-23e3-4229-82c1-e41cb67d5b9c"), // Microphone
        new("7820ae83-23e3-4229-82c1-e41cb67d5b9c"), // Meet Now
    };

    private readonly List<TrayIconState> _icons = [];

    public IReadOnlyList<TrayIconState> Icons => _icons;

    /// <summary>Applies a command; returns whether it succeeded, as <c>Shell_NotifyIcon</c> reports to the app.</summary>
    public bool Apply(NotifyIconData data)
    {
        int index = IndexOf(data);
        switch (data.Command)
        {
            case NotifyIconCommand.Add when index < 0:
                _icons.Add(Merge(new TrayIconState(data.Window, data.Id, data.Guid, 0, 0, "", false, 0, false), data));
                return true;
            case NotifyIconCommand.Modify when index >= 0:
                _icons[index] = Merge(_icons[index], data);
                return true;
            case NotifyIconCommand.Delete when index >= 0:
                _icons.RemoveAt(index);
                return true;
            case NotifyIconCommand.SetVersion when index >= 0:
                _icons[index] = _icons[index] with { Version = data.Version };
                return true;
            case NotifyIconCommand.SetFocus:
                return index >= 0;
            default:
                // Adding an icon that exists, or changing one that doesn't, fails as in Explorer.
                return false;
        }
    }

    /// <summary>Removes the icons of apps that went away without deleting them (crashed, killed).</summary>
    public bool RemoveDeadOwners(Func<nint, bool> windowExists) => _icons.RemoveAll(icon => !windowExists(icon.Window)) > 0;

    public TrayIconState? Find(nint window, uint id, Guid? guid)
    {
        int index = IndexOf(window, id, guid);
        return index >= 0 ? _icons[index] : null;
    }

    private int IndexOf(NotifyIconData data) => IndexOf(data.Window, data.Id, data.Guid);

    private int IndexOf(nint window, uint id, Guid? guid) => _icons.FindIndex(icon => guid is { } g
        ? icon.Guid == g
        : icon.Guid is null && icon.Window == window && icon.Id == id);

    // Only the fields the call's flags say are valid change.
    private static TrayIconState Merge(TrayIconState icon, NotifyIconData data)
    {
        NotifyIconFlags flags = data.Flags;
        return icon with
        {
            // An icon identified by GUID may move to another window.
            Window = data.Window != 0 ? data.Window : icon.Window,
            CallbackMessage = flags.HasFlag(NotifyIconFlags.Message) ? data.CallbackMessage : icon.CallbackMessage,
            IconHandle = flags.HasFlag(NotifyIconFlags.Icon) ? data.Icon : icon.IconHandle,
            Tip = flags.HasFlag(NotifyIconFlags.Tip) ? data.Tip : icon.Tip,
            IsHidden = data.Hidden ?? icon.IsHidden,
            ShowTip = flags.HasFlag(NotifyIconFlags.ShowTip) || (data.Command == NotifyIconCommand.Modify && icon.ShowTip),
        };
    }
}
