using NeoShell.Settings;

namespace NeoShell.Frames;

/// <summary>The read-only styles NeoShell comes with; "Duplicate" in the window frames window makes an editable copy.</summary>
public static class FramePresets
{
    public const string WindowsDefault = "Windows default";

    public static readonly IReadOnlyList<FrameStyle> All =
    [
        new() { Name = WindowsDefault },
        new() { Name = "Mica", Backdrop = FrameBackdrop.Mica },
        new() { Name = "Mica Alt", Backdrop = FrameBackdrop.MicaAlt },
        new() { Name = "Acrylic", Backdrop = FrameBackdrop.Acrylic },
        new() { Name = "Dark", Theme = FrameTheme.Dark },
        new() { Name = "Light", Theme = FrameTheme.Light },
        // Windows 10's "Show accent colour on title bars", per app.
        new() { Name = "Accent", CaptionColor = FrameColors.Accent, TextColor = FrameColors.Contrast, BorderColor = FrameColors.Accent },
        // The nearest public look to Aero glass: blurred and see-through while active, but no tint, glow or Vista's
        // buttons, and grey when inactive (DWM draws Acrylic only on the active window).
        new() { Name = "Glass (Vista-like)", Backdrop = FrameBackdrop.Acrylic, Theme = FrameTheme.Dark, BorderColor = FrameColors.Accent },
        // XP's blue scheme from luna.msstyles: ActiveCaption 0,84,227 and white caption text; the border is the dark
        // blue of its frame. Flat: DWM takes one colour, so no gradient, and the same colour when inactive.
        new() { Name = "Luna (XP-like)", CaptionColor = "#0054E3", TextColor = "#FFFFFF", BorderColor = "#0831D9", Corners = FrameCorners.SmallRound },
        new() { Name = "Windows 7 Basic", BasicFrame = true },
    ];

    public static bool IsPreset(string name) => All.Any(preset => preset.Name == name);

    /// <summary>The preset or user style called <paramref name="name"/>, or null.</summary>
    public static FrameStyle? Find(string? name, IReadOnlyList<FrameStyle> userStyles) =>
        All.FirstOrDefault(style => style.Name == name) ?? userStyles.FirstOrDefault(style => style.Name == name);

    /// <summary>A name for a copy of <paramref name="name"/> that no style has yet: "Luna (XP-like) copy", "… copy 2".</summary>
    public static string CopyName(string name, IReadOnlyList<FrameStyle> userStyles)
    {
        string copy = $"{name} copy";
        for (int number = 2; Find(copy, userStyles) is not null; number++)
            copy = $"{name} copy {number}";
        return copy;
    }
}
