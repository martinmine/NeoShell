using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoShell.Frames;

/// <summary>
/// The part of NeoShell's settings.json this program goes by: NeoShell's Settings window writes it, and tells this
/// program to read it again (<see cref="FrameProgram.NotifySettingsChanged"/>).
/// </summary>
internal sealed record HostSettings
{
    public bool WindowFramesEnabled { get; init; }
    public string WindowFrameStyle { get; init; } = FramePresets.WindowsDefault;
    public IReadOnlyList<FrameStyle> WindowFrameStyles { get; init; } = [];
    public IReadOnlyList<FrameRule> WindowFrameRules { get; init; } = [];

    /// <summary>The settings; defaults (nothing drawn) when there are none or they're not valid; null when the file can't be read now.</summary>
    public static HostSettings? Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), HostSettingsJson.Default.HostSettings) ?? new HostSettings();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new HostSettings();
        }
        catch (JsonException ex)
        {
            FrameLog.Error($"{path} is not valid; drawing no frames", ex);
            return new HostSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // NeoShell is replacing it.
            return null;
        }
    }

    /// <summary>Whether windows of <paramref name="processName"/> and <paramref name="className"/> get the Luna frame.</summary>
    public bool DrawsLuna(string? processName, string className) =>
        WindowFramesEnabled
        && FramePresets.Find(FrameRules.StyleFor(WindowFrameRules, WindowFrameStyle, processName, className), WindowFrameStyles)
            ?.Overlay == FrameOverlay.Luna;
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(HostSettings))]
internal sealed partial class HostSettingsJson : JsonSerializerContext;
