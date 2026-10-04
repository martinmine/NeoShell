namespace NeoShell.QuickSettings;

/// <summary>
/// Spots Quick Settings' shortcuts in a stream of key presses: Win+A (the tiles), Win+Ctrl+V (Sound output), Win+K
/// (Cast) and Win+P (Project).
/// </summary>
/// <remarks>
/// Windows' own Quick Settings host keeps these registered as hotkeys after Explorer has gone, so NeoShell can't
/// register them; it takes them from the keyboard hook instead, and swallows the letter (down, repeats and up) so
/// Windows' panel doesn't open as well.
/// </remarks>
public sealed class QuickSettingsKeys
{
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;

    private bool _winDown;
    private bool _controlDown;
    private int? _swallowing;

    /// <summary>Feeds one key event; returns true to swallow it, with the page to open when it completes a shortcut.</summary>
    public bool OnKey(int virtualKey, bool down, out QuickSettingsPage? page)
    {
        page = null;
        switch (virtualKey)
        {
            case VK_LWIN or VK_RWIN:
                _winDown = down;
                return false;
            case VK_CONTROL or VK_LCONTROL or VK_RCONTROL:
                _controlDown = down;
                return false;
        }

        if (virtualKey == _swallowing)
        {
            if (!down)
                _swallowing = null;
            return true;
        }
        if (!down || !_winDown)
            return false;

        page = (char)virtualKey switch
        {
            'A' when !_controlDown => QuickSettingsPage.Main,
            'K' when !_controlDown => QuickSettingsPage.Cast,
            'P' when !_controlDown => QuickSettingsPage.Project,
            'V' when _controlDown => QuickSettingsPage.SoundOutput,
            _ => null,
        };
        if (page is null)
            return false;

        _swallowing = virtualKey;
        return true;
    }
}
