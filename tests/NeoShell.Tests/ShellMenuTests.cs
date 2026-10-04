using NeoShell.Interop.Shell;

namespace NeoShell.Tests;

public sealed class ShellMenuTests
{
    [Theory]
    [InlineData("&Open", "Open")]
    [InlineData("Open &with", "Open with")]
    [InlineData("Save && exit", "Save & exit")]
    [InlineData("&Paste\tCtrl+V", "Paste")]
    [InlineData("Ends with &", "Ends with &")]
    [InlineData(" Spaced ", "Spaced")]
    [InlineData("", "")]
    public void Labels_lose_access_keys_and_shortcuts(string label, string expected)
    {
        Assert.Equal(expected, MenuText.Clean(label));
    }
}
