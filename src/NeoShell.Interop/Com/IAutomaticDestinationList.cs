using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// The shell's own interfaces to an app's jump list data (windows.storage.dll), undocumented: read from its symbols
// (CAutomaticDestinationList, CDestinationList) and from how Explorer's jump list broker (Windows.Internal.Shell.Broker
// .dll, CJumpViewBroker) calls them. Methods NeoShell never calls are declared without parameters: only their place in
// the vtable matters.

/// <summary>
/// An app's automatic destinations (<c>AutomaticDestinations\&lt;AppID hash&gt;.automaticDestinations-ms</c>): its
/// pinned items and the recent and frequent ones Windows records for it.
/// </summary>
[GeneratedComInterface]
[Guid("e9c5ef8d-fd41-4f72-ba87-eb03bad5817c")]
internal partial interface IAutomaticDestinationList
{
    [PreserveSig]
    int Initialize(
        [MarshalAs(UnmanagedType.LPWStr)] string appId,
        [MarshalAs(UnmanagedType.LPWStr)] string? appPath,
        [MarshalAs(UnmanagedType.LPWStr)] string? reserved);

    [PreserveSig] int HasList();

    /// <param name="listType">0 pinned, 1 recent, 2 frequent (recent and frequent include the pinned items).</param>
    /// <param name="flags">1, as Explorer's broker passes: links (pinned from an app's own categories) too.</param>
    [PreserveSig] int GetList(int listType, int maximum, int flags, in Guid iid, out nint list);

    [PreserveSig] int AddUsagePoint();

    /// <param name="item">An <c>IShellItem</c> or <c>IShellLink</c>.</param>
    /// <param name="index">-1 pins it last, -2 unpins it, 0 or more pins it there (moving a pinned one).</param>
    [PreserveSig] int PinItem(nint item, int index);

    /// <summary>S_OK and the item's place among the pins when it's pinned, E_FAIL when it isn't.</summary>
    [PreserveSig] int IsPinned(nint item, out int index);

    [PreserveSig] int RemoveDestination(nint item);
}

/// <summary>The shell's side of an app's own jump list (<c>ICustomDestinationList</c>): what's removed from it.</summary>
[GeneratedComInterface]
[Guid("507101cd-f6ad-46c8-8e20-eeb9e6bac47f")]
internal partial interface IInternalCustomDestinationList
{
    [PreserveSig] int SetMinItems();
    [PreserveSig] int SetApplicationID([MarshalAs(UnmanagedType.LPWStr)] string appId);
    [PreserveSig] int GetSlotCount();
    [PreserveSig] int GetCategoryCount();
    [PreserveSig] int GetCategory();
    [PreserveSig] int DeleteCategory();
    [PreserveSig] int EnumerateCategoryDestinations();

    /// <summary>Takes the link out of the list and adds it to what the app gets from <c>GetRemovedDestinations</c>.</summary>
    [PreserveSig] int RemoveDestination(nint item);
}
