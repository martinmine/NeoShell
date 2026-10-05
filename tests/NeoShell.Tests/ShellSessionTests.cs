namespace NeoShell.Tests;

public sealed class ShellSessionTests
{
    private const int Win = 0x5B, RightWin = 0x5C, Control = 0xA2, Escape = 0x1B, D = 0x44, Shift = 0xA0;

    private static bool[] Feed(params (int Key, bool Down)[] keys)
    {
        var detector = new StartKeyDetector();
        return [.. keys.Select(key => detector.OnKey(key.Key, key.Down))];
    }

    [Fact]
    public void Windows_key_alone_opens_start_on_release()
    {
        Assert.Equal([false, true], Feed((Win, true), (Win, false)));
        Assert.Equal([false, true], Feed((RightWin, true), (RightWin, false)));
    }

    [Fact]
    public void Windows_key_repeat_still_counts_as_alone()
    {
        Assert.Equal([false, false, false, true], Feed((Win, true), (Win, true), (Win, true), (Win, false)));
    }

    [Fact]
    public void Windows_key_combinations_dont_open_start()
    {
        Assert.Equal([false, false, false, false], Feed((Win, true), (D, true), (D, false), (Win, false)));
        Assert.Equal([false, false, false], Feed((Win, true), (Control, true), (Win, false)));
    }

    [Fact]
    public void Keys_held_before_the_windows_key_dont_spoil_the_next_press()
    {
        Assert.Equal([false, false, false, true], Feed((Shift, true), (Shift, false), (Win, true), (Win, false)));
    }

    [Fact]
    public void Control_escape_opens_start()
    {
        Assert.Equal([false, true, false, false], Feed((Control, true), (Escape, true), (Escape, false), (Control, false)));
        Assert.Equal([false, false], Feed((Escape, true), (Escape, false)));
    }

    private const int Comma = 0xBC;

    private static (bool Swallowed, bool? Peek)[] FeedPeek(params (int Key, bool Down)[] keys)
    {
        var detector = new PeekKeys();
        return [.. keys.Select(key => (detector.OnKey(key.Key, key.Down, out bool? peek), peek))];
    }

    [Fact]
    public void Win_comma_peeks_until_the_windows_key_is_let_go_of()
    {
        Assert.Equal(
            [(false, null), (true, true), (true, null), (true, null), (false, false)],
            FeedPeek((Win, true), (Comma, true), (Comma, true), (Comma, false), (Win, false)));
    }

    [Fact]
    public void Comma_let_go_of_after_the_windows_key_is_still_swallowed()
    {
        Assert.Equal(
            [(false, null), (true, true), (false, false), (true, null)],
            FeedPeek((Win, true), (Comma, true), (Win, false), (Comma, false)));
    }

    [Fact]
    public void Comma_without_the_windows_key_is_typed()
    {
        Assert.Equal([(false, null), (false, null)], FeedPeek((Comma, true), (Comma, false)));
        Assert.Equal([(false, null), (false, null), (false, null)], FeedPeek((Win, true), (D, true), (Win, false)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[0], true)]
    [InlineData(new byte[] { 0x02, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0 }, false)]
    public void Startup_approved_first_byte_odd_means_disabled(byte[]? approval, bool expected)
    {
        Assert.Equal(expected, StartupApps.IsApproved(approval));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" /background", @"C:\Program Files\App\app.exe", "/background")]
    [InlineData("\"C:\\App\\app.exe\"", @"C:\App\app.exe", "")]
    [InlineData("C:\\Program Files\\App\\app.exe -n user", @"C:\Program Files\App\app.exe", "-n user")]
    [InlineData("C:\\Program Files\\App\\app -n user", @"C:\Program Files\App\app.exe", "-n user")]
    [InlineData("C:\\Windows\\tool.exe", @"C:\Windows\tool.exe", "")]
    [InlineData("missing.exe --flag", "missing.exe", "--flag")]
    [InlineData("  \"C:\\App\\app.exe\"   -x  ", @"C:\App\app.exe", "-x")]
    public void Run_command_lines_split_like_create_process(string commandLine, string program, string arguments)
    {
        string[] existing = [@"C:\Program Files\App\app.exe", @"C:\App\app.exe", @"C:\Windows\tool.exe"];

        Assert.Equal((program, arguments), StartupApps.SplitCommandLine(commandLine, existing.Contains));
    }
}
