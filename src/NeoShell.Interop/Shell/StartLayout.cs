using System.Runtime.InteropServices;
using System.Text.Json;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>Reads the apps pinned to Explorer's Start menu.</summary>
public static class StartLayout
{
    private static readonly Guid CLSID_StartLayoutCmdlet = new("75AB852C-0441-46D4-A205-EF0A33F98255");

    /// <summary>
    /// The pins in Start's order, or throws. Start keeps them encrypted (<c>start2.bin</c>), so they're read through
    /// the export behind <c>Export-StartLayout</c>, which writes them to a JSON file. Call it off the UI thread.
    /// </summary>
    public static IReadOnlyList<ExplorerPin> ReadPinned()
    {
        string path = Path.Combine(Path.GetTempPath(), $"NeoShell-StartLayout-{Environment.ProcessId}.json");
        try
        {
            var exporter = Ole32.Create<IStartLayoutCmdlet>(CLSID_StartLayoutCmdlet, Ole32.CLSCTX_INPROC_SERVER);
            Marshal.ThrowExceptionForHR(exporter.ExportStartLayout(path));
            return [.. Parse(File.ReadAllText(path)).Select(pin => pin.LinkPath is { } link ? ExplorerPin.FromItem(ShellItems.Create(link)) : new ExplorerPin(pin.PackagedAppId, null))];
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The pins in an exported layout: <c>{"pinnedList":[{"packagedAppId":"…"},{"desktopAppLink":"%APPDATA%\…\x.lnk"}]}</c>.
    /// A shortcut comes back as its path, environment variables expanded.
    /// </summary>
    internal static IEnumerable<(string? PackagedAppId, string? LinkPath)> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("pinnedList", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (JsonElement entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            if (entry.TryGetProperty("packagedAppId", out JsonElement appId) && appId.GetString() is { Length: > 0 } id)
                yield return (id, null);
            else if (entry.TryGetProperty("desktopAppLink", out JsonElement link) && link.GetString() is { Length: > 0 } linkPath)
                yield return (null, Environment.ExpandEnvironmentVariables(linkPath));
        }
    }

}
