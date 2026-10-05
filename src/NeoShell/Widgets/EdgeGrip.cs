using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace NeoShell.Widgets;

/// <summary>An edge to drag for resizing sideways, with the resize pointer over it.</summary>
internal sealed partial class EdgeGrip : Grid
{
    public EdgeGrip() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}
