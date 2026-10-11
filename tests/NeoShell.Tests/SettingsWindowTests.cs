using NeoShell.Settings;

namespace NeoShell.Tests;

public sealed class SettingsWindowTests
{
    [Fact]
    public void Every_page_has_its_own_title_and_glyph()
    {
        SettingsPage[] pages = Enum.GetValues<SettingsPage>();

        Assert.Equal(pages.Length, pages.Select(SettingsPages.Title).Distinct().Count());
        Assert.Equal(pages.Length, pages.Select(SettingsPages.Glyph).Distinct().Count());
        Assert.Equal("Taskbar", SettingsPages.Title(pages[0]));
        Assert.Equal("About", SettingsPages.Title(pages[^1]));
    }

    [Theory]
    [InlineData("1.0.0+02b21f0bbcf8f89bca7b35348990f8a939e340a7", "1.0.0 (02b21f0)")]
    [InlineData("1.2.3+abc", "1.2.3 (abc)")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("", "Unknown")]
    [InlineData(null, "Unknown")]
    public void Version_shows_the_short_commit(string? informational, string expected)
    {
        Assert.Equal(expected, SettingsPages.Version(informational));
    }

    [Fact]
    public void Run_modes_have_their_own_texts()
    {
        Assert.Equal("As the shell", SettingsPages.RunModeTitle(RunMode.Shell));
        Assert.Equal("Alongside Explorer", SettingsPages.RunModeTitle(RunMode.AlongsideExplorer));
        Assert.NotEqual(SettingsPages.RunModeDescription(RunMode.Shell), SettingsPages.RunModeDescription(RunMode.AlongsideExplorer));
    }
}
