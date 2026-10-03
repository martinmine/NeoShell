using System.Text.Json;
using System.Text.Json.Serialization;
using NeoShell.Logging;

namespace NeoShell.Settings;

/// <summary>Holds the current <see cref="ShellSettings"/> and loads and saves them as JSON.</summary>
public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;

    public ShellSettings Current { get; private set; } = new();

    /// <summary>Raised after <see cref="Update"/> changed <see cref="Current"/>.</summary>
    public event Action? Changed;

    /// <summary>
    /// Loads the saved settings into <see cref="Current"/>, with defaults for anything missing. A file that can't be
    /// parsed is renamed to <c>.bak</c> so the user's edits aren't lost, and defaults are used.
    /// </summary>
    public ShellSettings Load() => Current = Read();

    /// <summary>Makes <paramref name="settings"/> current and saves them.</summary>
    public void Update(ShellSettings settings)
    {
        if (settings == Current)
            return;

        Current = settings;
        try
        {
            Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not save {Path}", ex);
        }
        Changed?.Invoke();
    }

    public void Save(ShellSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.ShellSettings));
        // Replace in one step so a crash while writing never leaves a truncated settings file.
        File.Move(temp, Path, overwrite: true);
    }

    private ShellSettings Read()
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
