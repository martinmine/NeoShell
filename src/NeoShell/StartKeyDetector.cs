namespace NeoShell;

/// <summary>
/// Spots the keys that open Start in a stream of key presses: the Windows key pressed and released on its own, or
/// Ctrl+Esc. Win+D and other Win combinations don't count.
/// </summary>
public sealed class StartKeyDetector
{
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;

    private bool _winDown;
    private bool _winAlone;
    private bool _controlDown;

    /// <summary>Feeds one key event; returns true when it completes a gesture that opens Start.</summary>
    public bool OnKey(int virtualKey, bool down)
    {
        switch (virtualKey)
        {
            case VK_LWIN or VK_RWIN when down:
                // Key repeat keeps sending "down"; only the first starts a press.
                if (!_winDown)
                {
                    _winDown = true;
                    _winAlone = true;
                }
                return false;
            case VK_LWIN or VK_RWIN:
                bool alone = _winDown && _winAlone;
                _winDown = false;
                return alone;
            case VK_CONTROL or VK_LCONTROL or VK_RCONTROL:
                _controlDown = down;
                _winAlone = false;
                return false;
        }

        if (!down)
            return false;
        _winAlone = false;
        return virtualKey == VK_ESCAPE && _controlDown;
    }
}
