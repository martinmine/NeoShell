using NeoShell.Capture;

namespace NeoShell.Tests;

public sealed class CaptureTests
{
    private static readonly DateTime Taken = new(2026, 10, 5, 18, 32, 7);

    [Fact]
    public void Screenshots_are_named_after_the_time_as_windows_names_them()
    {
        Assert.Equal("Screenshot 2026-10-05 183207.png", Screenshots.FileName(Taken, _ => false));
    }

    [Fact]
    public void Screenshots_in_the_same_second_are_numbered()
    {
        string[] existing = ["Screenshot 2026-10-05 183207.png", "Screenshot 2026-10-05 183207 (2).png"];

        Assert.Equal("Screenshot 2026-10-05 183207 (3).png", Screenshots.FileName(Taken, existing.Contains));
    }
}
