using NeoShell.Interop.Shell;

namespace NeoShell.AutoPlay;

/// <summary>
/// The kinds of content AutoPlay tells apart, with shell32's own values (its <c>CT_*</c> content types), so they can
/// be compared with Explorer's traces and registry.
/// </summary>
[Flags]
public enum AutoPlayContent
{
    None = 0,
    /// <summary>"Software and games": an <c>autorun.inf</c> with a program, on a disc.</summary>
    Autorun = 0x1,
    AudioCD = 0x2,
    DvdMovie = 0x4,
    DvdAudio = 0x8,
    BlankCD = 0x10,
    BlankDvd = 0x20,
    VideoCD = 0x40,
    SuperVideoCD = 0x80,
    Mixed = 0x100,
    Music = 0x200,
    Pictures = 0x400,
    Videos = 0x800,
    BluRay = 0x2000,
    BlankBluRay = 0x4000,
    Unknown = 0x8000,
    /// <summary>A removable drive, whatever is on it: Windows' default for drives.</summary>
    Storage = 0x10000,
    MemoryCard = 0x20000,
}

/// <summary>An AutoPlay event: its registry name, the text naming its content, and where its choices are saved.</summary>
/// <param name="Description">shell32's text for the content, "removable drives", in "Select what happens with …".</param>
/// <param name="ChoiceGroup">The subkey its saved choices sit under, when not the event's own.</param>
public sealed record AutoPlayEvent(AutoPlayContent Content, string Name, int Description, string? ChoiceGroup = null);

/// <summary>
/// How Explorer decides what AutoPlay does with media, from shell32 (<c>CAutoPlayParams</c>,
/// <c>CAutoplayContentHandler</c>) and twinui (<c>CAutoplayDialog</c>), Windows 11 25H2.
/// </summary>
public static class AutoPlayRules
{
    public const string TakeNoAction = "MSTakeNoAction";
    public const string PromptEachTime = "MSPromptEachTime";
    public const string OpenFolder = "MSOpenFolder";
    public const string AutoRun = "MSAutoRun";
    public const string UseAdvancedStorageOptions = "MSUseAdvancedStorageOptions";

    // shell32's table (content type, event, description string): the descriptions are shell32.dll strings.
    private static readonly AutoPlayEvent[] s_events =
    [
        new(AutoPlayContent.Autorun, "AutorunINFLegacyArrival", 17472),
        new(AutoPlayContent.AudioCD, "PlayCDAudioOnArrival", 17473),
        new(AutoPlayContent.DvdMovie, "PlayDVDMovieOnArrival", 17474),
        new(AutoPlayContent.DvdAudio, "PlayDVDAudioOnArrival", 17475),
        new(AutoPlayContent.BlankCD, "HandleCDBurningOnArrival", 17476),
        new(AutoPlayContent.BlankDvd, "HandleDVDBurningOnArrival", 17477),
        new(AutoPlayContent.BlankBluRay, "HandleBDBurningOnArrival", 17488),
        new(AutoPlayContent.VideoCD, "PlayVideoCDMovieOnArrival", 17478),
        new(AutoPlayContent.SuperVideoCD, "PlaySuperVideoCDMovieOnArrival", 17479),
        new(AutoPlayContent.Mixed, "MixedContentOnArrival", 17481),
        new(AutoPlayContent.Music, "PlayMusicFilesOnArrival", 17482),
        new(AutoPlayContent.Pictures, "ShowPicturesOnArrival", 17483),
        new(AutoPlayContent.Videos, "PlayVideoFilesOnArrival", 17484),
        new(AutoPlayContent.Unknown, "UnknownContentOnArrival", 17480),
        new(AutoPlayContent.Autorun | AutoPlayContent.AudioCD, "PlayEnhancedCDOnArrival", 17485),
        new(AutoPlayContent.Autorun | AutoPlayContent.DvdMovie, "PlayEnhancedDVDOnArrival", 17486),
        new(AutoPlayContent.BluRay, "PlayBluRayOnArrival", 17487),
        new(AutoPlayContent.Storage, "StorageOnArrival", 17489),
        new(AutoPlayContent.MemoryCard, "ShowPicturesOnArrival", 17490, "CameraAlternate"),
    ];

    /// <summary>
    /// The content a volume shows when it arrives (shell32's <c>CAutoPlayParams::Init</c>): disc formats and cameras
    /// from the volume's markers, a program from <c>autorun.inf</c>; anything else is a removable drive
    /// (<see cref="AutoPlayContent.Storage"/>) unless the user chose to be asked for each type of media
    /// (<paramref name="perMediaType"/>), when it's <see cref="AutoPlayContent.None"/> until the files are looked at.
    /// </summary>
    /// <param name="autorunPolicy">The <c>NoAutorun</c> policy: 1 ignores autorun.inf programs.</param>
    public static AutoPlayContent Initial(AutoPlayVolume volume, bool perMediaType, uint autorunPolicy)
    {
        AutoPlayContent content = AutoPlayContent.None;
        if (volume.Autorun?.Command is not null && (autorunPolicy & 1) == 0)
            content |= AutoPlayContent.Autorun;
        if (volume.Media.HasFlag(VolumeMedia.AudioTracks))
            content |= AutoPlayContent.AudioCD;
        if (volume.Media.HasFlag(VolumeMedia.DvdVideo))
            content |= AutoPlayContent.DvdMovie;
        else if (volume.Media.HasFlag(VolumeMedia.DvdAudio))
            content |= AutoPlayContent.DvdAudio;
        if (volume.Media.HasFlag(VolumeMedia.VideoCD))
            content |= AutoPlayContent.VideoCD;
        if (volume.Media.HasFlag(VolumeMedia.SuperVideoCD))
            content |= AutoPlayContent.SuperVideoCD;
        if (volume.Media.HasFlag(VolumeMedia.BluRay))
            content |= AutoPlayContent.BluRay;
        if (volume.Media.HasFlag(VolumeMedia.Camera))
            content |= AutoPlayContent.MemoryCard;

        if (!perMediaType && content == AutoPlayContent.None)
            content = AutoPlayContent.Storage;
        return content;
    }

    /// <summary>
    /// The content once the files were looked at (twinui's <c>_FixFinalContent</c>): more than one kind is mixed
    /// content, none is unknown content.
    /// </summary>
    public static AutoPlayContent Final(AutoPlayContent content, IEnumerable<AutoPlayContent> found)
    {
        content = found.Aggregate(content, (all, kind) => all | kind);
        AutoPlayContent kinds = content & (AutoPlayContent.Storage | AutoPlayContent.Music | AutoPlayContent.Pictures | AutoPlayContent.Videos);
        if ((kinds & (kinds - 1)) != 0)
            content |= AutoPlayContent.Mixed;
        return content == AutoPlayContent.None ? AutoPlayContent.Unknown : content;
    }

    /// <summary>The event for the content (shell32's <c>IndexFromType</c>): mixed content wins; otherwise an exact match.</summary>
    public static AutoPlayEvent? EventFor(AutoPlayContent content) =>
        content.HasFlag(AutoPlayContent.Mixed)
            ? s_events.First(e => e.Content == AutoPlayContent.Mixed)
            : s_events.FirstOrDefault(e => e.Content == content);

    /// <summary>The <c>ARCONTENT_*</c> flags apps see in <c>QueryCancelAutoPlay</c> (shell32's mapping).</summary>
    public static uint ArContent(AutoPlayContent content)
    {
        (AutoPlayContent From, uint To)[] map =
        [
            (AutoPlayContent.Autorun, 0x2), (AutoPlayContent.AudioCD, 0x4), (AutoPlayContent.DvdMovie, 0x8),
            (AutoPlayContent.Unknown, 0x40), (AutoPlayContent.BlankCD, 0x10), (AutoPlayContent.BlankDvd, 0x20),
            (AutoPlayContent.BlankBluRay, 0x2000), (AutoPlayContent.Music, 0x100), (AutoPlayContent.Pictures, 0x80),
            (AutoPlayContent.Videos, 0x200), (AutoPlayContent.VideoCD, 0x400), (AutoPlayContent.SuperVideoCD, 0x800),
            (AutoPlayContent.DvdAudio, 0x1000), (AutoPlayContent.BluRay, 0x4000), (AutoPlayContent.Storage, 0x40),
            (AutoPlayContent.MemoryCard, 0x8000),
        ];
        return map.Where(m => content.HasFlag(m.From)).Aggregate(0u, (all, m) => all | m.To);
    }

    /// <summary>What AutoPlay does on arrival, given the user's saved choice for the event.</summary>
    public static AutoPlayStart Start(string? savedChoice, bool savedChoiceExists) => savedChoice switch
    {
        null or PromptEachTime => AutoPlayStart.Ask,
        TakeNoAction => AutoPlayStart.Nothing,
        // A choice since uninstalled is forgotten and the user asked again.
        _ => savedChoiceExists ? AutoPlayStart.RunSaved : AutoPlayStart.Ask,
    };

    /// <summary>
    /// The groups of choices the flyout lists, in order (twinui's <c>_PopulateUIFromContentHandler</c>): a disc's
    /// program under its own header, then the event's choices, then the general ones (open the folder, take no
    /// action). Each list is newest first (by registry key time), a choice only once, and media discs don't offer to
    /// open the folder.
    /// </summary>
    /// <param name="choices">The registered choices of an event, as <c>AutoPlayHandlers.ForEvent</c> reads them.</param>
    /// <param name="autorun">A disc's program, when the content has one.</param>
    /// <param name="found">The kinds of media files in the order the files were looked at, when they were: twinui adds
    /// each new kind's choices after those already listed, so the content group isn't wholly newest first.</param>
    public static IReadOnlyList<AutoPlayGroup> Groups(
        AutoPlayContent content, AutoPlayHandler? autorun, Func<string, IReadOnlyList<AutoPlayHandler>> choices, Func<string, AutoPlayHandler?> read,
        IReadOnlyList<AutoPlayContent>? found = null)
    {
        List<AutoPlayGroup> groups = [];
        HashSet<string> listed = new(StringComparer.OrdinalIgnoreCase);
        bool mediaDisc = (content & (AutoPlayContent.AudioCD | AutoPlayContent.DvdMovie | AutoPlayContent.DvdAudio
            | AutoPlayContent.VideoCD | AutoPlayContent.SuperVideoCD | AutoPlayContent.BluRay)) != 0
            && (content & ~(AutoPlayContent.AudioCD | AutoPlayContent.DvdMovie | AutoPlayContent.DvdAudio
                | AutoPlayContent.VideoCD | AutoPlayContent.SuperVideoCD | AutoPlayContent.BluRay)) == 0;

        void Add(AutoPlayGroupKind kind, IEnumerable<AutoPlayHandler> handlers)
        {
            List<AutoPlayHandler> list = [];
            foreach (AutoPlayHandler handler in handlers)
            {
                if ((mediaDisc && handler.Name == OpenFolder) || !listed.Add(handler.Name))
                    continue;
                list.Add(handler);
            }
            if (list.Count > 0)
                groups.Add(new AutoPlayGroup(kind, list));
        }

        // Each kind of content's choices (mixed content: the music, picture and video ones; it has none of its own).
        IEnumerable<AutoPlayHandler> ForContent(AutoPlayContent type, bool takeNoAction)
        {
            List<AutoPlayHandler> list = [];
            for (var kind = AutoPlayContent.Autorun; kind <= AutoPlayContent.MemoryCard; kind = (AutoPlayContent)((int)kind << 1))
            {
                if (!type.HasFlag(kind) || kind == AutoPlayContent.Mixed || EventFor(kind) is not { } autoPlayEvent)
                    continue;
                list.AddRange(choices(autoPlayEvent.Name));
                // Memory cards offer the video players too.
                if (kind == AutoPlayContent.MemoryCard)
                    list.AddRange(choices("PlayVideoFilesOnArrival"));
            }
            if (takeNoAction && read(TakeNoAction) is { } none)
                list.Add(none);
            return NewestFirst(list);
        }

        // An enhanced disc: its program, then the disc's own choices.
        bool enhanced = content is (AutoPlayContent.Autorun | AutoPlayContent.AudioCD) or (AutoPlayContent.Autorun | AutoPlayContent.DvdMovie);
        if (content.HasFlag(AutoPlayContent.Autorun) && autorun is not null)
        {
            Add(enhanced ? AutoPlayGroupKind.EnhancedContent : AutoPlayGroupKind.Program, [autorun]);
            if (enhanced)
                Add(AutoPlayGroupKind.Content, ForContent(content & ~AutoPlayContent.Autorun, takeNoAction: false));
        }
        else if (content != AutoPlayContent.Unknown)
        {
            // Blank discs and unknown content offer to take no action themselves.
            bool takeNoAction = (content & (AutoPlayContent.BlankCD | AutoPlayContent.BlankDvd | AutoPlayContent.BlankBluRay)) != 0;
            if (found is { Count: > 1 })
            {
                List<AutoPlayHandler> list = [];
                AutoPlayContent soFar = AutoPlayContent.None;
                foreach (AutoPlayContent kind in found)
                {
                    soFar |= kind;
                    list.AddRange(ForContent(soFar, takeNoAction).Where(h => !list.Any(l => l.Name.Equals(h.Name, StringComparison.OrdinalIgnoreCase))));
                }
                Add(AutoPlayGroupKind.Content, list);
            }
            else
            {
                Add(AutoPlayGroupKind.Content, ForContent(content, takeNoAction));
            }
        }
        Add(AutoPlayGroupKind.General, ForContent(AutoPlayContent.Unknown, takeNoAction: true));
        return groups;
    }

    /// <summary>
    /// Newest first, the order of shell32's <c>CAutoplayHandlerList::Add</c>: each goes before the first older one,
    /// so equally old ones keep their order.
    /// </summary>
    public static IEnumerable<AutoPlayHandler> NewestFirst(IEnumerable<AutoPlayHandler> handlers)
    {
        List<AutoPlayHandler> sorted = [];
        foreach (AutoPlayHandler handler in handlers)
        {
            int index = sorted.FindIndex(h => handler.LastWrite > h.LastWrite);
            sorted.Insert(index < 0 ? sorted.Count : index, handler);
        }
        return sorted;
    }

    /// <summary>
    /// Whether AutoPlay asks at all: only when there's more to choose than taking no action (twinui compares the
    /// count with the general group's "Take no action" and "Ask me every time").
    /// </summary>
    public static bool HasChoices(IReadOnlyList<AutoPlayGroup> groups) =>
        groups.SelectMany(g => g.Handlers).Any(h => h.Name is not (TakeNoAction or PromptEachTime));

    /// <summary>
    /// Whether picking a choice in the flyout makes it the default (twinui's "save default"): not for a disc's
    /// program, mixed or unknown content, nor when the user's setting is "Ask me every time" for this content.
    /// </summary>
    public static bool RemembersChoice(AutoPlayContent content, string? savedChoice) =>
        !content.HasFlag(AutoPlayContent.Autorun) && !content.HasFlag(AutoPlayContent.Mixed) && content != AutoPlayContent.Unknown
        && savedChoice != PromptEachTime;
}

/// <summary>What AutoPlay does when media arrives.</summary>
public enum AutoPlayStart
{
    /// <summary>Show the toast and, when clicked, the choices.</summary>
    Ask,
    RunSaved,
    Nothing,
}

public enum AutoPlayGroupKind
{
    /// <summary>A disc's program, under "Install or run program from your media".</summary>
    Program,
    /// <summary>An enhanced disc's program, under "Run enhanced content".</summary>
    EnhancedContent,
    Content,
    General,
}

public sealed record AutoPlayGroup(AutoPlayGroupKind Kind, IReadOnlyList<AutoPlayHandler> Handlers);
