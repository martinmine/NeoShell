using Microsoft.Win32;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;
using NeoShell.Interop.Windowing;

namespace NeoShell.Interop.Shell;

/// <summary>
/// Signs of what a volume holds, found the way Windows' Shell Hardware Detection service looks for them
/// (<c>shsvcs!CVolume::_UpdateSpecialFilePresence</c>): the folders and files of DVD, Video CD and Blu-ray discs and of
/// cameras, and the tracks of audio CDs.
/// </summary>
[Flags]
public enum VolumeMedia
{
    None = 0,
    AudioTracks = 0x1,
    DvdVideo = 0x2,
    DvdAudio = 0x4,
    VideoCD = 0x8,
    SuperVideoCD = 0x10,
    BluRay = 0x20,
    /// <summary>A camera's memory card or a camcorder: <c>DCIM</c>, <c>AVCHD</c>.</summary>
    Camera = 0x40,
}

/// <summary>Media files AutoPlay looks for, by their perceived type.</summary>
public enum MediaKind
{
    Music,
    Pictures,
    Videos,
}

/// <summary>What a drive's <c>autorun.inf</c> says, with paths made full ones on the drive.</summary>
/// <param name="Command">The program to run (<c>open=</c>, or <c>shellexecute=</c> when <paramref name="ShellExecute"/>).</param>
/// <param name="Action">The text AutoPlay shows for running it (<c>action=</c>).</param>
public sealed record AutorunInf(string? Command, bool ShellExecute, string? Action, string? Icon, string? Label);

/// <summary>A volume that arrived, as AutoPlay needs to know it.</summary>
/// <param name="Root">The drive's root, <c>F:\</c>.</param>
/// <param name="DisplayName">Its name in File Explorer, "DVD Drive (F:) PHOTOS".</param>
/// <param name="Autorun">Its <c>autorun.inf</c>; commands only on optical drives, as Windows runs no others.</param>
public sealed record AutoPlayVolume(
    string Root, string DisplayName, DriveType Type, VolumeMedia Media, AutorunInf? Autorun, string Label, uint SerialNumber);

/// <summary>
/// Volumes for NeoShell's AutoPlay as the shell (Windows' own runs only inside Explorer): what a volume holds, and
/// whether Windows' settings, policies or the app in front let AutoPlay run. Safe to call from a background thread.
/// </summary>
public static unsafe class AutoPlayVolumes
{
    private const string PoliciesKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private static readonly Guid CLSID_QueryCancelAutoPlay = new("331f1768-05a9-4ddd-b86e-dae34ddc998a");

    // shsvcs' list. DVD, Video CD and Blu-ray markers count on optical drives only.
    private static readonly (string Path, VolumeMedia Media)[] s_markers =
    [
        (@"video_ts\video_ts.ifo", VolumeMedia.DvdVideo),
        (@"dvd_rtav\vr_mangr.ifo", VolumeMedia.DvdVideo),
        (@"audio_ts\audio_ts.ifo", VolumeMedia.DvdAudio),
        (@"VCD\entries.vcd", VolumeMedia.VideoCD),
        (@"SVCD\entries.svd", VolumeMedia.SuperVideoCD),
        (@"SVCD\entries.vcd", VolumeMedia.SuperVideoCD),
        ("DCIM", VolumeMedia.Camera),
        ("AVCHD", VolumeMedia.Camera),
        (@"PRIVATE\AVCHD", VolumeMedia.Camera),
        ("BDMV", VolumeMedia.BluRay),
        ("BDAV", VolumeMedia.BluRay),
    ];

    /// <summary>The volume at <paramref name="root"/>; null when it has no readable media (an empty drive).</summary>
    public static AutoPlayVolume? Inspect(string root)
    {
        var drive = new DriveInfo(root);
        if (!drive.IsReady)
            return null;

        bool optical = drive.DriveType == DriveType.CDRom;
        VolumeMedia media = VolumeMedia.None;
        foreach ((string path, VolumeMedia marker) in s_markers)
        {
            if (Path.Exists(Path.Combine(root, path)))
                media |= marker;
        }
        if (!optical)
            media &= VolumeMedia.Camera;
        // Only UDF Blu-ray discs are movies; shsvcs also checks the UDF revision, which needs the device.
        if (drive.DriveFormat != "UDF")
            media &= ~VolumeMedia.BluRay;
        // Audio CDs show their tracks as .cda files.
        if (optical && Directory.EnumerateFiles(root, "*.cda").Any())
            media |= VolumeMedia.AudioTracks;

        string autorunPath = Path.Combine(root, "autorun.inf");
        AutorunInf? autorun = null;
        // As shell32's IsPathSafeForAutoplay: not through a reparse point.
        if (File.Exists(autorunPath) && !File.GetAttributes(autorunPath).HasFlag(FileAttributes.ReparsePoint))
            autorun = ParseAutorun(File.ReadAllLines(autorunPath), root, optical);

        uint serial = 0;
        Kernel32.GetVolumeInformation(root, null, 0, &serial, null, null, null, 0);
        string name = DisplayName(ShellItems.GetDisplayName(root) ?? root, optical ? OpticalDrives.TypeName(root) : null, drive.VolumeLabel, autorun?.Label);
        return new AutoPlayVolume(root, name, drive.DriveType, media, autorun, drive.VolumeLabel, serial);
    }

    /// <summary>
    /// The drive's name as Explorer's own process shows it, from the shell's name in any other process: the optical
    /// drive's real type for "CD Drive", and <c>autorun.inf</c>'s label for the volume's.
    /// </summary>
    internal static string DisplayName(string shellName, string? typeName, string volumeLabel, string? autorunLabel)
    {
        string? cdDrive = typeName is null ? null : AutoPlayHandlers.Text($@"@%SystemRoot%\system32\windows.storage.dll,-{OpticalDrives.CdDrive}");
        int at = cdDrive is null ? -1 : shellName.IndexOf(cdDrive, StringComparison.Ordinal);
        if (at >= 0)
            shellName = shellName[..at] + typeName + shellName[(at + cdDrive!.Length)..];
        if (autorunLabel is not null)
        {
            if (volumeLabel.Length == 0)
                shellName += " " + autorunLabel;
            else if (shellName.EndsWith(volumeLabel, StringComparison.Ordinal))
                shellName = shellName[..^volumeLabel.Length] + autorunLabel;
        }
        return shellName;
    }

    /// <summary>
    /// Reads <c>autorun.inf</c> as shell32's <c>CMountPoint::_ProcessAutoRunFile</c>: the <c>[AutoRun.Amd64]</c> section
    /// if it has any keys, else <c>[AutoRun]</c>. Commands and actions only count on optical drives.
    /// </summary>
    internal static AutorunInf? ParseAutorun(IEnumerable<string> lines, string root, bool optical)
    {
        Dictionary<string, Dictionary<string, string>> sections = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? section = null;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string name = line[1..^1].Trim();
                if (!sections.TryGetValue(name, out section))
                    sections[name] = section = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            int equals = line.IndexOf('=');
            if (section is null || equals <= 0 || line.StartsWith(';'))
                continue;
            section.TryAdd(line[..equals].Trim(), line[(equals + 1)..].Trim());
        }

        if (!sections.TryGetValue("AutoRun.Amd64", out section) || section.Count == 0)
        {
            if (!sections.TryGetValue("AutoRun", out section))
                return null;
        }

        string? Value(string key) => section.TryGetValue(key, out string? value) && value.Length > 0 ? value : null;
        string? command = null;
        bool shellExecute = false;
        string? action = null;
        if (optical)
        {
            if (Value("open") is { } open)
            {
                command = QualifyToDrive(open, root);
            }
            else if (Value("shellexecute") is { } execute)
            {
                command = QualifyToDrive(execute, root);
                shellExecute = true;
            }
            // "@res.dll,-101" is a resource in a file on the drive.
            action = Value("action") is { } text && text.StartsWith('@') ? "@" + root + text[1..].TrimStart('\\') : Value("action");
        }
        string? icon = Value("icon") is { } iconValue ? Path.Combine(root, iconValue.TrimStart('\\')) : null;
        return new AutorunInf(command, shellExecute, action, icon, Value("label"));
    }

    // A relative program name is on the drive; its arguments stay as they are.
    private static string QualifyToDrive(string command, string root)
    {
        (string program, string arguments) = SplitCommand(command);
        if (!Path.IsPathRooted(program))
            program = Path.Combine(root, program.TrimStart('\\'));
        return arguments.Length == 0 ? program : $"\"{program}\" {arguments}";
    }

    /// <summary>A command line's program (unquoted) and arguments.</summary>
    public static (string Program, string Arguments) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int close = command.IndexOf('"', 1);
            return close < 0 ? (command[1..], "") : (command[1..close], command[(close + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }

    /// <summary>
    /// The kinds of media files on the volume, by perceived type, in the order they turn up walking it as
    /// Explorer's namespace walker does (each folder in its own order, into subfolders as met, four levels deep):
    /// what Explorer looks for when the user chose "Choose what to do with each type of media" for removable drives.
    /// The order matters: Explorer adds each kind's choices to the flyout as it finds them.
    /// </summary>
    public static IReadOnlyList<MediaKind> Sniff(string root, CancellationToken cancel)
    {
        List<MediaKind> found = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };

        void Walk(string folder, int depth)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder, "*", options))
            {
                cancel.ThrowIfCancellationRequested();
                if (found.Count == 3)
                    return;
                if (Directory.Exists(entry))
                {
                    if (depth < 3)
                        Walk(entry, depth + 1);
                    continue;
                }
                string extension = Path.GetExtension(entry);
                if (extension.Length == 0 || !seen.Add(extension) || Shlwapi.AssocGetPerceivedType(extension, out int type, out _, 0) != 0)
                    continue;
                MediaKind? kind = type switch
                {
                    Shlwapi.PERCEIVED_TYPE_AUDIO => MediaKind.Music,
                    Shlwapi.PERCEIVED_TYPE_IMAGE => MediaKind.Pictures,
                    Shlwapi.PERCEIVED_TYPE_VIDEO => MediaKind.Videos,
                    _ => null,
                };
                if (kind is { } next && !found.Contains(next))
                    found.Add(next);
            }
        }

        Walk(root, 0);
        return found;
    }

    /// <summary>"Use AutoPlay for all media and devices" is off (Settings → Bluetooth &amp; devices → AutoPlay).</summary>
    public static bool IsTurnedOff()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers");
        return key?.GetValue("DisableAutoplay") is int value && value == 1;
    }

    /// <summary>
    /// Group policy stops AutoPlay on this drive (<c>NoDriveTypeAutoRun</c>, <c>NoDriveAutoRun</c>, <c>NoDrives</c>), as
    /// shell32's <c>_IsAutoRunDriveAndEnabledByPolicy</c>. Network drives never play.
    /// </summary>
    public static bool IsBlockedByPolicy(string root, DriveType type)
    {
        if (type is DriveType.Network or DriveType.Unknown or DriveType.NoRootDirectory)
            return true;
        uint letter = 1u << (char.ToUpperInvariant(root[0]) - 'A');
        return (Policy("NoDriveAutoRun") & letter) != 0 || (Policy("NoDrives") & letter) != 0
            || (Policy("NoDriveTypeAutoRun") & (1u << (int)type)) != 0;
    }

    /// <summary>The "Default behavior for AutoRun" policy: 1 never runs autorun.inf commands, 2 runs them without asking.</summary>
    public static uint AutorunPolicy() => Policy("NoAutorun");

    // As SHRestricted: the machine's policy, else the user's.
    private static uint Policy(string name)
    {
        foreach (RegistryKey hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using RegistryKey? key = hive.OpenSubKey(PoliciesKey);
            if (key?.GetValue(name) is int value)
                return (uint)value;
        }
        return 0;
    }

    /// <summary>A full-screen game is in front: Explorer plays nothing then.</summary>
    public static bool IsGameRunning() =>
        Shell32.SHQueryUserNotificationState(out int state) == 0 && state == Shell32.QUNS_RUNNING_D3D_FULL_SCREEN;

    /// <summary>
    /// Whether an app stops AutoPlay for this media, as Explorer asks: the window in front through the registered
    /// "QueryCancelAutoPlay" message, then any <c>IQueryCancelAutoPlay</c> in the running object table.
    /// </summary>
    /// <param name="contentType">The media's <c>ARCONTENT_*</c> flags.</param>
    public static bool AppCancels(AutoPlayVolume volume, uint contentType)
    {
        uint message = WindowMessages.Register("QueryCancelAutoPlay");
        User32.SendMessageTimeout(
            User32.GetForegroundWindow(), message, char.ToUpperInvariant(volume.Root[0]) - 'A', (nint)contentType,
            User32.SMTO_ABORTIFHUNG, 1000, out nint result);
        if (result != 0)
            return true;

        if (Ole32.GetRunningObjectTable(0, out nint tablePointer) != 0)
            return false;
        nint moniker = 0;
        try
        {
            var table = ComPointer.TakeOwnership<IRunningObjectTable>(tablePointer);
            if (Ole32.CreateClassMoniker(CLSID_QueryCancelAutoPlay, out moniker) != 0 || table.GetObject(moniker, out nint unknown) != 0)
                return false;
            var query = ComPointer.TakeOwnership<IQueryCancelAutoPlay>(unknown);
            // S_FALSE cancels.
            return query.AllowAutoPlay(volume.Root, contentType, volume.Label, volume.SerialNumber) == 1;
        }
        catch (Exception ex) when (ex is InvalidCastException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
        finally
        {
            if (moniker != 0)
                System.Runtime.InteropServices.Marshal.Release(moniker);
        }
    }
}
