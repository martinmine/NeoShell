using NeoShell.Interop.Com;

namespace NeoShell.Interop.Shell;

/// <summary>An app pinned to Explorer's Start menu or taskbar.</summary>
/// <param name="AppUserModelId">A packaged app's AppUserModelID, or the one a pinned shortcut gives its app.</param>
/// <param name="TargetPath">For a pinned shortcut, the file it points to.</param>
public sealed record ExplorerPin(string? AppUserModelId, string? TargetPath)
{
    internal static ExplorerPin FromItem(IShellItem? item) =>
        item is null
            ? new ExplorerPin(null, null)
            : new ExplorerPin(ShellItems.GetString(item, ShellItems.AppUserModelIdKey), ShellItems.GetString(item, ShellItems.LinkTargetPathKey));
}
