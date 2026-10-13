using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Notifications;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;

namespace NeoShell.Taskbar;

/// <summary>A taskbar button as the XAML template shows it.</summary>
internal sealed class TaskButton(string key) : INotifyPropertyChanged
{
    private string _title = "";
    private ImageSource? _icon;
    private bool _isActive;
    private bool _isFlashing;
    private bool _showsAttention;
    private bool _isHovered;
    private bool _isDragged;
    private bool _showLabel;
    private double _width = TaskListBuilder.CombinedButtonWidth;
    private TaskProgress? _progress;
    private ImageSource? _overlay;
    private AppBadge? _badge;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; } = key;

    /// <summary>The pinned app this button is (or holds windows of), or null for an unpinned running app.</summary>
    public PinnedApp? Pinned { get; private set; }

    /// <summary>The app the button launches and pins.</summary>
    public PinnedApp App { get; private set; } = new("");

    public IReadOnlyList<WindowInfo> Windows { get; private set; } = [];

    public string Title { get => _title; private set => Set(ref _title, value); }

    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (Set(ref _isActive, value))
                RaisePills();
        }
    }

    /// <summary>Whether one of the button's windows is flashing on (see <see cref="WindowTracker.IsFlashing"/>).</summary>
    public bool IsFlashing { get => _isFlashing; private set => Set(ref _isFlashing, value); }

    /// <summary>The flash messages the button's windows have had: a blinking button keeps blinking while they come.</summary>
    public int FlashMessages { get; private set; }

    /// <summary>
    /// Set by Windows 11's taskbar while the button shows that its window wants attention (the red plate and long
    /// pill), which it keeps for a while after the flash (<see cref="AttentionBlink"/>).
    /// </summary>
    public bool ShowsAttention
    {
        get => _showsAttention;
        set
        {
            if (Set(ref _showsAttention, value))
            {
                Raise(nameof(HoverVisibility));
                RaisePills();
            }
        }
    }

    /// <summary>Set by the taskbar while the pointer is over the button.</summary>
    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (Set(ref _isHovered, value))
            {
                Raise(nameof(HoverVisibility));
                Raise(nameof(AttentionHoverVisibility));
                Raise(nameof(ActiveRestVisibility));
                Raise(nameof(ActiveHoverVisibility));
            }
        }
    }

    /// <summary>Set by the taskbar while the button is dragged: just the icon, as in Windows 11.</summary>
    public bool IsDragged
    {
        get => _isDragged;
        set
        {
            if (Set(ref _isDragged, value))
            {
                RaisePills();
                Raise(nameof(StackVisibility));
            }
        }
    }

    /// <summary>A button for one window, uncombined, has a label: the window's title.</summary>
    public bool ShowLabel
    {
        get => _showLabel;
        private set
        {
            if (Set(ref _showLabel, value))
                Raise(nameof(LabelVisibility));
        }
    }

    /// <summary>Set by the taskbar: a labelled button's narrows as the taskbar fills (<see cref="TaskbarFit"/>).</summary>
    public double Width
    {
        get => _width;
        set
        {
            if (Set(ref _width, value))
                Raise(nameof(LabelWidth));
        }
    }

    /// <summary>What the label has of the button's width; it's cut off at that, as Explorer's, not ended in an ellipsis.</summary>
    public double LabelWidth => Math.Max(0, Width - TaskbarFit.LabelChrome);

    public Visibility LabelVisibility => ShowLabel ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The indicator under the icon: none while dragged, the progress bar in its place while the app reports progress,
    /// else the attention, active or running pill.
    /// </summary>
    public TaskPill Pill =>
        Windows.Count == 0 || IsDragged ? TaskPill.None
        : ShowsProgress ? TaskPill.Progress
        : ShowsAttention ? TaskPill.Attention
        : IsActive ? TaskPill.Active
        : TaskPill.Running;

    /// <summary>
    /// The active plate fades in and out (an opacity transition), as Explorer's brush transition does; the attention
    /// plate takes its place.
    /// </summary>
    public double ActiveOpacity => IsActive && !IsDragged && !ShowsAttention ? 1 : 0;

    /// <summary>The attention plate fades in and out the same way.</summary>
    public double AttentionOpacity => ShowsAttention && !IsDragged ? 1 : 0;

    public Visibility RunningVisibility => Pill == TaskPill.Running ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ActiveVisibility => Pill == TaskPill.Active ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AttentionVisibility => Pill == TaskPill.Attention ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Several windows: the plate shows the edge of a second card behind it, as in Windows 11.</summary>
    public Visibility StackVisibility => Windows.Count > 1 && !IsDragged ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The hover plate; a flashing button's plate turns its hover colour instead, and so does the active one's (a shade
    /// lighter, as Explorer's: its fill becomes the secondary control fill rather than both showing).
    /// </summary>
    public Visibility HoverVisibility => IsHovered && !ShowsAttention && ActiveOpacity == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ActiveRestVisibility => IsHovered ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ActiveHoverVisibility => IsHovered ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AttentionHoverVisibility => IsHovered ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The app's progress (ITaskbarList3), shown as a bar along the bottom of the button.</summary>
    public TaskProgress? Progress
    {
        get => _progress;
        private set
        {
            if (Set(ref _progress, value))
            {
                RaisePills();
                Raise(nameof(ProgressPercent));
                Raise(nameof(IsProgressIndeterminate));
                Raise(nameof(IsProgressError));
                Raise(nameof(IsProgressPaused));
            }
        }
    }

    public bool ShowsProgress => Progress?.ShowsBar == true;

    public double ProgressOpacity => Pill == TaskPill.Progress ? 1 : 0;

    public double ProgressPercent => Pill == TaskPill.Progress ? Progress!.Value * 100 : 0;

    public bool IsProgressIndeterminate => Pill == TaskPill.Progress && Progress!.State == TaskbarProgressState.Indeterminate;

    public bool IsProgressError => Progress?.State == TaskbarProgressState.Error;

    public bool IsProgressPaused => Progress?.State == TaskbarProgressState.Paused;

    /// <summary>The app's overlay badge (ITaskbarList3), drawn over the corner of the icon.</summary>
    public ImageSource? Overlay
    {
        get => _overlay;
        private set
        {
            if (Set(ref _overlay, value))
                Raise(nameof(OverlayVisibility));
        }
    }

    /// <summary>The overlay sits where the badge goes; Explorer shows the badge instead while there is one.</summary>
    public Visibility OverlayVisibility => Overlay is null || Badge is not null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The app's badge notification (BadgeUpdateManager), drawn over the top-right corner of the icon.</summary>
    public AppBadge? Badge
    {
        get => _badge;
        private set
        {
            if (Set(ref _badge, value))
            {
                Raise(nameof(OverlayVisibility));
                Raise(nameof(BadgeHelpText));
            }
        }
    }

    public string BadgeHelpText => Badge is null ? "" : BadgeLook.HelpText(Badge);

    public void Update(TaskButtonModel model, WindowTracker tracker, bool combined)
    {
        Pinned = model.Pinned;
        Windows = model.Windows;
        App = model.Pinned ?? tracker.AppFor(model.Windows[0]);

        // A button for one window, uncombined, is about that window; otherwise it is about the app.
        WindowInfo? single = !combined && model.Windows.Count == 1 ? model.Windows[0] : null;
        Title = single?.Title ?? App.DisplayName;
        Icon = single is not null ? tracker.WindowIcon(single) : tracker.AppIcon(App) ?? FirstWindowIcon(tracker);
        ShowLabel = single is not null;
        IsActive = model.Windows.Any(w => w.Handle == tracker.Foreground);
        IsFlashing = model.Windows.Any(w => tracker.IsFlashing(w.Handle));
        FlashMessages = model.Windows.Sum(w => tracker.FlashMessages(w.Handle));
        // A combined button shows the first of its windows that reports anything.
        Progress = model.Windows.Select(w => tracker.Progress(w.Handle)).FirstOrDefault(p => p is not null);
        Overlay = model.Windows.Select(w => tracker.Overlay(w.Handle)).FirstOrDefault(o => o is not null);
        Badge = tracker.Badge(App);
        RaisePills();
        Raise(nameof(StackVisibility));
    }

    // UI Automation names list items by their ToString.
    public override string ToString() => Title;

    private ImageSource? FirstWindowIcon(WindowTracker tracker) =>
        Windows.Count > 0 ? tracker.WindowIcon(Windows[0]) : null;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(name);
        return true;
    }

    private void RaisePills()
    {
        Raise(nameof(Pill));
        Raise(nameof(RunningVisibility));
        Raise(nameof(ActiveVisibility));
        Raise(nameof(AttentionVisibility));
        Raise(nameof(ProgressOpacity));
        Raise(nameof(ProgressPercent));
        Raise(nameof(IsProgressIndeterminate));
        Raise(nameof(ActiveOpacity));
        Raise(nameof(AttentionOpacity));
        Raise(nameof(HoverVisibility));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
