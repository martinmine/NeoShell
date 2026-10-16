using System.Text.Json.Nodes;

namespace NeoShell.Agents;

/// <summary>
/// A tool agents can call: its name, the description and argument schema they read, and what it does. The handler
/// starts on the UI thread (and may await work elsewhere); it throws <see cref="ToolArgumentException"/> for
/// arguments it can't use.
/// </summary>
public sealed record AgentTool(string Name, string Description, JsonObject InputSchema, Func<JsonObject, Task<ToolResult>> Handler)
{
    /// <summary>A tool whose handler finishes on the UI thread.</summary>
    public AgentTool(string name, string description, JsonObject inputSchema, Func<JsonObject, ToolResult> handler)
        : this(name, description, inputSchema, arguments => Task.FromResult(handler(arguments)))
    {
    }

    /// <summary>The schema of a tool that takes no arguments.</summary>
    public static JsonObject NoArguments() => new() { ["type"] = "object", ["properties"] = new JsonObject() };
}

/// <summary>What a tool call returns: text for the agent to read, the same as data when it is data, or an error.</summary>
public sealed record ToolResult(string Text, JsonNode? Data = null, bool IsError = false)
{
    public static ToolResult Error(string message) => new(message, IsError: true);

    /// <summary>A result that is data: given as JSON text too, for clients that don't read structured content.</summary>
    public static ToolResult FromData(JsonObject data) => new(data.ToJsonString(), data);
}

/// <summary>An argument a tool can't use; the agent gets the message as the tool's error.</summary>
public sealed class ToolArgumentException(string message) : Exception(message);

/// <summary>Reads a tool's arguments, telling the agent what's wrong with one it can't use.</summary>
public static class ToolArguments
{
    public static string RequiredString(JsonObject arguments, string name) =>
        OptionalString(arguments, name) is { Length: > 0 } value ? value : throw new ToolArgumentException($"{name} is required");

    public static string? OptionalString(JsonObject arguments, string name) => arguments[name] switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out string? text) => text,
        _ => throw new ToolArgumentException($"{name} must be a string"),
    };

    public static int Integer(JsonObject arguments, string name, int fallback, int min, int max)
    {
        if (arguments[name] is null)
            return fallback;
        if (arguments[name] is JsonValue value && value.TryGetValue(out double number) && number == Math.Floor(number)
            && number >= min && number <= max)
        {
            return (int)number;
        }
        throw new ToolArgumentException($"{name} must be a whole number from {min} to {max}");
    }

    /// <summary>The schema of a string argument.</summary>
    public static JsonObject StringSchema(string description) => new() { ["type"] = "string", ["description"] = description };

    /// <summary>The schema of a whole-number argument.</summary>
    public static JsonObject IntegerSchema(string description, int min, int max) =>
        new() { ["type"] = "integer", ["description"] = description, ["minimum"] = min, ["maximum"] = max };
}
