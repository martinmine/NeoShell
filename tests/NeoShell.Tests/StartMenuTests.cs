using NeoShell.Interop.Search;
using NeoShell.Interop.Shell;
using NeoShell.Settings;
using NeoShell.StartMenu;

namespace NeoShell.Tests;

public sealed class StartMenuTests
{
    [Theory]
    [InlineData("Notepad", "notepad", 0)]
    [InlineData("Notepad", "note", 1)]
    [InlineData("Visual Studio Code", "code", 2)]
    [InlineData("Visual Studio Code", "studio code", 2)]
    [InlineData("Visual Studio Code", "vis cod", 2)]
    [InlineData("Windows PowerShell (x86)", "x86", 2)]
    [InlineData("Notepad", "pad", 3)]
    [InlineData("Notepad", "paint", -1)]
    [InlineData("Notepad", "", -1)]
    [InlineData("Notepad", "   ", -1)]
    [InlineData("Paint", "  PAINT ", 0)]
    public void Score_ranks_exact_then_prefix_then_word_start_then_contains(string name, string query, int expected)
    {
        Assert.Equal(expected, AppSearch.Score(name, query));
    }

    [Fact]
    public void Rank_puts_better_matches_first_and_ties_in_alphabetical_order()
    {
        string[] apps = ["Snipping Tool", "Paint", "Paint 3D", "Microsoft Paint Tool", "Command Prompt", "Unpainted"];

        Assert.Equal(["Paint", "Paint 3D", "Microsoft Paint Tool", "Unpainted"], AppSearch.Rank(apps, a => a, "paint"));
    }

    [Fact]
    public void Packaged_app_is_kept_by_its_app_id()
    {
        var entry = new AppCatalogEntry("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null);

        Assert.Equal(new PinnedApp("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null), StartCatalog.ToPinnedApp(entry));
    }

    [Fact]
    public void Shortcut_with_an_app_id_keeps_its_target_so_windows_without_one_still_match()
    {
        var entry = new AppCatalogEntry("Visual Studio Code", "Microsoft.VisualStudioCode", @"C:\Tools\VS Code\Code.exe");

        Assert.Equal(new PinnedApp("Visual Studio Code", "Microsoft.VisualStudioCode", @"C:\Tools\VS Code\Code.exe"), StartCatalog.ToPinnedApp(entry));
    }

    [Fact]
    public void Path_entry_is_kept_by_its_resolved_path()
    {
        // {1AC14E77-...} is FOLDERID_System.
        var entry = new AppCatalogEntry("Character Map", @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\charmap.exe", null);

        PinnedApp app = StartCatalog.ToPinnedApp(entry);

        Assert.Null(app.AppUserModelId);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "charmap.exe"), app.Path, ignoreCase: true);
    }

    [Theory]
    [InlineData("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", true)]
    [InlineData("MSEdge", true)]
    [InlineData(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\charmap.exe", false)]
    [InlineData(@"C:\Tools\tool.exe", false)]
    [InlineData("https://nodejs.org/", false)]
    public void App_ids_are_told_apart_from_paths_and_urls(string id, bool expected)
    {
        Assert.Equal(expected, AppCatalog.IsAppUserModelId(id));
    }

    [Fact]
    public void Known_folder_prefix_is_split_off()
    {
        Assert.Equal(
            (new Guid("6D809377-6AF0-444B-8957-A3773F02200E"), @"Git\cmd\git-gui.exe"),
            AppCatalog.SplitKnownFolder(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Git\cmd\git-gui.exe"));
        Assert.Null(AppCatalog.SplitKnownFolder(@"C:\Tools\tool.exe"));
        Assert.Null(AppCatalog.SplitKnownFolder(@"{not-a-guid}\tool.exe"));
    }

    [Theory]
    [InlineData(new[] { "folder" }, StartCatalog.Folders)]
    [InlineData(new[] { "document" }, StartCatalog.Documents)]
    [InlineData(new[] { "picture" }, StartCatalog.Documents)]
    [InlineData(new[] { "Music" }, StartCatalog.Documents)]
    [InlineData(new[] { "program" }, StartCatalog.Other)]
    [InlineData(new string[0], StartCatalog.Other)]
    public void Search_results_are_grouped_by_kind(string[] kinds, string expected)
    {
        Assert.Equal(expected, StartCatalog.GroupFor(new IndexResult("x", @"C:\x", kinds)));
    }

    [Theory]
    [InlineData("Notepad", "N")]
    [InlineData("éclair", "É")]
    [InlineData("7-Zip", "#")]
    [InlineData("", "#")]
    public void All_apps_letter_is_the_first_letter_or_hash(string name, string expected)
    {
        Assert.Equal(expected, StartCatalog.LetterFor(name));
    }

    [Theory]
    [InlineData("  quarterly   report ", "quarterly report")]
    [InlineData("report", "report")]
    [InlineData("\t", "")]
    public void Index_query_whitespace_is_collapsed(string query, string expected)
    {
        Assert.Equal(expected, IndexSearch.Normalize(query));
    }

    [Fact]
    public void Index_kinds_column_is_read_whatever_shape_it_comes_in()
    {
        Assert.Equal(["folder"], IndexSearch.ReadKinds("folder"));
        Assert.Equal(["document", "picture"], IndexSearch.ReadKinds(new[] { "document", "picture" }));
        Assert.Equal(["document"], IndexSearch.ReadKinds(new object[] { "document", 5 }));
        Assert.Empty(IndexSearch.ReadKinds(DBNull.Value));
    }

    [Fact]
    public void Index_query_searches_names_of_files_and_leaves_out_shortcuts()
    {
        Assert.Equal("System.ItemNameDisplay", IndexSearch.ContentProperties);
        Assert.Equal("AND SCOPE='file:' AND System.FileExtension <> '.lnk'", IndexSearch.WhereRestrictions);
        Assert.StartsWith("System.ItemNameDisplay,System.ItemPathDisplay,", IndexSearch.SelectColumns);
    }
}
