using NeoShell.Taskbar;

namespace NeoShell.Tests;

public sealed class ShowDesktopTests
{
    [Theory]
    [InlineData(null, null, false)] // Windows 11's default: off until Settings writes 0
    [InlineData(0, null, true)]
    [InlineData(1, null, false)]
    [InlineData(0, 1, false)] // "Turn off Aero Shake window minimizing mouse gesture"
    [InlineData(0, 0, true)]
    public void Title_bar_shake_is_off_unless_DisallowShaking_is_0(int? disallowShaking, int? policy, bool expected)
    {
        Assert.Equal(expected, ShowDesktop.ShakingAllowed(disallowShaking, policy));
    }
}
