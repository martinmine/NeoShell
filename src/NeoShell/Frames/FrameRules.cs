using NeoShell.Interop.Windowing;
using NeoShell.Settings;

namespace NeoShell.Frames;

/// <summary>Which windows get a frame style, and which one.</summary>
public static class FrameRules
{
    private const uint WS_CAPTION = 0x00C0_0000; // WS_BORDER | WS_DLGFRAME
    private const uint WS_CHILD = 0x4000_0000;
    private const uint WS_EX_TRANSPARENT = 0x0000_0020;
    private const uint WS_EX_NOACTIVATE = 0x0800_0000;

    /// <summary>
    /// The name of the style for a window of <paramref name="processName"/> and <paramref name="className"/>: the most
    /// specific rule that matches (process and class over process alone, the first of equals), else
    /// <paramref name="globalStyle"/>. Null when the matching rule says to leave the window alone.
    /// </summary>
    public static string? StyleFor(IReadOnlyList<FrameRule> rules, string globalStyle, string? processName, string className)
    {
        FrameRule? match = rules.FirstOrDefault(rule => MatchesProcess(rule, processName)
                && rule.ClassName is { Length: > 0 } ruleClass && string.Equals(ruleClass, className, StringComparison.OrdinalIgnoreCase))
            ?? rules.FirstOrDefault(rule => MatchesProcess(rule, processName) && string.IsNullOrEmpty(rule.ClassName));
        return match is null ? globalStyle : match.Style;

        static bool MatchesProcess(FrameRule rule, string? processName) =>
            processName is not null && string.Equals(rule.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a window's frame may be styled, as MicaForEveryone filters them: a visible, uncloaked top-level window
    /// with a title bar (tool windows and popups included when they have one), that takes activation and the pointer,
    /// and isn't NeoShell's own.
    /// </summary>
    public static bool IsEligible(WindowFrame.Target window, int ownProcessId) =>
        window.ProcessId != ownProcessId
        && window.IsVisible
        && !window.IsCloaked
        && (window.Style & WS_CHILD) == 0
        && (window.Style & WS_CAPTION) == WS_CAPTION
        && (window.ExStyle & (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)) == 0;
}
