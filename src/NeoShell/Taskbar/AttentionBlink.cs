namespace NeoShell.Taskbar;

/// <summary>
/// When a flashing task button blinks and when it stops, as Explorer's (Taskbar.View.dll,
/// <c>TaskListButton::TaskListButtonPlayRequestingAttentionAnimation</c>; recorded in docs/design/taskbar.md): a window
/// that flashes on lights its button and starts a blink; each blink plays to its end, and another follows while the
/// window's flash messages keep coming. Once a whole blink passed without any, the button stays lit if the flash left
/// the window on, and goes back to normal if it didn't (stopped, or the window was activated). Activating a window
/// whose button is lit plays one more blink before it goes back to normal; a flash stopped while the button is lit
/// and still leaves it lit (Explorer only looks again at the end of a blink), but closing the window that flashed
/// ends it at once.
/// </summary>
/// <param name="active">Whether one of the button's windows is active as it starts flashing.</param>
public sealed class AttentionBlink(bool active)
{
    private bool _flashing;
    private bool _active = active;
    private int _messages;
    private int _messagesAtBlink;

    /// <summary>Whether the button shows the attention look (its red plate and long pill).</summary>
    public bool Shows { get; private set; }

    /// <summary>Whether a blink is playing.</summary>
    public bool Blinking { get; private set; }

    /// <summary>
    /// Whether a window was active before the last update: a blink that starts then dims the plate to half rather than
    /// to nothing; one started by activating the window still goes to nothing, as Explorer's (it reads the state
    /// before the activation reaches the button).
    /// </summary>
    public bool WasActive { get; private set; }

    /// <summary>
    /// Takes the button's windows' state: whether one is flashing on (<see cref="WindowTracker.IsFlashing"/>), their
    /// flash messages so far and whether one is active. Returns true when a blink should start now.
    /// </summary>
    public bool Update(bool flashing, int messages, bool active)
    {
        bool start = !Blinking && ((flashing && !_flashing) || (Shows && active && !_active));
        // Fewer messages: the window that flashed is gone.
        if (!Blinking && messages < _messages)
            Shows = flashing;
        _flashing = flashing;
        WasActive = _active;
        _active = active;
        _messages = messages;
        if (start)
        {
            Shows = true;
            Blinking = true;
            _messagesAtBlink = messages;
        }
        return start;
    }

    /// <summary>A blink played to its end: returns true when another should follow.</summary>
    public bool BlinkEnded()
    {
        if (_messages != _messagesAtBlink)
        {
            _messagesAtBlink = _messages;
            return true;
        }
        Blinking = false;
        Shows = _flashing;
        return false;
    }
}
