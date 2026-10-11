using Microsoft.UI.Xaml.Controls;
using NeoShell.Taskbar;

namespace NeoShell.Settings;

/// <summary>
/// A page of the Settings window. It fills its controls from the settings when shown, and again when something else
/// changes them (the sidebar's edge dragged, Start resized); its own changes are saved as they're made.
/// </summary>
internal partial class SettingsPageView : UserControl
{
    private bool _saving;

    protected SettingsPageView(Taskbars taskbars)
    {
        Taskbars = taskbars;
        Loaded += (_, _) =>
        {
            Store.Changed += OnChanged;
            Reload();
        };
        Unloaded += (_, _) => Store.Changed -= OnChanged;
    }

    protected Taskbars Taskbars { get; }

    protected SettingsStore Store => Taskbars.Settings;

    protected ShellSettings Current => Store.Current;

    protected RunMode RunMode => Taskbars.RunMode;

    /// <summary>The controls are being filled: their change events aren't the user's.</summary>
    protected bool IsFilling { get; private set; }

    /// <summary>Fills the controls from the settings (called with <see cref="IsFilling"/> set).</summary>
    protected virtual void Load()
    {
    }

    protected void Reload() => Filling(Load);

    /// <summary>Runs <paramref name="fill"/> with <see cref="IsFilling"/> set.</summary>
    protected void Filling(Action fill)
    {
        bool loading = IsFilling;
        IsFilling = true;
        try
        {
            fill();
        }
        finally
        {
            IsFilling = loading;
        }
    }

    protected void Save(ShellSettings settings)
    {
        _saving = true;
        try
        {
            Store.Update(settings);
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>A page of Windows' Settings, or as the shell what <see cref="Launcher.OpenSettings"/> opens instead.</summary>
    protected void OpenWindowsSettings(string displayName, string settingsUri, string applet = "") =>
        Launcher.OpenSettings(RunMode, displayName, settingsUri, applet);

    private void OnChanged()
    {
        if (!_saving)
            Reload();
    }
}
