namespace NeoShell;

/// <summary>
/// Spots Win+Comma, Explorer's peek at the desktop, in a stream of key presses: the peek starts with the comma and
/// lasts while the Windows key is held.
/// </summary>
/// <remarks>The comma is swallowed (down, repeats and up), so the app in front doesn't type it.</remarks>
public sealed class PeekKeys
{
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_OEM_COMMA = 0xBC;

    private bool _winDown;
    private bool _peeking;
    private bool _swallowing;

    /// <summary>
    /// Feeds one key event; returns true to swallow it, with <paramref name="peek"/> true when the peek starts and
    /// false when it ends.
    /// </summary>
    public bool OnKey(int virtualKey, bool down, out bool? peek)
    {
        peek = null;
        if (virtualKey is VK_LWIN or VK_RWIN)
        {
            _winDown = down;
            if (!down && _peeking)
            {
                _peeking = false;
                peek = false;
            }
            return false;
        }

        if (virtualKey != VK_OEM_COMMA)
            return false;
        if (!down)
        {
            bool swallowed = _swallowing;
            _swallowing = false;
            return swallowed;
        }
        if (!_winDown)
            return false;

        _swallowing = true;
        if (!_peeking)
        {
            _peeking = true;
            peek = true;
        }
        return true;
    }
}
