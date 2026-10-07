using NeoShell.Interop.Input;
using NeoShell.Tray;

namespace NeoShell.Tests;

public sealed class InputIndicatorTests
{
    private static readonly Guid JapaneseIme = new("a76c93d9-5523-4e90-aafa-4db112f9ac76");
    private static readonly Guid UnknownIme = new("11111111-2222-3333-4444-555555555555");

    private static readonly InputMethod UkNorwegian =
        new("0809:00000414", "en-GB", "ENG", "English (United Kingdom)", "Norwegian", 0x04140809, Guid.Empty);
    private static readonly InputMethod Us =
        new("0409:00000409", "en-US", "ENG", "English (United States)", "US", 0x04090409, Guid.Empty);
    private static readonly InputMethod Japanese =
        new("0411:{03B5835F-F03C-411B-9CE2-AA23E1171E36}{A76C93D9-5523-4E90-AAFA-4DB112F9AC76}", "ja-JP", "JPN", "Japanese",
            "Microsoft IME", 0, JapaneseIme);
    private static readonly InputMethod[] Methods = [UkNorwegian, Us, Japanese];

    private static CurrentInputMethod Layout(nint hkl, string keyboardCode) =>
        new("ENG", keyboardCode, "English (United Kingdom)", "Norwegian keyboard", "en-GB", hkl, false);

    private static readonly CurrentInputMethod JapaneseInFront =
        new("日本", "", "Japanese", "Microsoft IME", "ja", 0, true);

    [Fact]
    public void Keyboard_layout_in_front_is_found_by_its_handle()
    {
        Assert.Same(UkNorwegian, IndicatorDisplay.MatchInputMethod(Methods, Layout(0x04140809, "NO")));
        Assert.Same(Us, IndicatorDisplay.MatchInputMethod(Methods, Layout(0x04090409, "US")));
        Assert.Null(IndicatorDisplay.MatchInputMethod(Methods, Layout(0x04070407, "DE")));
    }

    [Fact]
    public void Text_service_in_front_is_found_by_its_language_and_name()
    {
        // The switcher names the language "ja"; the list has the full tag.
        Assert.Same(Japanese, IndicatorDisplay.MatchInputMethod(Methods, JapaneseInFront));
        Assert.Null(IndicatorDisplay.MatchInputMethod([UkNorwegian, Us], JapaneseInFront));
    }

    [Fact]
    public void Keyboard_layout_shows_the_language_over_the_keyboard()
    {
        Assert.Equal((null, "ENG", "NO"), IndicatorDisplay.InputLabel(Layout(0x04140809, "NO"), UkNorwegian));
    }

    [Fact]
    public void Text_service_shows_its_glyph_or_else_its_letters_alone()
    {
        Assert.Equal(("", "", ""), IndicatorDisplay.InputLabel(JapaneseInFront, Japanese));
        Assert.Equal((null, "日本", ""), IndicatorDisplay.InputLabel(JapaneseInFront, Japanese with { TextServiceProfile = UnknownIme }));
        Assert.Null(IndicatorDisplay.InputMethodGlyph(Us));
    }

    [Fact]
    public void Tooltip_names_the_language_the_keyboard_and_how_to_switch()
    {
        Assert.Equal(
            "English (United Kingdom)\nNorwegian\n\nTo switch input methods, press Windows key + space.",
            IndicatorDisplay.InputToolTip(Layout(0x04140809, "NO"), UkNorwegian));
        // Without the list, the switcher's own name for the keyboard.
        Assert.StartsWith("English (United Kingdom)\nNorwegian keyboard\n", IndicatorDisplay.InputToolTip(Layout(0x04140809, "NO"), null));
    }

    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(2, false, 0)]
    [InlineData(0, true, 2)]
    [InlineData(1, true, 0)]
    [InlineData(-1, false, 0)]
    [InlineData(-1, true, 2)]
    public void Win_space_moves_round_the_list(int current, bool backwards, int expected)
    {
        Assert.Equal(expected, IndicatorDisplay.NextInputMethod(3, current, backwards));
    }

    [Theory]
    [InlineData(0x04140809L, "00000414")]
    [InlineData(0x04090409L, "00000409")]
    [InlineData(0xF0020409L, "00020409")]
    public void Layout_id_comes_from_the_handle_or_the_registry_for_a_variant(long hkl, string expected)
    {
        // A variant's device word is 0xF000 with its "Layout Id"; the registry maps 0002 to US-International.
        Assert.Equal(expected, InputMethods.LayoutId((nint)hkl, id => id == "0002" ? "00020409" : null));
    }

    private const int Win = 0x5B, Space = 0x20, Shift = 0xA0, Control = 0xA2;

    private static (bool Swallowed, InputSwitchCommand? Command)[] Feed(params (int Key, bool Down)[] keys)
    {
        var detector = new InputSwitchKeys();
        return [.. keys.Select(key => (detector.OnKey(key.Key, key.Down, out InputSwitchCommand? command), command))];
    }

    [Fact]
    public void Win_space_opens_moves_on_and_switches_when_the_windows_key_is_let_go_of()
    {
        Assert.Equal(
            [(false, null), (true, InputSwitchCommand.Open), (true, null), (true, InputSwitchCommand.Next), (true, null),
             (false, InputSwitchCommand.Commit)],
            Feed((Win, true), (Space, true), (Space, false), (Space, true), (Space, false), (Win, false)));
    }

    [Fact]
    public void Shift_goes_backwards()
    {
        Assert.Equal(
            [(false, null), (false, null), (true, InputSwitchCommand.OpenBackwards), (true, null), (false, null),
             (true, InputSwitchCommand.Next), (true, null), (false, InputSwitchCommand.Commit)],
            Feed((Win, true), (Shift, true), (Space, true), (Space, false), (Shift, false), (Space, true), (Space, false), (Win, false)));
    }

    [Fact]
    public void Space_without_the_windows_key_or_with_control_is_typed()
    {
        Assert.Equal([(false, null), (false, null)], Feed((Space, true), (Space, false)));
        Assert.Equal(
            [(false, null), (false, null), (false, null), (false, null), (false, null), (false, null)],
            Feed((Win, true), (Control, true), (Space, true), (Space, false), (Control, false), (Win, false)));
    }

    [Fact]
    public void Space_let_go_of_after_the_windows_key_is_still_swallowed()
    {
        Assert.Equal(
            [(false, null), (true, InputSwitchCommand.Open), (false, InputSwitchCommand.Commit), (true, null)],
            Feed((Win, true), (Space, true), (Win, false), (Space, false)));
    }
}
