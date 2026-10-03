using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// Windows Search (searchapi.h). Methods NeoShell never calls are declared without parameters: only their place in
// the vtable matters.

[GeneratedComInterface]
[Guid("ab310581-ac80-11d1-8df3-00c04fb6ef69")]
internal partial interface ISearchManager
{
    [PreserveSig] int GetIndexerVersionStr();
    [PreserveSig] int GetIndexerVersion();
    [PreserveSig] int GetParameter();
    [PreserveSig] int SetParameter();
    [PreserveSig] int get_ProxyName();
    [PreserveSig] int get_BypassList();
    [PreserveSig] int SetProxy();
    [PreserveSig] int GetCatalog([MarshalAs(UnmanagedType.LPWStr)] string catalog, out ISearchCatalogManager manager);
}

[GeneratedComInterface]
[Guid("ab310581-ac80-11d1-8df3-00c04fb6ef50")]
internal partial interface ISearchCatalogManager
{
    [PreserveSig] int get_Name();
    [PreserveSig] int GetParameter();
    [PreserveSig] int SetParameter();
    [PreserveSig] int GetCatalogStatus();
    [PreserveSig] int Reset();
    [PreserveSig] int Reindex();
    [PreserveSig] int ReindexMatchingURLs();
    [PreserveSig] int ReindexSearchRoot();
    [PreserveSig] int put_ConnectTimeout();
    [PreserveSig] int get_ConnectTimeout();
    [PreserveSig] int put_DataTimeout();
    [PreserveSig] int get_DataTimeout();
    [PreserveSig] int NumberOfItems();
    [PreserveSig] int NumberOfItemsToIndex();
    [PreserveSig] int URLBeingIndexed();
    [PreserveSig] int GetURLIndexingState();
    [PreserveSig] int GetPersistentItemsChangedSink();
    [PreserveSig] int RegisterViewForNotification();
    [PreserveSig] int GetItemsChangedSink();
    [PreserveSig] int UnregisterViewForNotification();
    [PreserveSig] int SetExtensionClusion();
    [PreserveSig] int EnumerateExcludedExtensions();
    [PreserveSig] int GetQueryHelper(out ISearchQueryHelper helper);
}

[GeneratedComInterface]
[Guid("ab310581-ac80-11d1-8df3-00c04fb6ef63")]
internal unsafe partial interface ISearchQueryHelper
{
    [PreserveSig] int get_ConnectionString(char** connectionString);
    [PreserveSig] int put_QueryContentLocale(uint lcid);
    [PreserveSig] int get_QueryContentLocale();
    [PreserveSig] int put_QueryKeywordLocale(uint lcid);
    [PreserveSig] int get_QueryKeywordLocale();
    [PreserveSig] int put_QueryTermExpansion(int expansion);
    [PreserveSig] int get_QueryTermExpansion();
    [PreserveSig] int put_QuerySyntax(int syntax);
    [PreserveSig] int get_QuerySyntax();
    [PreserveSig] int put_QueryContentProperties([MarshalAs(UnmanagedType.LPWStr)] string properties);
    [PreserveSig] int get_QueryContentProperties();
    [PreserveSig] int put_QuerySelectColumns([MarshalAs(UnmanagedType.LPWStr)] string columns);
    [PreserveSig] int get_QuerySelectColumns();
    [PreserveSig] int put_QueryWhereRestrictions([MarshalAs(UnmanagedType.LPWStr)] string restrictions);
    [PreserveSig] int get_QueryWhereRestrictions();
    [PreserveSig] int put_QuerySorting([MarshalAs(UnmanagedType.LPWStr)] string sorting);
    [PreserveSig] int get_QuerySorting();
    [PreserveSig] int GenerateSQLFromUserQuery([MarshalAs(UnmanagedType.LPWStr)] string query, char** sql);
    [PreserveSig] int WriteProperties();
    [PreserveSig] int put_QueryMaxResults(int maxResults);
}
