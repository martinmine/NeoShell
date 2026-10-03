namespace NeoShell.StartMenu;

/// <summary>Ranks apps by how well their name matches what the user typed.</summary>
public static class AppSearch
{
    private static readonly char[] s_wordSeparators = [' ', '-', '_', '.', '(', ')', '&', '+'];

    /// <summary>Matching items, best first; equally good matches are alphabetical.</summary>
    public static IReadOnlyList<T> Rank<T>(IEnumerable<T> items, Func<T, string> name, string query) =>
        [.. items
            .Select(item => (Item: item, Score: Score(name(item), query)))
            .Where(match => match.Score >= 0)
            .OrderBy(match => match.Score)
            .ThenBy(match => name(match.Item), StringComparer.CurrentCultureIgnoreCase)
            .Select(match => match.Item)];

    /// <summary>
    /// 0 exact, 1 the name starts with the query, 2 every query word starts a word of the name ("studio code" finds
    /// "Visual Studio Code"), 3 the name contains the query; -1 no match. Case-insensitive.
    /// </summary>
    public static int Score(string name, string query)
    {
        query = query.Trim();
        if (query.Length == 0)
            return -1;
        if (name.Equals(query, StringComparison.CurrentCultureIgnoreCase))
            return 0;
        if (name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            return 1;

        string[] words = name.Split(s_wordSeparators, StringSplitOptions.RemoveEmptyEntries);
        string[] terms = query.Split(s_wordSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 0 && terms.All(term => words.Any(word => word.StartsWith(term, StringComparison.CurrentCultureIgnoreCase))))
            return 2;
        if (name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            return 3;
        return -1;
    }
}
