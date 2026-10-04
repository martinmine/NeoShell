using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace NeoShell.StartMenu;

/// <summary>A corner to drag for resizing, with the resize pointer over it.</summary>
internal sealed partial class ResizeGrip : Grid
{
    /// <summary>The top-left corner; otherwise the top-right one.</summary>
    public bool IsLeft
    {
        get;
        set
        {
            field = value;
            ProtectedCursor = InputSystemCursor.Create(value ? InputSystemCursorShape.SizeNorthwestSoutheast : InputSystemCursorShape.SizeNortheastSouthwest);
        }
    }

    public ResizeGrip() => IsLeft = false;
}
