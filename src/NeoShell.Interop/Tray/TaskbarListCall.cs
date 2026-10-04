namespace NeoShell.Interop.Tray;

/// <summary>The <c>TBPF_*</c> progress states of <c>ITaskbarList3::SetProgressState</c>.</summary>
public enum TaskbarProgressState
{
    None = 0,
    Indeterminate = 1,
    Normal = 2,
    Error = 4,
    Paused = 8,
}

public enum TaskbarListCallKind { ProgressState, ProgressValue, OverlayIcon, FullScreen }

/// <summary>
/// An <c>ITaskbarList3</c> call from an app about one of its windows. Inside the app, ExplorerFrame turns the call into
/// a message to the window named by <c>Shell_TrayWnd</c>'s <c>TaskbandHWND</c> property.
/// </summary>
/// <param name="Value">
/// The progress state, the progress from 0 to 1, the overlay icon's HICON (0 removes it), or 1/0 for full screen.
/// </param>
public sealed record TaskbarListCall(TaskbarListCallKind Kind, nint Window, double Value)
{
    private const uint WM_USER = 0x0400;
    // Progress values arrive scaled to 0..0xFFFE, whatever the total the app gave.
    private const double ProgressMaximum = 0xFFFE;

    /// <summary>Reads a task band message; null for messages that aren't one NeoShell uses.</summary>
    internal static TaskbarListCall? Parse(uint message, nint wParam, nint lParam) => message switch
    {
        WM_USER + 65 => new TaskbarListCall(TaskbarListCallKind.ProgressState, wParam, (int)lParam),
        WM_USER + 64 => new TaskbarListCall(TaskbarListCallKind.ProgressValue, wParam, Math.Clamp(lParam / ProgressMaximum, 0, 1)),
        WM_USER + 79 => new TaskbarListCall(TaskbarListCallKind.OverlayIcon, wParam, lParam),
        // MarkFullscreenWindow puts the flag first and the window second.
        WM_USER + 60 => new TaskbarListCall(TaskbarListCallKind.FullScreen, lParam, wParam != 0 ? 1 : 0),
        _ => null,
    };
}
