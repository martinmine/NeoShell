namespace NeoShell.Snap;

/// <summary>
/// Spots Win+arrow and Win+Shift+arrow keys, Snap's shortcuts, in a stream of key presses: once per press, held or not.
/// </summary>
/// <remarks>
/// Windows keeps these registered as hotkeys after Explorer has gone, so NeoShell can't register them; it takes them from
/// the keyboard hook instead, and swallows the arrow (down, repeats and up). Some Windows still does itself (Win+Shift+
/// Left/Right move a window to another monitor, Win+Shift+Down restores a maximized one): those pass unless
/// <paramref name="takes"/> says NeoShell does them, for a window it snapped. With Ctrl or Alt they're other shortcuts,
/// and pass.
/// </remarks>
/// <param name="takes">Whether NeoShell does what a shortcut does, now; it's asked as the arrow goes down.</param>
public sealed class SnapKeys(Func<SnapKey, bool> takes)
{
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;

    private bool _winDown;
    private readonly HashSet<int> _shiftDown = [];
    private readonly HashSet<int> _modifiersDown = [];
    private int? _swallowing;

    /// <summary>Feeds one key event; returns true to swallow it, with the arrow when it completes a shortcut.</summary>
    public bool OnKey(int virtualKey, bool down, out SnapKey? key)
    {
        key = null;
        if (virtualKey is VK_LWIN or VK_RWIN)
        {
            _winDown = down;
            return false;
        }
        // Shift, Ctrl and Alt, either side or not.
        if (virtualKey is 0x10 or 0xA0 or 0xA1 or 0x11 or 0x12 or (>= 0xA2 and <= 0xA5))
        {
            HashSet<int> keys = virtualKey is 0x10 or 0xA0 or 0xA1 ? _shiftDown : _modifiersDown;
            if (down)
                keys.Add(virtualKey);
            else
                keys.Remove(virtualKey);
            return false;
        }

        if (virtualKey == _swallowing)
        {
            if (!down)
                _swallowing = null;
            return true;
        }
        if (!down || !_winDown || _modifiersDown.Count > 0)
            return false;

        bool shift = _shiftDown.Count > 0;
        SnapKey? pressed = virtualKey switch
        {
            VK_LEFT => shift ? SnapKey.ShiftLeft : SnapKey.Left,
            VK_RIGHT => shift ? SnapKey.ShiftRight : SnapKey.Right,
            VK_UP => shift ? SnapKey.ShiftUp : SnapKey.Up,
            VK_DOWN => shift ? SnapKey.ShiftDown : SnapKey.Down,
            _ => null,
        };
        if (pressed is not { } shortcut || !takes(shortcut))
            return false;
        key = shortcut;
        _swallowing = virtualKey;
        return true;
    }
}
