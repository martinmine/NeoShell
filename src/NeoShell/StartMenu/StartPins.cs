using NeoShell.Settings;
using NeoShell.Taskbar;

namespace NeoShell.StartMenu;

/// <summary>
/// How Start's pins change: apps moved about, pinned and unpinned, grouped into folders and taken out again, as in
/// Explorer's Start. A folder keeps its place while it has an app left (one is enough); an emptied folder goes.
/// </summary>
public static class StartPins
{
    /// <summary>The name a folder shows: its own, or "Folder" while it has none.</summary>
    public static string DisplayName(StartFolder folder) => folder.Name.Length > 0 ? folder.Name : "Folder";

    /// <summary>Every pinned app, folders' apps included.</summary>
    public static IEnumerable<PinnedApp> Apps(IEnumerable<StartPin> pins) =>
        pins.SelectMany(pin => pin.Folder?.Apps ?? (pin.App is { } app ? [app] : []));

    public static bool Contains(IEnumerable<StartPin> pins, PinnedApp app) => Apps(pins).Any(p => TaskGrouping.SameApp(p, app));

    public static IReadOnlyList<StartPin> Pin(IReadOnlyList<StartPin> pins, PinnedApp app) => [.. pins, new StartPin(app)];

    /// <summary>The app taken out of the grid and out of any folder.</summary>
    public static IReadOnlyList<StartPin> Unpin(IReadOnlyList<StartPin> pins, PinnedApp app) =>
        [.. pins
            .Select(pin => pin.Folder is { } folder && folder.Apps.Any(p => TaskGrouping.SameApp(p, app))
                ? new StartPin(Folder: folder with { Apps = [.. folder.Apps.Where(p => !TaskGrouping.SameApp(p, app))] })
                : pin)
            .Where(pin => pin.App is { } pinned ? !TaskGrouping.SameApp(pinned, app) : pin.Folder?.Apps.Count > 0)];

    /// <summary>Explorer's pins after the ones made in NeoShell, which stay first; apps already pinned aren't added again.</summary>
    public static IReadOnlyList<StartPin> AddImported(IReadOnlyList<StartPin> pins, IEnumerable<PinnedApp> imported) =>
        [.. pins, .. imported.Where(app => !Contains(pins, app)).Select(app => new StartPin(app))];

    /// <summary>The pin at <paramref name="from"/> moved to <paramref name="to"/>, the pins between shifting along.</summary>
    public static IReadOnlyList<StartPin> Move(IReadOnlyList<StartPin> pins, int from, int to)
    {
        List<StartPin> moved = [.. pins];
        StartPin pin = moved[from];
        moved.RemoveAt(from);
        moved.Insert(to, pin);
        return moved;
    }

    /// <summary>
    /// The app at <paramref name="app"/> dropped onto the pin at <paramref name="onto"/>: added at the end of a folder,
    /// or put in a new folder with another app, that app first, where that app was.
    /// </summary>
    public static IReadOnlyList<StartPin> Group(IReadOnlyList<StartPin> pins, int app, int onto)
    {
        PinnedApp dropped = pins[app].App!;
        StartFolder folder = pins[onto].Folder is { } existing
            ? existing with { Apps = [.. existing.Apps, dropped] }
            : new StartFolder(NewId(), "", [pins[onto].App!, dropped]);
        List<StartPin> grouped = [.. pins];
        grouped[onto] = new StartPin(Folder: folder);
        grouped.RemoveAt(app);
        return grouped;
    }

    /// <summary>The app at <paramref name="index"/> put in a folder of its own, where it was ("Create a new app folder").</summary>
    public static IReadOnlyList<StartPin> NewFolder(IReadOnlyList<StartPin> pins, int index) =>
        Replace(pins, index, new StartFolder(NewId(), "", [pins[index].App!]));

    public static IReadOnlyList<StartPin> Rename(IReadOnlyList<StartPin> pins, int folder, string name) =>
        Replace(pins, folder, pins[folder].Folder! with { Name = name.Trim() });

    /// <summary>An app moved within its folder.</summary>
    public static IReadOnlyList<StartPin> MoveInFolder(IReadOnlyList<StartPin> pins, int folder, int from, int to)
    {
        List<PinnedApp> apps = [.. pins[folder].Folder!.Apps];
        PinnedApp app = apps[from];
        apps.RemoveAt(from);
        apps.Insert(to, app);
        return Replace(pins, folder, pins[folder].Folder! with { Apps = apps });
    }

    /// <summary>
    /// An app taken out of its folder and put in the grid at <paramref name="to"/>, counted with the folder still in
    /// place. The folder goes if that was its last app.
    /// </summary>
    public static IReadOnlyList<StartPin> TakeOut(IReadOnlyList<StartPin> pins, int folder, int index, int to)
    {
        StartFolder source = pins[folder].Folder!;
        List<StartPin> moved = [.. pins];
        moved[folder] = new StartPin(Folder: source with { Apps = [.. source.Apps.Where((_, i) => i != index)] });
        moved.Insert(to, new StartPin(source.Apps[index]));
        if (source.Apps.Count == 1)
            moved.RemoveAt(to <= folder ? folder + 1 : folder);
        return moved;
    }

    /// <summary>"Remove from app folder": the app goes just after its folder.</summary>
    public static IReadOnlyList<StartPin> RemoveFromFolder(IReadOnlyList<StartPin> pins, int folder, int index) =>
        TakeOut(pins, folder, index, folder + 1);

    /// <summary>Where the folder with this id is among the pins, or -1.</summary>
    public static int IndexOf(IReadOnlyList<StartPin> pins, string folderId)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            if (pins[i].Folder?.Id == folderId)
                return i;
        }
        return -1;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static IReadOnlyList<StartPin> Replace(IReadOnlyList<StartPin> pins, int index, StartFolder folder)
    {
        List<StartPin> replaced = [.. pins];
        replaced[index] = new StartPin(Folder: folder);
        return replaced;
    }
}
