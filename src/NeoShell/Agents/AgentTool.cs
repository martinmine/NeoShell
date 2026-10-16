using System.Text.Json.Nodes;

namespace NeoShell.Agents;

/// <summary>What a tool lets an agent do, each allowed or not in the Agents settings.</summary>
public enum AgentToolGroup
{
    /// <summary>See windows, the tray, indicators, notifications and search.</summary>
    Read,
    /// <summary>Arrange windows, start apps, open shell surfaces, change volume and Do not disturb.</summary>
    Control,
    /// <summary>Take screenshots.</summary>
    Screen,
    /// <summary>Show the agent's own state on the taskbar and send notifications.</summary>
    Presence,
}

/// <summary>
/// A tool agents can call: its name, group, the description and argument schema they read, and what it does. The
/// handler starts on the UI thread (and may await work elsewhere); it throws <see cref="ToolArgumentException"/> for
/// arguments it can't use.
/// </summary>
public sealed record AgentTool(
    string Name, AgentToolGroup Group, string Description, JsonObject InputSchema, Func<JsonObject, Task<ToolResult>> Handler)
{
    /// <summary>A tool whose handler finishes on the UI thread.</summary>
    public AgentTool(string name, AgentToolGroup group, string description, JsonObject inputSchema, Func<JsonObject, ToolResult> handler)
        : this(name, group, description, inputSchema, arguments => Task.FromResult(handler(arguments)))
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

    public static bool? OptionalBoolean(JsonObject arguments, string name) => arguments[name] switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out bool flag) => flag,
        _ => throw new ToolArgumentException($"{name} must be true or false"),
    };

    public static bool RequiredBoolean(JsonObject arguments, string name) =>
        OptionalBoolean(arguments, name) ?? throw new ToolArgumentException($"{name} is required");

    /// <summary>One of a fixed set of names, as the schema's <c>enum</c> lists them.</summary>
    public static T Choice<T>(JsonObject arguments, string name, IReadOnlyDictionary<string, T> choices) =>
        RequiredString(arguments, name) is var value && choices.TryGetValue(value, out T? choice)
            ? choice
            : throw new ToolArgumentException($"{name} must be one of {string.Join(", ", choices.Keys)}");

    /// <summary>
    /// The window a <c>handle</c> argument names, among the windows with taskbar buttons: agents act on those only,
    /// not on NeoShell's own windows or ones they made up a handle for.
    /// </summary>
    public static T Window<T>(JsonObject arguments, IEnumerable<T> windows, Func<T, nint> handleOf)
    {
        if (arguments["handle"] is not JsonValue value || !value.TryGetValue(out long handle))
            throw new ToolArgumentException("handle is required: a window's handle from list_windows");
        foreach (T window in windows)
        {
            if (handleOf(window) == (nint)handle)
                return window;
        }
        throw new ToolArgumentException($"No window with a taskbar button has handle {handle}; list_windows gives them");
    }

    public static JsonObject BooleanSchema(string description) => new() { ["type"] = "boolean", ["description"] = description };

    public static JsonObject ChoiceSchema(string description, IEnumerable<string> choices) =>
        new() { ["type"] = "string", ["description"] = description, ["enum"] = new JsonArray([.. choices.Select(c => (JsonNode)c)]) };

    public static JsonObject HandleSchema() =>
        new() { ["type"] = "integer", ["description"] = "The window's handle, from list_windows" };

    /// <summary>A tool's arguments: their schemas by name, and which must be given.</summary>
    public static JsonObject Schema(JsonObject properties, params string[] required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Length > 0)
            schema["required"] = new JsonArray([.. required.Select(name => (JsonNode)name)]);
        return schema;
    }

    /// <summary>The schema of a string argument.</summary>
    public static JsonObject StringSchema(string description) => new() { ["type"] = "string", ["description"] = description };

    /// <summary>The schema of a whole-number argument.</summary>
    public static JsonObject IntegerSchema(string description, int min, int max) =>
        new() { ["type"] = "integer", ["description"] = description, ["minimum"] = min, ["maximum"] = max };
}
