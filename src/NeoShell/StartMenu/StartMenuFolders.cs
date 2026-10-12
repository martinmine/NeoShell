namespace NeoShell.StartMenu;

/// <summary>An item of the Start Menu folders: a shortcut (or other file) to open, or a folder of them.</summary>
/// <param name="Path">The file to open; null for a folder.</param>
public sealed record StartMenuEntry(string Name, string? Path, IReadOnlyList<StartMenuEntry> Children)
{
    public bool IsFolder => Path is null;
}

/// <summary>
/// The Start Menu folders as XP's All Programs showed them: the user's and all users' folders merged (a folder in both
/// is one folder with the items of both), folders before shortcuts, each by name, file extensions hidden.
/// </summary>
public static class StartMenuFolders
{
    /// <summary>
    /// What's at the top of All Programs (the Start Menu folders' own shortcuts, shown above a line) and the Programs
    /// folders' tree.
    /// </summary>
    public static (IReadOnlyList<StartMenuEntry> Top, IReadOnlyList<StartMenuEntry> Programs) Load()
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        ];
        IReadOnlyList<StartMenuEntry> top = [.. Read(roots, includeFolders: false)];
        IReadOnlyList<StartMenuEntry> programs = Read([.. roots.Select(root => Path.Combine(root, "Programs"))], includeFolders: true);
        return (top, programs);
    }

    /// <summary>The merged contents of folders that are the same folder for different users.</summary>
    public static IReadOnlyList<StartMenuEntry> Read(IReadOnlyList<string> folders, bool includeFolders = true)
    {
        var subfolders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders.Where(Directory.Exists))
        {
            try
            {
                var directory = new DirectoryInfo(folder);
                foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
                {
                    if ((item.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        continue;
                    if (item is DirectoryInfo)
                    {
                        if (!includeFolders)
                            continue;
                        if (!subfolders.TryGetValue(item.Name, out List<string>? same))
                            subfolders[item.Name] = same = [];
                        same.Add(item.FullName);
                    }
                    else
                    {
                        files.TryAdd(DisplayName(item.Name), item.FullName);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that can't be read shows what the others have.
            }
        }

        StringComparer byName = StringComparer.CurrentCultureIgnoreCase;
        return
        [
            .. subfolders.OrderBy(folder => folder.Key, byName).Select(folder => new StartMenuEntry(folder.Key, null, Read(folder.Value))),
            .. files.OrderBy(file => file.Key, byName).Select(file => new StartMenuEntry(file.Key, file.Value, [])),
        ];
    }

    /// <summary>A file's name as the Start Menu shows it: shortcuts and Internet shortcuts without their extension.</summary>
    public static string DisplayName(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
    }
}
