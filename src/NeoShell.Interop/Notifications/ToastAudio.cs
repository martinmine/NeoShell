using System.Xml.Linq;

namespace NeoShell.Interop.Notifications;

/// <summary>A toast's sound as its XML asks for it: its <c>audio</c> element and its <c>scenario</c>.</summary>
/// <param name="Source">The <c>src</c>: an <c>ms-winsoundevent:</c> sound, or a file; null for the default sound.</param>
/// <param name="Scenario">The toast's <c>scenario</c> (<c>alarm</c>, <c>incomingCall</c>, <c>reminder</c>…), or null. Only a
/// toast with a button has one: without, the notification platform treats it as an ordinary toast.</param>
public sealed record ToastAudio(string? Source, bool Loop, bool Silent, string? Scenario)
{
    /// <summary>A toast that asks for no sound, as a tray balloon with <c>NIIF_NOSOUND</c>.</summary>
    public static readonly ToastAudio None = new(null, false, true, null);

    /// <summary>Reads it from a toast's XML. Booleans are true only when "true", as the notification platform reads them.</summary>
    internal static ToastAudio Parse(XDocument toast)
    {
        XElement? root = toast.Root;
        XElement? audio = root?.Element("audio");
        string? source = (string?)audio?.Attribute("src");
        bool hasButton = root?.Element("actions")?.Elements("action").Any() == true;
        return new ToastAudio(
            string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
            IsTrue(audio?.Attribute("loop")),
            IsTrue(audio?.Attribute("silent")),
            hasButton ? (string?)root?.Attribute("scenario") : null);
    }

    /// <summary>
    /// The toast's title and body as <see cref="UserNotifications"/> reads them from the listener: the first text of
    /// its <c>ToastGeneric</c> binding (else its first binding) and the rest, one per line.
    /// </summary>
    internal static (string Title, string Body) Texts(XDocument toast)
    {
        XElement[] bindings = [.. toast.Root?.Element("visual")?.Elements("binding") ?? []];
        XElement? binding = bindings.FirstOrDefault(b => string.Equals((string?)b.Attribute("template"), "ToastGeneric", StringComparison.OrdinalIgnoreCase))
            ?? bindings.FirstOrDefault();
        string[] texts = binding is null ? [] : [.. binding.Elements("text").Select(t => t.Value).Where(t => !string.IsNullOrWhiteSpace(t))];
        return texts.Length == 0 ? ("", "") : (texts[0], string.Join("\n", texts.Skip(1)));
    }

    private static bool IsTrue(XAttribute? attribute) => string.Equals((string?)attribute, "true", StringComparison.OrdinalIgnoreCase);
}
