using Microsoft.UI.Xaml;
using Windows.UI;

namespace NeoShell.Tests;

public sealed class SystemThemeTests
{
    // Windows' default blue: lighter shades, the accent (0063B1), darker shades, unused.
    private static readonly byte[] Palette =
    [
        0x9C, 0xEB, 0xFF, 0x00, 0x40, 0xBD, 0xFF, 0x00, 0x00, 0x7F, 0xDC, 0x00, 0x00, 0x63, 0xB1, 0x00,
        0x00, 0x55, 0xA1, 0x00, 0x00, 0x33, 0x7C, 0x00, 0x00, 0x14, 0x5A, 0x00, 0x00, 0xCC, 0x6A, 0x00,
    ];

    [Fact]
    public void Accent_shown_on_taskbar_is_the_second_darker_shade()
    {
        Assert.Equal(Color.FromArgb(255, 0x00, 0x33, 0x7C), SystemTheme.ParseAccent(1, Palette));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    [InlineData("1")]
    public void No_accent_unless_color_prevalence_is_on(object? prevalence)
    {
        Assert.Null(SystemTheme.ParseAccent(prevalence, Palette));
    }

    [Fact]
    public void No_accent_without_a_full_palette()
    {
        Assert.Null(SystemTheme.ParseAccent(1, null));
        Assert.Null(SystemTheme.ParseAccent(1, Palette[..20]));
    }

    [Fact]
    public void Text_contrasts_with_the_accent()
    {
        Assert.Equal(ElementTheme.Dark, SystemTheme.ThemeOn(Color.FromArgb(255, 0x00, 0x33, 0x7C)));
        Assert.Equal(ElementTheme.Light, SystemTheme.ThemeOn(Color.FromArgb(255, 0xF7, 0xD0, 0x4A)));
    }
}
