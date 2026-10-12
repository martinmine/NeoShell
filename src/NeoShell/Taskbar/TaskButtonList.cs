using System.Collections.ObjectModel;
using NeoShell.Interop.Windowing;

namespace NeoShell.Taskbar;

/// <summary>
/// A taskbar's buttons, kept in step with the pinned apps and windows, and what a click or Win+1…9 does with a
/// button's windows. Shared by every theme's taskbar, which shows the buttons its own way.
/// </summary>
internal sealed class TaskButtonList(WindowTracker tracker) : ObservableCollection<TaskButton>
{
    /// <summary>
    /// Makes the list <paramref name="models"/>, in their order. Buttons are updated in place, so they keep their state
    /// and the list doesn't flicker; returns those that are new.
    /// </summary>
    public List<TaskButton> Update(IReadOnlyList<TaskButtonModel> models, bool combine)
    {
        var keys = models.Select(model => model.Key).ToHashSet();
        for (int i = Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(this[i].Key))
                RemoveAt(i);
        }
        var added = new List<TaskButton>();
        for (int i = 0; i < models.Count; i++)
        {
            int existing = IndexOf(models[i].Key, i);
            if (existing < 0)
            {
                Insert(i, new TaskButton(models[i].Key));
                added.Add(this[i]);
            }
            else if (existing != i)
            {
                Move(existing, i);
            }
            this[i].Update(models[i], tracker, combine);
        }
        return added;
    }

    /// <summary>The Nth button (0-based), or null past the last.</summary>
    public TaskButton? At(int index) => index < Count ? this[index] : null;

    /// <summary>The button a window is on, or null.</summary>
    public TaskButton? Of(nint hwnd) => this.FirstOrDefault(button => button.Windows.Any(w => w.Handle == hwnd));

    /// <summary>
    /// Whether a click acts on the button's app or window straight away (<see cref="Click"/>): with Shift held, or with
    /// no window or one. A button of several windows shows them, each theme its own way.
    /// </summary>
    public static bool ClickActs(TaskButton button) => KeyboardState.IsShiftDown() || button.Windows.Count <= 1;

    /// <summary>
    /// A click that acts (<see cref="ClickActs"/>): starts the app (another instance with Shift), or brings its window
    /// to the front, or minimizes it when it's in front already.
    /// </summary>
    public void Click(TaskButton button)
    {
        if (KeyboardState.IsShiftDown() || button.Windows.Count == 0)
        {
            Launcher.Launch(button.App);
        }
        else if (button.Windows.Count == 1)
        {
            nint hwnd = button.Windows[0].Handle;
            if (hwnd == tracker.Foreground && !TopLevelWindows.IsMinimized(hwnd))
                TopLevelWindows.MinimizeAndActivateNext(hwnd);
            else
                TopLevelWindows.Activate(hwnd);
        }
    }

    /// <summary>Win+1…9 on a button of several windows: each press brings the next one to the front.</summary>
    public void ActivateNext(TaskButton button) =>
        TopLevelWindows.Activate(TaskActivation.NextWindow([.. button.Windows.Select(w => w.Handle)], tracker.Foreground));

    /// <summary>
    /// Win+Ctrl+1…9: the button's window that was in front last, then round its windows; starts the app when it has
    /// none.
    /// </summary>
    public void ActivateLastWindow(TaskButton button)
    {
        if (button.Windows.Count == 0)
            Launcher.Launch(button.App);
        else
            TopLevelWindows.Activate(TaskActivation.LastActiveWindow(
                [.. button.Windows.Select(w => w.Handle)], TopLevelWindows.GetAll(), tracker.Foreground));
    }

    private int IndexOf(string key, int start)
    {
        for (int i = start; i < Count; i++)
        {
            if (this[i].Key == key)
                return i;
        }
        return -1;
    }
}
