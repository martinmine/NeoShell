namespace NeoShell.Themes;

/// <summary>
/// A theme's Start menu, as <see cref="Taskbar.Taskbars"/> uses it: made once per theme, opened above whichever taskbar
/// asks, closed by a click elsewhere. What it lists comes from the shared parts (the app catalog, Start's pins, search,
/// the power options).
/// </summary>
internal interface IStartMenuView
{
    bool IsOpen { get; }

    /// <summary>
    /// Pressing the Start button deactivates Start before the button's click arrives; that click must not reopen it.
    /// </summary>
    bool WasJustDeactivated { get; }

    /// <summary>Opens above <paramref name="taskbar"/>, taking the keyboard.</summary>
    void Show(ITaskbarView taskbar);

    void Hide();

    /// <summary>Closes the window for good: on exit, or when the theme changes.</summary>
    void Close();
}
