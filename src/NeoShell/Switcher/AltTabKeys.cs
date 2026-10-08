namespace NeoShell.Switcher;

/// <summary>What a key does to the window switcher.</summary>
public enum SwitcherCommand { Open, OpenBackwards, OpenSticky, OpenStickyBackwards, Next, Previous, Up, Down, Switch, Cancel, CloseWindow }

/// <summary>
/// Spots Alt+Tab in a stream of key presses, then drives the window switcher while Alt is held, as Explorer's does:
/// Tab and Shift+Tab, the arrow keys, Enter (or letting go of Alt) to switch, Esc to cancel and Delete to close the
/// chosen window. Ctrl+Alt+Tab opens it to stay: letting go of the keys switches nothing, Tab moves on without Alt
/// and Space switches too.
/// </summary>
/// <remarks>
/// The switcher's keys are swallowed (down, repeats and up) so neither the app in front nor Windows' own Alt+Tab sees
/// them. Alt itself always passes through: Windows has to know it's down, and it's let go of in the app in front.
/// </remarks>
public sealed class AltTabKeys
{
    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;
    private const int VK_DELETE = 0x2E;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    private readonly HashSet<int> _swallowing = [];
    private bool _altDown;
    private bool _shiftDown;
    private bool _controlDown;
    private bool _winDown;
    private bool _sticky;

    /// <summary>Whether the switcher is up (or about to show), so its keys are taken.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Feeds one key event; returns true to swallow it, with what the switcher should do.</summary>
    public bool OnKey(int virtualKey, bool down, out SwitcherCommand? command)
    {
        command = null;
        switch (virtualKey)
        {
            case VK_MENU or VK_LMENU or VK_RMENU:
                _altDown = down;
                if (!down && IsOpen && !_sticky)
                {
                    IsOpen = false;
                    command = SwitcherCommand.Switch;
                }
                return false;
            case VK_SHIFT or VK_LSHIFT or VK_RSHIFT:
                _shiftDown = down;
                return false;
            case VK_CONTROL or VK_LCONTROL or VK_RCONTROL:
                _controlDown = down;
                return false;
            case VK_LWIN or VK_RWIN:
                _winDown = down;
                return false;
        }

        // The rest of a press that was swallowed going down.
        if (!down)
            return _swallowing.Remove(virtualKey);

        if (virtualKey == VK_TAB && _altDown && !_winDown && !IsOpen)
        {
            _sticky = _controlDown;
            command = (_sticky, _shiftDown) switch
            {
                (false, false) => SwitcherCommand.Open,
                (false, true) => SwitcherCommand.OpenBackwards,
                (true, false) => SwitcherCommand.OpenSticky,
                (true, true) => SwitcherCommand.OpenStickyBackwards,
            };
            IsOpen = true;
        }
        else if (IsOpen)
        {
            command = virtualKey switch
            {
                VK_TAB when _altDown || _sticky => _shiftDown ? SwitcherCommand.Previous : SwitcherCommand.Next,
                VK_SPACE when _sticky => SwitcherCommand.Switch,
                VK_LEFT => SwitcherCommand.Previous,
                VK_RIGHT => SwitcherCommand.Next,
                VK_UP => SwitcherCommand.Up,
                VK_DOWN => SwitcherCommand.Down,
                VK_RETURN => SwitcherCommand.Switch,
                VK_ESCAPE => SwitcherCommand.Cancel,
                VK_DELETE => SwitcherCommand.CloseWindow,
                _ => null,
            };
            if (command is SwitcherCommand.Switch or SwitcherCommand.Cancel)
                IsOpen = false;
        }

        if (command is null)
            return false;
        _swallowing.Add(virtualKey);
        return true;
    }

    /// <summary>
    /// The switcher closed by other means (a click, or another window taking the foreground): its keys pass through
    /// again until the next Alt+Tab.
    /// </summary>
    public void Close() => IsOpen = false;
}
