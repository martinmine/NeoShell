using NeoShell.AutoPlay;
using NeoShell.Interop.Shell;

namespace NeoShell.Tests;

public sealed class AutoPlayTests
{
    private static AutoPlayVolume Volume(VolumeMedia media = VolumeMedia.None, AutorunInf? autorun = null) =>
        new(@"F:\", "DVD Drive (F:) TEST", DriveType.CDRom, media, autorun, "TEST", 1);

    private static AutoPlayHandler Handler(string name, long lastWrite = 0) => new(name, name + " action", "Provider", null, lastWrite);

    [Fact]
    public void A_drive_without_special_content_is_a_removable_drive()
    {
        Assert.Equal(AutoPlayContent.Storage, AutoPlayRules.Initial(Volume(), perMediaType: false, autorunPolicy: 0));
    }

    [Fact]
    public void Asking_for_each_type_of_media_leaves_the_content_to_the_files()
    {
        Assert.Equal(AutoPlayContent.None, AutoPlayRules.Initial(Volume(), perMediaType: true, autorunPolicy: 0));
        Assert.Equal(AutoPlayContent.Pictures, AutoPlayRules.Final(AutoPlayContent.None, [AutoPlayContent.Pictures]));
        Assert.Equal(AutoPlayContent.Unknown, AutoPlayRules.Final(AutoPlayContent.None, []));
    }

    [Fact]
    public void More_than_one_kind_of_media_is_mixed_content()
    {
        AutoPlayContent content = AutoPlayRules.Final(AutoPlayContent.None, [AutoPlayContent.Music, AutoPlayContent.Pictures]);
        Assert.True(content.HasFlag(AutoPlayContent.Mixed));
        Assert.Equal("MixedContentOnArrival", AutoPlayRules.EventFor(content)!.Name);
    }

    [Fact]
    public void Disc_formats_and_cameras_come_from_the_volume()
    {
        Assert.Equal(AutoPlayContent.DvdMovie, AutoPlayRules.Initial(Volume(VolumeMedia.DvdVideo), false, 0));
        Assert.Equal(AutoPlayContent.AudioCD, AutoPlayRules.Initial(Volume(VolumeMedia.AudioTracks), false, 0));
        Assert.Equal(AutoPlayContent.BluRay, AutoPlayRules.Initial(Volume(VolumeMedia.BluRay), false, 0));
        Assert.Equal(AutoPlayContent.MemoryCard, AutoPlayRules.Initial(Volume(VolumeMedia.Camera), false, 0));
        Assert.Equal(AutoPlayContent.MemoryCard, AutoPlayRules.Initial(Volume(VolumeMedia.Camera), true, 0));
    }

    [Fact]
    public void A_disc_program_counts_unless_policy_ignores_it()
    {
        var autorun = new AutorunInf(@"F:\setup.exe", false, null, null, null);
        Assert.Equal(AutoPlayContent.Autorun, AutoPlayRules.Initial(Volume(autorun: autorun), false, 0));
        Assert.Equal(AutoPlayContent.Storage, AutoPlayRules.Initial(Volume(autorun: autorun), false, autorunPolicy: 1));
        // A label or icon alone is no program.
        Assert.Equal(AutoPlayContent.Storage, AutoPlayRules.Initial(Volume(autorun: autorun with { Command = null }), false, 0));
    }

    [Fact]
    public void A_disc_program_with_audio_or_a_film_is_enhanced()
    {
        var autorun = new AutorunInf(@"F:\setup.exe", false, null, null, null);
        Assert.Equal("PlayEnhancedCDOnArrival", AutoPlayRules.EventFor(AutoPlayRules.Initial(Volume(VolumeMedia.AudioTracks, autorun), false, 0))!.Name);
        Assert.Equal("PlayEnhancedDVDOnArrival", AutoPlayRules.EventFor(AutoPlayRules.Initial(Volume(VolumeMedia.DvdVideo, autorun), false, 0))!.Name);
    }

    [Fact]
    public void Events_match_shell32s_table()
    {
        Assert.Equal("StorageOnArrival", AutoPlayRules.EventFor(AutoPlayContent.Storage)!.Name);
        Assert.Equal(17489, AutoPlayRules.EventFor(AutoPlayContent.Storage)!.Description);
        Assert.Equal("AutorunINFLegacyArrival", AutoPlayRules.EventFor(AutoPlayContent.Autorun)!.Name);
        Assert.Equal("PlayDVDMovieOnArrival", AutoPlayRules.EventFor(AutoPlayContent.DvdMovie)!.Name);
        AutoPlayEvent memoryCard = AutoPlayRules.EventFor(AutoPlayContent.MemoryCard)!;
        Assert.Equal(("ShowPicturesOnArrival", "CameraAlternate"), (memoryCard.Name, memoryCard.ChoiceGroup));
        Assert.Null(AutoPlayRules.EventFor(AutoPlayContent.VideoCD | AutoPlayContent.BluRay));
    }

    [Fact]
    public void Apps_hear_the_arcontent_flags()
    {
        Assert.Equal(0x40u, AutoPlayRules.ArContent(AutoPlayContent.Storage));
        Assert.Equal(0x80u | 0x100u, AutoPlayRules.ArContent(AutoPlayContent.Pictures | AutoPlayContent.Music));
        Assert.Equal(0x2u | 0x8u, AutoPlayRules.ArContent(AutoPlayContent.Autorun | AutoPlayContent.DvdMovie));
    }

    [Fact]
    public void The_saved_choice_decides_whether_to_ask()
    {
        Assert.Equal(AutoPlayStart.Ask, AutoPlayRules.Start(null, false));
        Assert.Equal(AutoPlayStart.Ask, AutoPlayRules.Start("MSPromptEachTime", true));
        Assert.Equal(AutoPlayStart.Nothing, AutoPlayRules.Start("MSTakeNoAction", true));
        Assert.Equal(AutoPlayStart.RunSaved, AutoPlayRules.Start("MSOpenFolder", true));
        // Uninstalled since.
        Assert.Equal(AutoPlayStart.Ask, AutoPlayRules.Start("GoneHandler", false));
    }

    [Fact]
    public void Choices_are_newest_first_keeping_the_order_of_equals()
    {
        AutoPlayHandler[] handlers = [Handler("A", 5), Handler("B", 9), Handler("C", 5), Handler("D", 1)];
        Assert.Equal(["B", "A", "C", "D"], AutoPlayRules.NewestFirst(handlers).Select(h => h.Name));
    }

    // The registry of this VM: the storage event's choice, the general ones, and a DVD player and the Store's.
    private static readonly Dictionary<string, AutoPlayHandler[]> s_events = new()
    {
        ["StorageOnArrival"] = [Handler("MSStorageSense", 3)],
        ["UnknownContentOnArrival"] = [Handler("MSOpenFolder", 2)],
        ["PlayDVDMovieOnArrival"] = [Handler("FindAppPlayDVDMovieOnArrival", 2), Handler("VLCPlayDVDMovieOnArrival", 7)],
        ["ShowPicturesOnArrival"] = [Handler("Photos", 9)],
        ["PlayVideoFilesOnArrival"] = [Handler("VLCPlayVideoFilesOnArrival", 7)],
    };

    private static IReadOnlyList<AutoPlayGroup> Groups(
        AutoPlayContent content, AutoPlayHandler? program = null, IReadOnlyList<AutoPlayContent>? found = null) =>
        AutoPlayRules.Groups(
            content, program, name => s_events.TryGetValue(name, out var handlers) ? handlers : [],
            name => name == "MSTakeNoAction" ? Handler(name, 2) : null, found);

    private static string Names(IReadOnlyList<AutoPlayGroup> groups) =>
        string.Join(" | ", groups.Select(g => $"{g.Kind}: {string.Join(", ", g.Handlers.Select(h => h.Name))}"));

    [Fact]
    public void A_removable_drive_offers_its_choices_then_the_general_ones()
    {
        Assert.Equal("Content: MSStorageSense | General: MSOpenFolder, MSTakeNoAction", Names(Groups(AutoPlayContent.Storage)));
    }

    [Fact]
    public void A_film_doesnt_offer_to_open_the_folder()
    {
        Assert.Equal(
            "Content: VLCPlayDVDMovieOnArrival, FindAppPlayDVDMovieOnArrival | General: MSTakeNoAction",
            Names(Groups(AutoPlayContent.DvdMovie)));
    }

    [Fact]
    public void A_disc_program_comes_first_under_its_own_header()
    {
        Assert.Equal("Program: MSAutoRun | General: MSOpenFolder, MSTakeNoAction", Names(Groups(AutoPlayContent.Autorun, Handler("MSAutoRun"))));
        Assert.Equal(
            "EnhancedContent: MSAutoRun | Content: VLCPlayDVDMovieOnArrival, FindAppPlayDVDMovieOnArrival | General: MSOpenFolder, MSTakeNoAction",
            Names(Groups(AutoPlayContent.Autorun | AutoPlayContent.DvdMovie, Handler("MSAutoRun"))));
    }

    [Fact]
    public void A_memory_card_offers_picture_and_video_apps()
    {
        Assert.Equal(
            "Content: Photos, VLCPlayVideoFilesOnArrival | General: MSOpenFolder, MSTakeNoAction",
            Names(Groups(AutoPlayContent.MemoryCard)));
    }

    [Fact]
    public void Mixed_content_offers_each_kinds_choices_in_the_order_the_kinds_were_found()
    {
        AutoPlayContent mixed = AutoPlayContent.Mixed | AutoPlayContent.Pictures | AutoPlayContent.Videos;
        Assert.Equal(
            "Content: Photos, VLCPlayVideoFilesOnArrival | General: MSOpenFolder, MSTakeNoAction",
            Names(Groups(mixed, found: [AutoPlayContent.Pictures, AutoPlayContent.Videos])));
        // The videos found first: their player stays first though the picture app is newer.
        Assert.Equal(
            "Content: VLCPlayVideoFilesOnArrival, Photos | General: MSOpenFolder, MSTakeNoAction",
            Names(Groups(mixed, found: [AutoPlayContent.Videos, AutoPlayContent.Pictures])));
    }

    [Fact]
    public void Unknown_content_has_only_the_general_choices_and_with_nothing_else_doesnt_ask()
    {
        IReadOnlyList<AutoPlayGroup> groups = Groups(AutoPlayContent.Unknown);
        Assert.Equal("General: MSOpenFolder, MSTakeNoAction", Names(groups));
        Assert.True(AutoPlayRules.HasChoices(groups));
        Assert.False(AutoPlayRules.HasChoices([new AutoPlayGroup(AutoPlayGroupKind.General, [Handler("MSTakeNoAction")])]));
    }

    [Fact]
    public void Choices_are_remembered_except_for_programs_mixed_and_unknown_content()
    {
        Assert.True(AutoPlayRules.RemembersChoice(AutoPlayContent.Storage, null));
        Assert.False(AutoPlayRules.RemembersChoice(AutoPlayContent.Autorun, null));
        Assert.False(AutoPlayRules.RemembersChoice(AutoPlayContent.Mixed | AutoPlayContent.Music | AutoPlayContent.Pictures, null));
        Assert.False(AutoPlayRules.RemembersChoice(AutoPlayContent.Unknown, null));
        Assert.False(AutoPlayRules.RemembersChoice(AutoPlayContent.Storage, "MSPromptEachTime"));
    }

    [Fact]
    public void Autorun_inf_takes_the_64_bit_section_and_qualifies_paths_to_the_drive()
    {
        string[] lines =
        [
            "[AutoRun]", "open=setup32.exe", "label=Disc",
            "[AutoRun.Amd64]", "open=\"bin\\setup.exe\" /auto", "icon=setup.exe,1", "action=Install the game",
        ];
        AutorunInf autorun = AutoPlayVolumes.ParseAutorun(lines, @"F:\", optical: true)!;
        Assert.Equal("\"F:\\bin\\setup.exe\" /auto", autorun.Command);
        Assert.Equal(@"F:\setup.exe,1", autorun.Icon);
        Assert.Equal("Install the game", autorun.Action);
        // The label sits in the other section.
        Assert.Null(autorun.Label);
    }

    [Fact]
    public void Autorun_inf_runs_nothing_off_optical_drives()
    {
        AutorunInf autorun = AutoPlayVolumes.ParseAutorun(["[autorun]", "open=setup.exe", "label=Stick", "icon=stick.ico"], @"E:\", optical: false)!;
        Assert.Null(autorun.Command);
        Assert.Equal("Stick", autorun.Label);
        Assert.Equal(@"E:\stick.ico", autorun.Icon);
    }

    [Fact]
    public void Autorun_inf_shellexecute_and_resource_actions()
    {
        AutorunInf autorun = AutoPlayVolumes.ParseAutorun(["[AutoRun]", "shellexecute=index.htm", "action=@res.dll,-101"], @"F:\", true)!;
        Assert.Equal(@"F:\index.htm", autorun.Command);
        Assert.True(autorun.ShellExecute);
        Assert.Equal(@"@F:\res.dll,-101", autorun.Action);
        Assert.Null(AutoPlayVolumes.ParseAutorun(["[Other]", "open=x.exe"], @"F:\", true));
    }

    [Fact]
    public void Optical_drives_are_named_by_what_they_read_and_write()
    {
        Assert.Equal(9316, OpticalDrives.NameFor(OpticalDrives.Mmc2 | OpticalDrives.CdRead | OpticalDrives.DvdRead));
        Assert.Equal(9348, OpticalDrives.NameFor(OpticalDrives.Mmc2 | OpticalDrives.DvdRead | OpticalDrives.CdRewritable | OpticalDrives.CdRecordable));
        Assert.Equal(9347, OpticalDrives.NameFor(OpticalDrives.Mmc2 | OpticalDrives.DvdRead | OpticalDrives.DvdPlusRewritable));
        Assert.Equal(9373, OpticalDrives.NameFor(OpticalDrives.Mmc2 | OpticalDrives.DvdRead | OpticalDrives.BluRayRead));
        // Not an MMC-2 drive.
        Assert.Equal(OpticalDrives.CdDrive, OpticalDrives.NameFor(OpticalDrives.DvdRead));
    }

    [Fact]
    public void Drive_names_get_the_drive_type_and_autorun_label_explorer_shows()
    {
        Assert.Equal("DVD Drive (F:) NEOPICS", AutoPlayVolumes.DisplayName("CD Drive (F:) NEOPICS", "DVD Drive", "NEOPICS", null));
        Assert.Equal("DVD Drive (F:) Game", AutoPlayVolumes.DisplayName("CD Drive (F:) GAME_DISC", "DVD Drive", "GAME_DISC", "Game"));
        Assert.Equal("USB Drive (E:) Stick", AutoPlayVolumes.DisplayName("USB Drive (E:)", null, "", "Stick"));
    }

    [Fact]
    public void Commands_split_into_program_and_arguments()
    {
        Assert.Equal((@"F:\my setup.exe", "/a /b"), AutoPlayVolumes.SplitCommand("\"F:\\my setup.exe\" /a /b"));
        Assert.Equal((@"F:\setup.exe", ""), AutoPlayVolumes.SplitCommand(@"F:\setup.exe"));
    }
}
