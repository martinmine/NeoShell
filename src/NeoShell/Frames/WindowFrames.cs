using Microsoft.UI.Dispatching;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Frames;

/// <summary>
/// Restyles other apps' title bars and frames by the user's global style and app rules (see
/// docs/design/window-frames.md): every window as it starts, each new one as it shows, and each again as it comes to
/// the front, which catches apps that set their own dark mode on activation or a theme change. Created by
/// <see cref="App"/> while the feature is on, in both run modes; disposing it puts every window it styled back.
/// </summary>
internal sealed class WindowFrames : IDisposable
{
    /// <summary>What a window was given, and its dark mode before, to put back.</summary>
    private sealed record Styled(FrameAttributes Attributes, bool DarkModeBefore);

    private readonly SettingsStore _settings;
    private readonly WindowEvents _events = new();
    private readonly Dictionary<nint, Styled> _styled = [];
    // Windows DWM refused (gone, or of an app running as administrator): skipped from then on, never retried.
    private readonly HashSet<nint> _refused = [];
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly DispatcherQueueTimer _restyleActivated;
    private nint _activated;
    private uint _accent;

    public WindowFrames(SettingsStore settings)
    {
        _settings = settings;
        // The foreground event comes as the window is activated, often before the app's own activation code (WM_ACTIVATE)
        // has run and set its attributes again: the window is styled once more a moment later.
        _restyleActivated = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _restyleActivated.Interval = TimeSpan.FromMilliseconds(250);
        _restyleActivated.IsRepeating = false;
        _restyleActivated.Tick += (_, _) => Style(_activated);
        _events.Raised += OnWindowEvent;
        StyleAll();
    }

    public void Dispose()
    {
        _restyleActivated.Stop();
        _events.Dispose();
        foreach ((nint hwnd, Styled styled) in _styled)
            WindowFrame.Reset(hwnd, styled.Attributes.Parts, styled.DarkModeBefore);
        Log.Info($"Window frames: put {_styled.Count} windows back");
        _styled.Clear();
    }

    private void OnWindowEvent(WindowEvent kind, nint hwnd)
    {
        switch (kind)
        {
            case WindowEvent.Shown or WindowEvent.Uncloaked:
                Style(hwnd);
                break;
            case WindowEvent.Foreground:
                Style(hwnd);
                _activated = hwnd;
                _restyleActivated.Stop();
                _restyleActivated.Start();
                break;
            case WindowEvent.Destroyed:
                _styled.Remove(hwnd);
                _refused.Remove(hwnd);
                break;
        }
    }

    /// <summary>Styles every window again, as the settings now say: on a change of them, and at the start.</summary>
    public void StyleAll()
    {
        _accent = ImmersiveColors.Get("ImmersiveSystemAccent") ?? 0xFF0078D4;
        foreach (nint hwnd in _styled.Keys.Where(hwnd => !TopLevelWindows.Exists(hwnd)).ToList())
            _styled.Remove(hwnd);
        foreach (nint hwnd in TopLevelWindows.GetAll())
            Style(hwnd);
    }

    private void Style(nint hwnd)
    {
        if (_refused.Contains(hwnd))
            return;
        WindowFrame.Target window = WindowFrame.Read(hwnd);
        if (!FrameRules.IsEligible(window, _ownProcessId))
            return;

        ShellSettings settings = _settings.Current;
        FrameStyle? style = FramePresets.Find(
            FrameRules.StyleFor(settings.WindowFrameRules, settings.WindowFrameStyle, window.ProcessName, window.ClassName),
            settings.WindowFrameStyles);
        FrameAttributes attributes = style is null ? new FrameAttributes() : FrameColors.ToAttributes(style, _accent);

        _styled.TryGetValue(hwnd, out Styled? before);
        FrameParts reset = FrameColors.PartsToReset(before?.Attributes, attributes);
        bool darkModeBefore = before?.DarkModeBefore ?? WindowFrame.ReadDarkMode(hwnd) ?? false;
        bool accepted = (reset == FrameParts.None || WindowFrame.Reset(hwnd, reset, darkModeBefore))
            && (attributes.Parts == FrameParts.None || WindowFrame.Apply(hwnd, attributes));
        if (!accepted)
        {
            Log.Info($"Window frames: DWM refused {window.ProcessName} ({window.ClassName}); leaving it alone");
            _refused.Add(hwnd);
            _styled.Remove(hwnd);
        }
        else if (attributes.Parts == FrameParts.None)
        {
            _styled.Remove(hwnd);
        }
        else
        {
            _styled[hwnd] = new Styled(attributes, darkModeBefore);
        }
    }
}
