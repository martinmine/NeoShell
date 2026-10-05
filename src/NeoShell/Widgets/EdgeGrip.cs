using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace NeoShell.Widgets;

/// <summary>An edge to drag for resizing sideways, with the resize pointer over it.</summary>
internal sealed partial class EdgeGrip : Grid
{
    public EdgeGrip() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}

/// <summary>A bottom-right corner to drag for resizing, with the resize pointer over it.</summary>
internal sealed partial class CornerGrip : Grid
{
    public CornerGrip() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthwestSoutheast);
}
