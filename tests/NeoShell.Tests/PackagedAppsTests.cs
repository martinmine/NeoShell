using System.Xml.Linq;
using NeoShell.Interop.Shell;

namespace NeoShell.Tests;

public sealed class PackagedAppsTests
{
    private static readonly XDocument s_manifest = XDocument.Parse("""
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
          <Applications>
            <Application Id="App">
              <uap:VisualElements DisplayName="Notepad" Square44x44Logo="Assets\NotepadAppList.png" />
            </Application>
            <Application Id="Helper">
              <uap:VisualElements DisplayName="Helper" Square44x44Logo="Assets\Helper.png" AppListEntry="none" />
            </Application>
          </Applications>
        </Package>
        """);

    [Theory]
    [InlineData("App", @"Assets\NotepadAppList.png")]
    [InlineData("Helper", @"Assets\Helper.png")]
    [InlineData("Missing", null)]
    public void Manifest_logo_is_the_apps_square_44_logo(string appId, string? expected) =>
        Assert.Equal(expected, PackagedApps.ManifestLogo(s_manifest, appId));

    [Fact]
    public void Logo_candidates_prefer_the_unplated_target_size()
    {
        Assert.Equal(
            [@"Assets\NotepadAppList.targetsize-24_altform-unplated.png", @"Assets\NotepadAppList.targetsize-24.png"],
            PackagedApps.LogoCandidates(@"Assets\NotepadAppList.png", 24));
    }
}
