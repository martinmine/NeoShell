namespace NeoShell.Snap;

/// <summary>Spots Win+arrow keys, Snap's shortcuts, in a stream of key presses: once per press, held or not.</summary>
/// <remarks>
/// Windows keeps these registered as hotkeys after Explorer has gone, without acting on them, so NeoShell can't
/// register them; it takes them from the keyboard hook instead, and swallows the arrow (down, repeats and up). With
/// Shift, Ctrl or Alt they're other shortcuts, and pass.
/// </remarks>
public sealed class SnapKeys
{
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;

    private bool _winDown;
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
        if (virtualKey is 0x10 or 0x11 or 0x12 or (>= 0xA0 and <= 0xA5))
        {
            if (down)
                _modifiersDown.Add(virtualKey);
            else
                _modifiersDown.Remove(virtualKey);
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

        key = virtualKey switch
        {
            VK_LEFT => SnapKey.Left,
            VK_RIGHT => SnapKey.Right,
            VK_UP => SnapKey.Up,
            VK_DOWN => SnapKey.Down,
            _ => null,
        };
        if (key is null)
            return false;
        _swallowing = virtualKey;
        return true;
    }
}
