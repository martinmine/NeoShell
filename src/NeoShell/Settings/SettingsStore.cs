using System.Text.Json;
using System.Text.Json.Serialization;
using NeoShell.Logging;

namespace NeoShell.Settings;

/// <summary>Loads and saves <see cref="ShellSettings"/> as JSON.</summary>
public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;

    /// <summary>
    /// Returns the saved settings, with defaults for anything missing. A file that can't be parsed is renamed
    /// to <c>.bak</c> so the user's edits aren't lost, and defaults are used.
    /// </summary>
    public ShellSettings Load()
    {
        string json;
        try
        {
            json = File.ReadAllText(Path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new ShellSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not read {Path}; using default settings", ex);
            return new ShellSettings();
        }

        try
        {
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.ShellSettings)
                ?? throw new JsonException("The settings file contains null.");
        }
        catch (JsonException ex)
        {
            string backup = Path + ".bak";
            Log.Warn($"{Path} is not valid; moved it to {backup} and using default settings", ex);
            File.Move(Path, backup, overwrite: true);
            return new ShellSettings();
        }
    }

    public void Save(ShellSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.ShellSettings));
        // Replace in one step so a crash while writing never leaves a truncated settings file.
        File.Move(temp, Path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ShellSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
