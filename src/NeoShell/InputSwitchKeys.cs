namespace NeoShell;

public enum InputSwitchCommand
{
    /// <summary>Show the switcher with the next input method chosen.</summary>
    Open,
    /// <summary>Show the switcher with the previous input method chosen (Win+Shift+Space).</summary>
    OpenBackwards,
    Next,
    Previous,
    /// <summary>The Windows key was let go of: switch to the chosen input method and hide the switcher.</summary>
    Commit,
}

/// <summary>
/// Spots Win+Space, Explorer's input switcher, in a stream of key presses: each Space while the Windows key is held
/// moves on (back with Shift), and letting go of the Windows key switches.
/// </summary>
/// <remarks>Space is swallowed (down, repeats and up), so the app in front doesn't type it.</remarks>
public sealed class InputSwitchKeys
{
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SPACE = 0x20;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;

    private bool _winDown;
    private bool _shiftDown;
    private bool _controlOrAltDown;
    private bool _open;
    private bool _swallowing;

    /// <summary>Feeds one key event; returns true to swallow it, with what the switcher does in <paramref name="command"/>.</summary>
    public bool OnKey(int virtualKey, bool down, out InputSwitchCommand? command)
    {
        command = null;
        switch (virtualKey)
        {
            case VK_LWIN or VK_RWIN:
                _winDown = down;
                if (!down && _open)
                {
                    _open = false;
                    command = InputSwitchCommand.Commit;
                }
                return false;
            case VK_SHIFT or VK_LSHIFT or VK_RSHIFT:
                _shiftDown = down;
                return false;
            case VK_CONTROL or VK_LCONTROL or VK_RCONTROL or VK_MENU or VK_LMENU or VK_RMENU:
                _controlOrAltDown = down;
                return false;
            case not VK_SPACE:
                return false;
        }

        if (!down)
        {
            bool swallowed = _swallowing;
            _swallowing = false;
            return swallowed;
        }
        if (!_winDown || _controlOrAltDown)
            return false;

        _swallowing = true;
        command = (_open, _shiftDown) switch
        {
            (false, false) => InputSwitchCommand.Open,
            (false, true) => InputSwitchCommand.OpenBackwards,
            (true, false) => InputSwitchCommand.Next,
            (true, true) => InputSwitchCommand.Previous,
        };
        _open = true;
        return true;
    }
}
