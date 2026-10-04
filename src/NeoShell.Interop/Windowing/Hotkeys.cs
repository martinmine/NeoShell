using NeoShell.Interop.Native;

namespace NeoShell.Interop.Windowing;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
    /// <summary>Holding the keys down doesn't repeat the hotkey.</summary>
    NoRepeat = 0x4000,
}

/// <summary>System-wide hotkeys (<c>RegisterHotKey</c>). Create it on the UI thread; <see cref="Pressed"/> is raised there.</summary>
public sealed class Hotkeys : IDisposable
{
    private readonly MessageWindow _window;
    private readonly List<int> _ids = [];

    public Hotkeys()
    {
        _window = new MessageWindow("NeoShell.Hotkeys", OnMessage);
    }

    /// <summary>The ID given to <see cref="Register"/> of the hotkey pressed.</summary>
    public event Action<int>? Pressed;

    /// <summary>Registers a hotkey; false when another app already has it.</summary>
    public bool Register(int id, HotkeyModifiers modifiers, uint virtualKey)
    {
        if (!User32.RegisterHotKey(_window.Handle, id, (uint)modifiers, virtualKey))
            return false;
        _ids.Add(id);
        return true;
    }

    public void Dispose()
    {
        foreach (int id in _ids)
            User32.UnregisterHotKey(_window.Handle, id);
        _window.Dispose();
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != User32.WM_HOTKEY)
            return null;

        Pressed?.Invoke((int)wParam);
        return 0;
    }
}
