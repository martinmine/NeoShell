using NeoShell.QuickSettings;

namespace NeoShell;

/// <summary>What a panel shortcut opens.</summary>
public enum PanelShortcut { QuickSettings, SoundOutput, Cast, Project, NotificationCenter, QuickLinks }

/// <summary>
/// Spots the shortcuts of Quick Settings and the notification center in a stream of key presses: Win+A (the tiles),
/// Win+Ctrl+V (Sound output), Win+K (Cast), Win+P (Project) and Win+N (notifications and calendar); and Start's Quick
/// Link menu, Win+X.
/// </summary>
/// <remarks>
/// Windows' own Quick Settings host keeps its shortcuts registered as hotkeys after Explorer has gone, so NeoShell
/// can't register them; it takes them from the keyboard hook instead, and swallows the letter (down, repeats and up)
/// so Windows' panel doesn't open as well.
/// </remarks>
public sealed class PanelKeys
{
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;

    private bool _winDown;
    private bool _controlDown;
    private int? _swallowing;

    /// <summary>Feeds one key event; returns true to swallow it, with what to open when it completes a shortcut.</summary>
    public bool OnKey(int virtualKey, bool down, out PanelShortcut? shortcut)
    {
        shortcut = null;
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

        shortcut = (char)virtualKey switch
        {
            'A' when !_controlDown => PanelShortcut.QuickSettings,
            'K' when !_controlDown => PanelShortcut.Cast,
            'P' when !_controlDown => PanelShortcut.Project,
            'V' when _controlDown => PanelShortcut.SoundOutput,
            'N' when !_controlDown => PanelShortcut.NotificationCenter,
            'X' when !_controlDown => PanelShortcut.QuickLinks,
            _ => null,
        };
        if (shortcut is null)
            return false;

        _swallowing = virtualKey;
        return true;
    }

    /// <summary>The Quick Settings page a shortcut opens; null for the notification center and the Quick Link menu.</summary>
    public static QuickSettingsPage? PageFor(PanelShortcut shortcut) => shortcut switch
    {
        PanelShortcut.QuickSettings => QuickSettingsPage.Main,
        PanelShortcut.SoundOutput => QuickSettingsPage.SoundOutput,
        PanelShortcut.Cast => QuickSettingsPage.Cast,
        PanelShortcut.Project => QuickSettingsPage.Project,
        _ => null,
    };
}
