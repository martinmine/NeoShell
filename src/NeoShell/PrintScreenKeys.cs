using Microsoft.Win32;

namespace NeoShell;

/// <summary>
/// Spots the Print Screen key pressed on its own, which opens the snip overlay while "Use the Print screen key to open
/// screen capture" is on (Settings → Accessibility → Keyboard). The key is swallowed (down, repeats and up); with
/// the setting off, or with Win, Ctrl, Alt or Shift held, it's left to Windows (Alt+PrtScn copies the window in front,
/// Win+PrtScn is a hotkey of NeoShell's).
/// </summary>
/// <remarks>
/// Explorer (twinui.pcshell, <c>CScreenClippingExperienceManager::LookUpPrintScreenSetting</c>) takes the key unless
/// the policy <c>MakePrintScreenKeyYieldable</c> is 0 or the user's <c>PrintScreenKeyForSnippingEnabled</c> is 0; on
/// when neither is set.
/// </remarks>
public sealed class PrintScreenKeys
{
    private const int VK_SNAPSHOT = 0x2C;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    // Ctrl, Alt and Shift, either side, and the Windows keys.
    private static readonly int[] s_modifiers = [0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, VK_LWIN, VK_RWIN];

    private readonly HashSet<int> _held = [];
    private bool _swallowing;

    /// <summary>
    /// Feeds one key event; returns true to swallow it, with <paramref name="snip"/> set when the snip overlay should
    /// open.
    /// </summary>
    /// <param name="enabled">Reads the setting, asked only when Print Screen goes down on its own.</param>
    public bool OnKey(int virtualKey, bool down, Func<bool> enabled, out bool snip)
    {
        snip = false;
        if (s_modifiers.Contains(virtualKey))
        {
            if (down)
                _held.Add(virtualKey);
            else
                _held.Remove(virtualKey);
            return false;
        }
        if (virtualKey != VK_SNAPSHOT)
            return false;

        if (!down)
        {
            bool swallowed = _swallowing;
            _swallowing = false;
            return swallowed;
        }
        if (_swallowing)
            return true;
        if (_held.Count > 0 || !enabled())
            return false;
        _swallowing = snip = true;
        return true;
    }

    /// <summary>Whether Print Screen opens the snip overlay, from the policy and the user's setting (null when unset).</summary>
    public static bool IsEnabled(int? policy, int? userSetting) => policy != 0 && userSetting != 0;

    /// <summary>Reads the setting (and its policy) now: Settings changes it while NeoShell runs.</summary>
    public static bool ReadSetting()
    {
        using RegistryKey? policy = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer");
        using RegistryKey? keyboard = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
        return IsEnabled(policy?.GetValue("MakePrintScreenKeyYieldable") as int?, keyboard?.GetValue("PrintScreenKeyForSnippingEnabled") as int?);
    }
}
