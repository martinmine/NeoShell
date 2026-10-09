namespace NeoShell.Interop.Shell;

/// <summary>
/// How many entries each part of a jump list shows, as Explorer's jump list broker shares them out
/// (<c>CJumpViewBroker::EnumList</c>, <c>GetCustomCategories</c>): at most <c>JumpListItems_Maximum</c> entries in all,
/// taken by the pinned ones first, then by the app's own categories in their order, where a Recent or Frequent the app
/// asked for (<c>AppendKnownCategory</c>) takes one too; what's left goes to its Recent and Frequent (shared out evenly,
/// the first getting the odd one). Tasks don't count.
/// </summary>
internal static class JumpListBudget
{
    /// <summary>Explorer's maximum when <c>JumpListItems_Maximum</c> isn't set.</summary>
    public const int DefaultMaximum = 13;

    /// <summary>The highest <c>JumpListItems_Maximum</c> Explorer takes.</summary>
    public const int HighestMaximum = 60;

    /// <summary>The maximum Explorer uses for a <c>JumpListItems_Maximum</c> setting (null when it isn't set).</summary>
    public static int Maximum(int? setting) => setting is { } value and >= 0 ? Math.Min(value, HighestMaximum) : DefaultMaximum;

    /// <summary>How many entries the pinned ones and each category show.</summary>
    /// <param name="categories">Each category's entries, or null for a known category (Recent, Frequent): as many as are left.</param>
    /// <param name="knownAskedFor">Whether the app asked for its known categories (not the Recent an app without
    /// categories of its own gets), which then take one each.</param>
    public static (int Pinned, int[] Categories) Split(int maximum, int pinned, IReadOnlyList<int?> categories, bool knownAskedFor)
    {
        int left = Math.Max(0, maximum);
        int shownPinned = Math.Min(pinned, left);
        left -= shownPinned;

        int[] shown = new int[categories.Count];
        for (int i = 0; i < categories.Count; i++)
        {
            if (categories[i] is { } count)
            {
                shown[i] = Math.Min(count, left);
                left -= shown[i];
            }
            else if (knownAskedFor && left > 0)
            {
                left--;
            }
        }

        int known = categories.Count(count => count is null);
        int k = 0;
        for (int i = 0; i < categories.Count; i++)
        {
            if (categories[i] is null)
                shown[i] = left / known + (k++ < left % known ? 1 : 0);
        }
        return (shownPinned, shown);
    }
}
