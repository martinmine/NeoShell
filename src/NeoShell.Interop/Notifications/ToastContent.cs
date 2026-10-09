using System.Globalization;
using System.Xml.Linq;

namespace NeoShell.Interop.Notifications;

/// <summary>
/// What a toast shows besides its texts, read from its XML as the notification platform reads it
/// (NotificationController.dll's <c>NotificationParser</c>, Windows 11 25H2): images, attribution, header, progress
/// bar, inputs and buttons.
/// </summary>
/// <param name="Title">The first text, as <see cref="ToastAudio.Texts"/>.</param>
/// <param name="Body">The other texts, one per line; not the attribution.</param>
/// <param name="Scenario">The toast's <c>scenario</c> (<c>reminder</c>, <c>alarm</c>, <c>incomingCall</c>,
/// <c>urgent</c>), or null.</param>
/// <param name="UseButtonStyle">The toast's <c>useButtonStyle</c>: buttons take their <c>hint-buttonStyle</c> colours
/// and show their icons beside the text.</param>
/// <param name="Long"><c>duration="long"</c>.</param>
/// <param name="Logo">The <c>appLogoOverride</c> image beside the texts.</param>
/// <param name="Hero">The <c>hero</c> image across the top.</param>
/// <param name="Images">Inline images under the texts.</param>
/// <param name="Header">The <c>header</c> element's title.</param>
public sealed record ToastContent(
    string Title,
    string Body,
    string? Scenario,
    bool UseButtonStyle,
    bool Long,
    ToastImage? Logo,
    ToastImage? Hero,
    IReadOnlyList<ToastImage> Images,
    string? Attribution,
    string? Header,
    ToastProgress? Progress,
    IReadOnlyList<ToastInput> Inputs,
    IReadOnlyList<ToastAction> Actions)
{
    /// <summary>Buttons under the texts, not the context menu's nor those beside a text box.</summary>
    public IEnumerable<ToastAction> Buttons => Actions.Where(a => !a.InContextMenu && Inputs.All(i => !IsBesideTextBox(a, i)));

    public IEnumerable<ToastAction> ContextMenu => Actions.Where(a => a.InContextMenu);

    /// <summary>The button beside a text box (its <c>hint-inputId</c> names it), if any: a reply's send button.</summary>
    public ToastAction? ButtonBeside(ToastInput input) => Actions.FirstOrDefault(a => IsBesideTextBox(a, input));

    /// <summary>
    /// Whether it stays on screen until the user acts on it: reminders, alarms and incoming calls with a button do
    /// (Explorer's "priority" toasts); without one they're ordinary toasts.
    /// </summary>
    public bool StaysUntilDismissed =>
        Scenario is { } scenario
        && (Is(scenario, "reminder") || Is(scenario, "alarm") || Is(scenario, "incomingCall"))
        && Actions.Any(a => !a.InContextMenu);

    public bool IsIncomingCall => Scenario is { } scenario && Is(scenario, "incomingCall");

    public bool IsUrgent => Scenario is { } scenario && Is(scenario, "urgent");

    /// <summary>
    /// Reads it from the toast's XML. Image sources are found with <paramref name="locateImage"/> (null: not shown);
    /// <paramref name="data"/> fills in data-bound values (<c>{progressValue}</c>), as the toast's
    /// <c>NotificationData</c> does.
    /// </summary>
    internal static ToastContent Parse(XDocument toast, Func<string, string?> locateImage, IReadOnlyDictionary<string, string>? data = null)
    {
        XElement root = toast.Root ?? new XElement("toast");
        XElement[] bindings = [.. root.Element("visual")?.Elements("binding") ?? []];
        XElement? binding = bindings.FirstOrDefault(b => Is((string?)b.Attribute("template"), "ToastGeneric")) ?? bindings.FirstOrDefault();
        XElement[] elements = [.. binding?.Elements() ?? []];
        XElement[] texts = [.. elements.Where(e => e.Name == "text")];
        XElement[] images = [.. elements.Where(e => e.Name == "image")];
        string[] lines = [.. texts.Where(e => !IsPlacement(e, "attribution")).Select(e => e.Value).Where(t => !string.IsNullOrWhiteSpace(t))];

        ToastImage? Image(XElement element) =>
            (string?)element.Attribute("src") is { Length: > 0 } source && locateImage(source.Trim()) is { } uri
                ? new ToastImage(uri, Is((string?)element.Attribute("hint-crop"), "circle"))
                : null;
        ToastImage? Placed(string placement) => images.Where(e => IsPlacement(e, placement)).Select(Image).FirstOrDefault(i => i is not null);

        XElement? actions = root.Element("actions");
        ToastInput[] inputs = [.. actions?.Elements("input").Select(Input) ?? []];
        return new ToastContent(
            lines.FirstOrDefault() ?? "",
            string.Join("\n", lines.Skip(1)),
            NullIfEmpty((string?)root.Attribute("scenario")),
            IsTrue(root.Attribute("useButtonStyle")),
            Is((string?)root.Attribute("duration"), "long"),
            Placed("appLogoOverride"),
            Placed("hero"),
            [.. images.Where(e => IsPlacement(e, null)).Select(Image).OfType<ToastImage>()],
            NullIfEmpty(texts.FirstOrDefault(e => IsPlacement(e, "attribution"))?.Value),
            NullIfEmpty((string?)root.Element("header")?.Attribute("title")),
            elements.FirstOrDefault(e => e.Name == "progress") is { } progress ? ReadProgress(progress, data) : null,
            inputs,
            ReadActions(actions?.Elements("action") ?? [], inputs, locateImage));
    }

    // Each action gets the ID the controller knows it by (ActivateNotification's invoke ID): a button beside a text
    // box goes by its input's ID, the others by "<" and their place among them (context menu items too), as
    // Explorer's toasts send them.
    private static ToastAction[] ReadActions(IEnumerable<XElement> elements, ToastInput[] inputs, Func<string, string?> locateImage)
    {
        var actions = new List<ToastAction>();
        int index = 0;
        foreach (XElement element in elements)
        {
            string arguments = (string?)element.Attribute("arguments") ?? "";
            string type = (string?)element.Attribute("activationType") ?? "foreground";
            string? inputId = NullIfEmpty((string?)element.Attribute("hint-inputId"));
            bool system = Is(type, "system");
            bool besideTextBox = !system && inputs.Any(i => i.IsText && i.Id == inputId);
            string content = (string?)element.Attribute("content") ?? "";
            // System actions without their own text get Windows' words for them.
            if (system && content.Length == 0)
                content = Is(arguments, "snooze") ? "Snooze" : Is(arguments, "dismiss") ? "Dismiss" : "";
            actions.Add(new ToastAction(
                besideTextBox ? inputId! : "<" + (index++).ToString(CultureInfo.InvariantCulture),
                content,
                arguments,
                type,
                IsPlacement(element, "contextMenu"),
                NullIfEmpty((string?)element.Attribute("imageUri")) is { } image ? locateImage(image.Trim()) : null,
                inputId,
                NullIfEmpty((string?)element.Attribute("hint-buttonStyle"))));
        }
        return [.. actions];
    }

    /// <summary>
    /// Where an image source is, as a URI the picture can be loaded from, or null for one the notification platform
    /// doesn't show: <c>ms-appx:///</c> and <c>ms-appdata:///local/</c> in the app's package (a scale variant of the
    /// file will do), <c>file:///</c> and full paths, and web addresses only for a packaged app allowed on the internet.
    /// </summary>
    internal static string? LocateImage(string source, string? packageFolder, string? packageData, bool allowsWeb)
    {
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return allowsWeb && Uri.IsWellFormedUriString(source, UriKind.Absolute) ? source : null;

        string? path = null;
        if (Relative(source, "ms-appx:///") is { } inPackage)
            path = packageFolder is null ? null : Path.Combine(packageFolder, inPackage);
        else if (Relative(source, "ms-appdata:///local/") is { } inData)
            path = packageData is null ? null : Path.Combine(packageData, inData);
        else if (source.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(source, UriKind.Absolute, out Uri? file))
            path = file.LocalPath;
        else if (Path.IsPathFullyQualified(source))
            path = source;
        return path is not null && (File.Exists(path) ? path : ScaledVariant(path)) is { } found ? new Uri(found).AbsoluteUri : null;
    }

    // A package's "Assets/Logo.png" is often only there as "Logo.scale-200.png" and the like: the largest scale will do.
    private static string? ScaledVariant(string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder))
            return null;
        string pattern = Path.GetFileNameWithoutExtension(path) + ".*" + Path.GetExtension(path);
        return Directory.EnumerateFiles(folder, pattern).OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static string? Relative(string source, string prefix) =>
        source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? Uri.UnescapeDataString(source[prefix.Length..]).Replace('/', Path.DirectorySeparatorChar)
            : null;

    private static ToastInput Input(XElement element) => new(
        (string?)element.Attribute("id") ?? "",
        !Is((string?)element.Attribute("type"), "selection"),
        NullIfEmpty((string?)element.Attribute("title")),
        NullIfEmpty((string?)element.Attribute("placeHolderContent")),
        NullIfEmpty((string?)element.Attribute("defaultInput")),
        [.. element.Elements("selection").Select(s => new ToastChoice((string?)s.Attribute("id") ?? "", (string?)s.Attribute("content") ?? ""))]);

    private static ToastProgress ReadProgress(XElement element, IReadOnlyDictionary<string, string>? data)
    {
        string? Read(string name)
        {
            string? value = (string?)element.Attribute(name);
            // "{key}" is filled in from the toast's data.
            if (value is ['{', .. var key, '}'])
                value = data?.GetValueOrDefault(key);
            return NullIfEmpty(value);
        }

        string? text = Read("value");
        double? value = text is not null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? Math.Clamp(v, 0, 1)
            : null;
        return new ToastProgress(Read("title"), value, Read("valueStringOverride"), Read("status") ?? "");
    }

    private static bool IsPlacement(XElement element, string? placement)
    {
        string? value = NullIfEmpty((string?)element.Attribute("placement"));
        return placement is null ? value is null : Is(value, placement);
    }

    private static bool Is(string? value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsTrue(XAttribute? attribute) => Is((string?)attribute, "true");

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool IsBesideTextBox(ToastAction action, ToastInput input) =>
        input.IsText && action.InputId == input.Id && !Is(action.ActivationType, "system") && !action.InContextMenu;
}

/// <param name="Uri">Where the picture is: a file or web URI.</param>
/// <param name="Circle"><c>hint-crop="circle"</c>.</param>
public sealed record ToastImage(string Uri, bool Circle);

/// <param name="Value">0 to 1; null while indeterminate.</param>
/// <param name="ValueText">Shown in place of the percentage.</param>
public sealed record ToastProgress(string? Title, double? Value, string? ValueText, string Status);

/// <param name="IsText">A text box; else a selection box.</param>
/// <param name="Default">The text box's text, or the ID of the choice selected at first.</param>
public sealed record ToastInput(string Id, bool IsText, string? Title, string? Placeholder, string? Default, IReadOnlyList<ToastChoice> Choices);

public sealed record ToastChoice(string Id, string Content);

/// <param name="InvokeId">What the notification platform's controller knows it by.</param>
/// <param name="ActivationType"><c>foreground</c>, <c>background</c>, <c>protocol</c> or <c>system</c>.</param>
/// <param name="InContextMenu"><c>placement="contextMenu"</c>: an item of the toast's menu, not a button.</param>
/// <param name="ImageUri">The button's icon: a file or web URI.</param>
/// <param name="InputId"><c>hint-inputId</c>.</param>
/// <param name="Style"><c>hint-buttonStyle</c>: <c>Success</c> or <c>Critical</c>.</param>
public sealed record ToastAction(
    string InvokeId, string Content, string Arguments, string ActivationType, bool InContextMenu, string? ImageUri, string? InputId, string? Style);
