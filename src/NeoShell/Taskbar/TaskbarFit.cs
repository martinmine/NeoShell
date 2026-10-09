namespace NeoShell.Taskbar;

/// <summary>
/// How the taskbar makes room as it fills, as Explorer's (measured on 25H2 with buttons never combined): a labelled
/// button is as wide as its label needs, up to <see cref="LabelledMaxWidth"/>. When they don't all fit, they narrow
/// together, each keeping the same share of what it has above <see cref="LabelledMinWidth"/>, their labels cut off.
/// When they don't fit even then, the search box (or the icon and label) gives way to the icon, and they widen again.
/// Explorer then shrinks its icons and finally moves buttons into an overflow menu; NeoShell cuts the row off.
/// </summary>
public static class TaskbarFit
{
    /// <summary>Effective pixels: the narrowest a labelled button goes before the search box collapses.</summary>
    public const double LabelledMinWidth = 84;

    /// <summary>Effective pixels: the widest a labelled button gets, however long its label.</summary>
    public const double LabelledMaxWidth = 180;

    /// <summary>Effective pixels of a labelled button besides its label: 10 before the icon, the icon, 8 after it, 10 after the label.</summary>
    public const double LabelChrome = 10 + 24 + 8 + 10;

    /// <summary>A labelled button's width when there's room for it, from its label's width.</summary>
    public static double NaturalWidth(double labelWidth) => Math.Min(LabelledMaxWidth, LabelChrome + Math.Ceiling(labelWidth));

    /// <summary>The narrowest a button goes: a labelled one to <see cref="LabelledMinWidth"/>, others never narrow.</summary>
    public static double NarrowestWidth(double naturalWidth) => Math.Min(naturalWidth, LabelledMinWidth);

    /// <summary>
    /// The buttons' widths in <paramref name="room"/>, from their natural widths: those, or all narrowed by the same
    /// share of what each has above its narrowest; when even the narrowest don't fit, the narrowest.
    /// </summary>
    public static double[] Widths(IReadOnlyList<double> naturalWidths, double room)
    {
        double natural = naturalWidths.Sum();
        double narrowest = naturalWidths.Sum(NarrowestWidth);
        if (natural <= room)
            return [.. naturalWidths];
        if (narrowest >= room)
            return [.. naturalWidths.Select(NarrowestWidth)];

        double share = (room - narrowest) / (natural - narrowest);
        return [.. naturalWidths.Select(width => NarrowestWidth(width) + (width - NarrowestWidth(width)) * share)];
    }
}
