using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Tray;
using NeoShell.Interop.Windowing;
using NeoShell.Settings;

namespace NeoShell.Taskbar;

/// <summary>A taskbar button as the XAML template shows it.</summary>
internal sealed class TaskButton(string key) : INotifyPropertyChanged
{
    private string _title = "";
    private string _toolTip = "";
    private ImageSource? _icon;
    private bool _isActive;
    private bool _isFlashing;
    private bool _showLabel;
    private TaskProgress? _progress;
    private ImageSource? _overlay;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; } = key;

    /// <summary>The pinned app this button is (or holds windows of), or null for an unpinned running app.</summary>
    public PinnedApp? Pinned { get; private set; }

    /// <summary>The app the button launches and pins.</summary>
    public PinnedApp App { get; private set; } = new("");

    public IReadOnlyList<WindowInfo> Windows { get; private set; } = [];

    public string Title { get => _title; private set => Set(ref _title, value); }

    public string ToolTip { get => _toolTip; private set => Set(ref _toolTip, value); }

    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (Set(ref _isActive, value))
                Raise(nameof(ActiveVisibility));
        }
    }

    public bool IsFlashing
    {
        get => _isFlashing;
        private set
        {
            if (Set(ref _isFlashing, value))
                Raise(nameof(FlashVisibility));
        }
    }

    public bool ShowLabel
    {
        get => _showLabel;
        private set
        {
            if (Set(ref _showLabel, value))
            {
                Raise(nameof(LabelVisibility));
                Raise(nameof(Width));
            }
        }
    }

    public double Width => ShowLabel ? TaskListBuilder.LabeledButtonWidth - 4 : 40;

    public Visibility LabelVisibility => ShowLabel ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RunningVisibility => !IsActive && Windows.Count == 1 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MultipleVisibility => !IsActive && Windows.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FlashVisibility => IsFlashing ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The app's progress (ITaskbarList3), shown as a bar along the bottom of the button.</summary>
    public TaskProgress? Progress
    {
        get => _progress;
        private set
        {
            if (Set(ref _progress, value))
            {
                Raise(nameof(ProgressVisibility));
                Raise(nameof(ProgressPercent));
                Raise(nameof(IsProgressIndeterminate));
                Raise(nameof(IsProgressError));
                Raise(nameof(IsProgressPaused));
            }
        }
    }

    public Visibility ProgressVisibility => Progress is null ? Visibility.Collapsed : Visibility.Visible;

    public double ProgressPercent => (Progress?.Value ?? 0) * 100;

    public bool IsProgressIndeterminate => Progress?.State == TaskbarProgressState.Indeterminate;

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

    public Visibility OverlayVisibility => Overlay is null ? Visibility.Collapsed : Visibility.Visible;

    public void Update(TaskButtonModel model, WindowTracker tracker, bool combined)
    {
        Pinned = model.Pinned;
        Windows = model.Windows;
        App = model.Pinned ?? tracker.AppFor(model.Windows[0]);

        // A button for one window, uncombined, is about that window; otherwise it is about the app.
        WindowInfo? single = !combined && model.Windows.Count == 1 ? model.Windows[0] : null;
        Title = single?.Title ?? App.DisplayName;
        ToolTip = model.Windows.Count switch
        {
            0 => App.DisplayName,
            1 => model.Windows[0].Title,
            int count => $"{App.DisplayName} - {count} windows",
        };
        Icon = single is not null ? tracker.WindowIcon(single) : tracker.AppIcon(App) ?? FirstWindowIcon(tracker);
        ShowLabel = single is not null;
        IsActive = model.Windows.Any(w => w.Handle == tracker.Foreground);
        IsFlashing = model.Windows.Any(w => tracker.IsFlashing(w.Handle));
        // A combined button shows the first of its windows that reports anything.
        Progress = model.Windows.Select(w => tracker.Progress(w.Handle)).FirstOrDefault(p => p is not null);
        Overlay = model.Windows.Select(w => tracker.Overlay(w.Handle)).FirstOrDefault(o => o is not null);
        Raise(nameof(RunningVisibility));
        Raise(nameof(MultipleVisibility));
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

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
