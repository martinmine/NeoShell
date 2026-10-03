using System.Data.OleDb;
using System.Runtime.InteropServices;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Search;

/// <summary>A file or folder found by Windows Search.</summary>
/// <param name="Kinds">System.Kind values, such as "folder", "document" or "picture".</param>
public sealed record IndexResult(string Name, string Path, IReadOnlyList<string> Kinds);

/// <summary>
/// Searches the Windows Search index: <c>ISearchQueryHelper</c> turns what the user typed into SQL, which runs
/// through OLE DB against <c>Search.CollatorDSO</c>. Blocking: call it off the UI thread.
/// </summary>
public static unsafe class IndexSearch
{
    internal const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows'";
    internal const string SelectColumns = "System.ItemNameDisplay,System.ItemPathDisplay,System.Kind";
    // Words match names, as in the Windows search box; matching file contents buries what the user means.
    internal const string ContentProperties = "System.ItemNameDisplay";
    // Files and folders only: no e-mail or other stores. Shortcuts are left out: Start lists the apps they open.
    internal const string WhereRestrictions = "AND SCOPE='file:' AND System.FileExtension <> '.lnk'";

    private const int SEARCH_PREFIX_EXPANSION = 1;
    private static readonly Guid CLSID_CSearchManager = new("7d096c5f-ac08-4f1f-beb7-5c22c517ce39");

    public static IReadOnlyList<IndexResult> Search(string query, int maxResults, CancellationToken cancellation)
    {
        query = Normalize(query);
        if (query.Length == 0)
            return [];

        string sql = BuildSql(query, maxResults);
        cancellation.ThrowIfCancellationRequested();

        using var connection = new OleDbConnection(ConnectionString);
        connection.Open();
        using var command = new OleDbCommand(sql, connection);
        using CancellationTokenRegistration registration = cancellation.Register(command.Cancel);
        using OleDbDataReader reader = command.ExecuteReader();

        var results = new List<IndexResult>();
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                continue;
            results.Add(new IndexResult(reader.GetString(0), reader.GetString(1), ReadKinds(reader.GetValue(2))));
        }
        return results;
    }

    /// <summary>Collapses whitespace; the query helper does the rest (keywords, quoting, prefix matching).</summary>
    internal static string Normalize(string query) =>
        string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static IReadOnlyList<string> ReadKinds(object value) => value switch
    {
        string kind => [kind],
        string[] kinds => kinds,
        object[] kinds => [.. kinds.OfType<string>()],
        _ => [],
    };

    private static string BuildSql(string query, int maxResults)
    {
        Guid iid = typeof(ISearchManager).GUID;
        Marshal.ThrowExceptionForHR(Ole32.CoCreateInstance(
            CLSID_CSearchManager, 0, Ole32.CLSCTX_LOCAL_SERVER | Ole32.CLSCTX_INPROC_SERVER, iid, out ISearchManager manager));
        Marshal.ThrowExceptionForHR(manager.GetCatalog("SystemIndex", out ISearchCatalogManager catalog));
        Marshal.ThrowExceptionForHR(catalog.GetQueryHelper(out ISearchQueryHelper helper));

        Marshal.ThrowExceptionForHR(helper.put_QuerySelectColumns(SelectColumns));
        Marshal.ThrowExceptionForHR(helper.put_QueryContentProperties(ContentProperties));
        Marshal.ThrowExceptionForHR(helper.put_QueryWhereRestrictions(WhereRestrictions));
        // "repo" finds "report.docx", as the Windows search box does.
        Marshal.ThrowExceptionForHR(helper.put_QueryTermExpansion(SEARCH_PREFIX_EXPANSION));
        Marshal.ThrowExceptionForHR(helper.put_QueryMaxResults(maxResults));

        char* sql;
        Marshal.ThrowExceptionForHR(helper.GenerateSQLFromUserQuery(query, &sql));
        try
        {
            return new string(sql);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)sql);
        }
    }
}
