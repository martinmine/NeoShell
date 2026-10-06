using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Shell;

/// <summary>What a drop does, as OLE's <c>DROPEFFECT</c>.</summary>
[Flags]
public enum DropEffect { None = 0, Copy = 1, Move = 2, Link = 4 }

/// <summary>
/// Drops on the desktop's window, as on Explorer's desktop: whatever is dropped on an icon (a folder, the Recycle Bin,
/// an app) or on the desktop itself goes to the shell's own drop target, so moving, copying and linking (by the keys
/// held), confirmations and progress are Explorer's. The app only says which icon is where.
/// </summary>
/// <remarks>
/// Drags from other apps arrive through a native OLE drop target, registered on WinUI's content window: the window
/// OLE finds under the pointer, and it looks no further. The shell's targets need the drag's own data object, which
/// WinUI's drop events don't give. WinUI's own drags don't reach OLE targets in the same process, so the app passes
/// drags of the desktop's own items on from WinUI's drop events (<see cref="OwnDragOver"/>), and the shell's data
/// object for them stands in. UI thread only: OLE calls the drop target on the thread that registered it.
/// </remarks>
public sealed unsafe partial class DesktopDragDrop : IDisposable
{
    private const uint MK_LBUTTON = 0x1;
    private const uint MK_SHIFT = 0x4;
    private const uint MK_CONTROL = 0x8;
    private const uint MK_ALT = 0x20;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const uint AllEffects = (uint)(DropEffect.Copy | DropEffect.Move | DropEffect.Link);

    private static readonly Guid DragDropHelper = new("4657278a-411b-11d2-839a-00c04fd918d0");

    private readonly nint _owner;
    private readonly nint _target;
    private readonly Func<int, int, DesktopItem?> _itemAt;
    private readonly DropTarget _dropTarget;
    private readonly IDropTargetHelper? _helper;
    private nint _ownData;
    private bool _ownDragEntered;

    /// <param name="owner">The desktop's window, which owns the shell's dialogs.</param>
    /// <param name="itemAt">The icon at a point on the screen (physical pixels), or null for the desktop itself.</param>
    public DesktopDragDrop(nint owner, Func<int, int, DesktopItem?> itemAt)
    {
        _owner = owner;
        _itemAt = itemAt;
        _dropTarget = new DropTarget(this);
        try
        {
            _helper = Ole32.Create<IDropTargetHelper>(DragDropHelper, Ole32.CLSCTX_INPROC_SERVER);
        }
        catch (COMException)
        {
            _helper = null; // drops still work, without the drag image over the desktop
        }

        _target = User32.FindWindowEx(owner, 0, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
        if (_target == 0)
            _target = owner;
        void* target = ComInterfaceMarshaller<IDropTarget>.ConvertToUnmanaged(_dropTarget);
        try
        {
            Marshal.ThrowExceptionForHR(Ole32.RegisterDragDrop(_target, (nint)target));
        }
        finally
        {
            ComInterfaceMarshaller<IDropTarget>.Free(target);
        }
    }

    /// <summary>The icon a drag is over and would drop on, or null; for its highlight.</summary>
    public event Action<DesktopItem?>? TargetChanged;

    /// <summary>
    /// The desktop's own items (<see cref="BeginOwnDrag"/>) were let go over the desktop itself, not on an icon that
    /// takes them, at this point on the screen: nothing moves on disk, only their places on the desktop.
    /// </summary>
    public event Action<int, int>? OwnItemsDropped;

    /// <summary>Items from elsewhere were dropped on the desktop itself at this point: the shell puts them in the Desktop folder.</summary>
    public event Action<int, int>? ItemsDropped;

    /// <summary>The desktop's own items while the app drags them, or null.</summary>
    public IReadOnlyList<DesktopItem>? OwnItems { get; private set; }

    /// <summary>Starts a drag of the desktop's own items; the app's drop events then go to <see cref="OwnDragOver"/> and the like.</summary>
    public void BeginOwnDrag(IReadOnlyList<DesktopItem> items)
    {
        EndOwnDrag();
        OwnItems = items;
        _ownData = CreateDataObject(items);
    }

    /// <summary>The desktop's own items are dragged over this point on the screen; returns what a drop there would do.</summary>
    public DropEffect OwnDragOver(int x, int y)
    {
        if (_ownData == 0)
            return DropEffect.None;

        var point = new User32.POINT { x = x, y = y };
        uint effect = AllEffects;
        if (_ownDragEntered)
        {
            _dropTarget.Over(KeyState(), point, &effect);
        }
        else
        {
            _ownDragEntered = true;
            _dropTarget.Enter(_ownData, KeyState(), point, &effect);
        }
        return (DropEffect)effect;
    }

    /// <summary>The drag of the desktop's own items left the desktop.</summary>
    public void OwnDragLeave()
    {
        if (_ownDragEntered)
            _dropTarget.Leave();
        _ownDragEntered = false;
    }

    /// <summary>The desktop's own items were let go at this point on the screen; returns what the drop did.</summary>
    public DropEffect OwnDrop(int x, int y)
    {
        if (_ownData == 0)
            return DropEffect.None;

        var point = new User32.POINT { x = x, y = y };
        uint effect = AllEffects;
        if (!_ownDragEntered)
            _dropTarget.Enter(_ownData, KeyState(), point, &effect);
        _ownDragEntered = false;
        effect = AllEffects;
        _dropTarget.Drop(_ownData, KeyState(), point, &effect);
        return (DropEffect)effect;
    }

    /// <summary>Ends the drag of the desktop's own items, wherever it went.</summary>
    public void EndOwnDrag()
    {
        OwnDragLeave();
        OwnItems = null;
        if (_ownData != 0)
            Marshal.Release(_ownData);
        _ownData = 0;
    }

    public void Dispose()
    {
        EndOwnDrag();
        Ole32.RevokeDragDrop(_target);
        _dropTarget.Leave();
    }

    /// <summary>The keys held, as OLE passes them to drop targets: Shift moves, Ctrl copies, Alt links.</summary>
    private static uint KeyState()
    {
        static bool IsDown(int key) => (User32.GetAsyncKeyState(key) & 0x8000) != 0;
        return MK_LBUTTON
            | (IsDown(VK_SHIFT) ? MK_SHIFT : 0)
            | (IsDown(VK_CONTROL) ? MK_CONTROL : 0)
            | (IsDown(VK_MENU) ? MK_ALT : 0);
    }

    /// <summary>The shell's data object for the items: their ID lists, file names and whatever else apps look for.</summary>
    private nint CreateDataObject(IReadOnlyList<DesktopItem> items)
    {
        IShellFolder desktop = DesktopFolder.Open();
        nint* idLists = stackalloc nint[items.Count];
        for (int i = 0; i < items.Count; i++)
            idLists[i] = items[i].ToNative();
        try
        {
            Guid iid = new("0000010e-0000-0000-c000-000000000046"); // IDataObject
            nint dataObject;
            return desktop.GetUIObjectOf(_owner, (uint)items.Count, idLists, &iid, null, &dataObject) == 0 ? dataObject : 0;
        }
        finally
        {
            for (int i = 0; i < items.Count; i++)
                Marshal.FreeCoTaskMem(idLists[i]);
        }
    }

    /// <summary>The shell's drop target for an icon, or for the desktop itself (the Desktop folder) when it's null.</summary>
    private IDropTarget? ShellDropTarget(DesktopItem? item)
    {
        IShellFolder desktop = DesktopFolder.Open();
        Guid iid = typeof(IDropTarget).GUID;
        nint target;
        if (item is null)
            return desktop.CreateViewObject(_owner, &iid, &target) == 0 ? ComPointer.TakeOwnership<IDropTarget>(target) : null;

        nint idList = item.ToNative();
        try
        {
            return desktop.GetUIObjectOf(_owner, 1, &idList, &iid, null, &target) == 0 ? ComPointer.TakeOwnership<IDropTarget>(target) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(idList);
        }
    }

    /// <summary>
    /// The desktop's drop target. Each icon a drag passes over that takes drops gets the drag from the shell's target
    /// for it; elsewhere the desktop's own target (the Desktop folder) does, except for the desktop's own items, which
    /// only change place. OLE calls the interface for drags from other apps, which also get the drag image drawn over
    /// the desktop; the app calls <see cref="Enter"/> and the rest for its own (WinUI draws those).
    /// </summary>
    [GeneratedComClass]
    private sealed partial class DropTarget(DesktopDragDrop owner) : IDropTarget
    {
        private nint _dataObject;
        private DesktopItem? _item;
        private IDropTarget? _shellTarget;
        private bool _tracking;

        private bool IsOwnDrag => owner.OwnItems is not null;

        int IDropTarget.DragEnter(nint dataObject, uint keyState, User32.POINT point, uint* effect)
        {
            try
            {
                Enter(dataObject, keyState, point, effect);
                owner._helper?.DragEnter(owner._target, dataObject, &point, *effect);
            }
            catch (Exception ex)
            {
                Fail(ex, effect);
            }
            return 0;
        }

        int IDropTarget.DragOver(uint keyState, User32.POINT point, uint* effect)
        {
            try
            {
                Over(keyState, point, effect);
                owner._helper?.DragOver(&point, *effect);
            }
            catch (Exception ex)
            {
                Fail(ex, effect);
            }
            return 0;
        }

        int IDropTarget.DragLeave()
        {
            try
            {
                owner._helper?.DragLeave();
                Leave();
            }
            catch (Exception ex)
            {
                NativeCallback.Report(ex);
            }
            return 0;
        }

        int IDropTarget.Drop(nint dataObject, uint keyState, User32.POINT point, uint* effect)
        {
            try
            {
                uint allowed = *effect;
                Over(keyState, point, effect);
                owner._helper?.Drop(dataObject, &point, *effect);
                *effect = allowed;
                Drop(dataObject, keyState, point, effect);
            }
            catch (Exception ex)
            {
                Fail(ex, effect);
            }
            return 0;
        }

        /// <summary>A drag came over the desktop.</summary>
        /// <param name="effect">What the source allows; on return, what a drop would do.</param>
        public void Enter(nint dataObject, uint keyState, User32.POINT point, uint* effect)
        {
            Leave();
            Marshal.AddRef(dataObject);
            _dataObject = dataObject;
            Over(keyState, point, effect);
        }

        /// <summary>Follows the drag to the icon under it, and says what a drop there would do.</summary>
        /// <param name="effect">What the source allows; on return, what a drop would do.</param>
        public void Over(uint keyState, User32.POINT point, uint* effect)
        {
            DesktopItem? item = owner._itemAt(point.x, point.y);
            // Over a file that takes no drops, or over a dragged item itself, it's the desktop's.
            if (item is not null && (!item.IsDropTarget || owner.OwnItems?.Any(own => own.ParsingName == item.ParsingName) == true))
                item = null;

            if (!_tracking || item?.ParsingName != _item?.ParsingName)
            {
                if (_shellTarget is { } previous)
                {
                    _shellTarget = null;
                    previous.DragLeave();
                }
                _tracking = true;
                _item = item;
                owner.TargetChanged?.Invoke(item);

                // The desktop's own items over the desktop itself only move around on it.
                _shellTarget = item is null && IsOwnDrag ? null : owner.ShellDropTarget(item);
                if (_shellTarget is not null)
                {
                    _shellTarget.DragEnter(_dataObject, keyState, point, effect);
                    return;
                }
            }

            if (_shellTarget is not null)
                _shellTarget.DragOver(keyState, point, effect);
            else
                *effect = item is null && IsOwnDrag ? *effect & (uint)DropEffect.Move : 0;
        }

        /// <summary>The drag was let go: the shell's target takes it, or the desktop's own items change place.</summary>
        /// <param name="effect">What the source allows; on return, what the drop did.</param>
        public void Drop(nint dataObject, uint keyState, User32.POINT point, uint* effect)
        {
            try
            {
                Over(keyState, point, effect);
                if (_shellTarget is { } target)
                {
                    // The shell's target takes the drop instead of a DragLeave, and runs the file operation.
                    _shellTarget = null;
                    target.Drop(dataObject, keyState, point, effect);
                    if (_item is null && *effect != 0)
                        owner.ItemsDropped?.Invoke(point.x, point.y);
                }
                else if (IsOwnDrag && _item is null)
                {
                    owner.OwnItemsDropped?.Invoke(point.x, point.y);
                }
            }
            finally
            {
                Leave();
            }
        }

        /// <summary>Ends the drag over the desktop: lets go of the shell's target, the data and the highlight.</summary>
        public void Leave()
        {
            if (_shellTarget is { } target)
            {
                _shellTarget = null;
                target.DragLeave();
            }
            if (_item is not null)
                owner.TargetChanged?.Invoke(null);
            _item = null;
            _tracking = false;
            if (_dataObject != 0)
                Marshal.Release(_dataObject);
            _dataObject = 0;
        }

        /// <summary>OLE must not see exceptions; a failed call takes no drop.</summary>
        private static void Fail(Exception exception, uint* effect)
        {
            NativeCallback.Report(exception);
            *effect = 0;
        }
    }
}
