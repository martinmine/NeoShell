using NeoShell.Interop.Windowing;
using NeoShell.Settings;
using NeoShell.Taskbar;

namespace NeoShell.Tests;

public sealed class TaskListTests
{
    private const int OwnProcess = 100;
    private const string Notepad = @"C:\Windows\System32\notepad.exe";
    private const string Code = @"C:\Program Files\VS Code\Code.exe";
    private const string Terminal = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";

    private static WindowInfo Window(
        nint handle, string? path = Notepad, string? appId = null, bool visible = true, bool cloaked = false,
        bool appWindow = false, bool toolWindow = false, bool noActivate = false, nint owner = 0, int process = 1,
        string className = "Notepad") =>
        new(handle, $"Window {handle}", className, visible, cloaked, appWindow, toolWindow, noActivate, owner, process, appId, path);

    [Fact]
    public void Ordinary_top_level_window_gets_a_button()
    {
        Assert.True(TaskFilter.GetsButton(Window(1), OwnProcess));
    }

    [Theory]
    [InlineData(false, false, false, false, false, 0)] // hidden
    [InlineData(true, true, false, false, false, 0)]   // cloaked: another virtual desktop
    [InlineData(true, false, false, true, false, 0)]   // tool window
    [InlineData(true, false, false, false, true, 0)]   // no-activate
    [InlineData(true, false, false, false, false, 5)]  // owned (a dialog)
    public void Windows_that_explorer_leaves_off_the_taskbar_get_no_button(
        bool visible, bool cloaked, bool appWindow, bool toolWindow, bool noActivate, int owner)
    {
        Assert.False(TaskFilter.GetsButton(
            Window(1, visible: visible, cloaked: cloaked, appWindow: appWindow, toolWindow: toolWindow, noActivate: noActivate, owner: owner),
            OwnProcess));
    }

    [Fact]
    public void App_window_style_overrides_owner_tool_and_no_activate()
    {
        Assert.True(TaskFilter.GetsButton(Window(1, appWindow: true, toolWindow: true, noActivate: true, owner: 5), OwnProcess));
    }

    [Fact]
    public void App_window_style_doesnt_show_hidden_or_cloaked_windows()
    {
        Assert.False(TaskFilter.GetsButton(Window(1, appWindow: true, visible: false), OwnProcess));
        Assert.False(TaskFilter.GetsButton(Window(1, appWindow: true, cloaked: true), OwnProcess));
    }

    [Fact]
    public void Shell_surfaces_like_explorers_start_get_no_button()
    {
        Assert.False(TaskFilter.GetsButton(Window(1, className: "Windows.UI.Core.CoreWindow"), OwnProcess));
    }

    [Fact]
    public void NeoShells_own_windows_get_no_button()
    {
        Assert.False(TaskFilter.GetsButton(Window(1, process: OwnProcess), OwnProcess));
    }

    [Fact]
    public void Group_key_is_the_app_id_then_the_executable()
    {
        Assert.Equal(Terminal, TaskGrouping.Key(Window(1, appId: Terminal, path: @"C:\x\WindowsTerminal.exe")));
        Assert.Equal(Notepad, TaskGrouping.Key(Window(1)));
        Assert.Equal("window:1", TaskGrouping.Key(Window(1, path: null)));
    }

    [Fact]
    public void Pinned_app_matches_windows_by_app_id_or_by_path()
    {
        var pinnedTerminal = new PinnedApp("Terminal", AppUserModelId: Terminal);
        var pinnedNotepad = new PinnedApp("Notepad", Path: @"c:\windows\system32\NOTEPAD.EXE");

        Assert.True(TaskGrouping.Matches(pinnedTerminal, Window(1, appId: Terminal)));
        Assert.True(TaskGrouping.Matches(pinnedNotepad, Window(1)));
        // A window with an app ID only matches that ID, even when the executable is the same.
        Assert.False(TaskGrouping.Matches(pinnedNotepad, Window(1, appId: "Contoso.Notes")));
        Assert.False(TaskGrouping.Matches(pinnedTerminal, Window(1)));
    }

    [Fact]
    public void File_Explorer_windows_take_File_Explorers_app_id()
    {
        Assert.Equal("Microsoft.Windows.Explorer", WindowInfo.ImplicitAppId("CabinetWClass", @"C:\Windows\explorer.exe"));
        Assert.Null(WindowInfo.ImplicitAppId("Progman", @"C:\Windows\explorer.exe"));
        Assert.Null(WindowInfo.ImplicitAppId("CabinetWClass", @"C:\Tools\other.exe"));
        Assert.Null(WindowInfo.ImplicitAppId("CabinetWClass", null));
    }

    [Fact]
    public void App_for_a_window_launches_by_app_id_when_it_has_one()
    {
        Assert.Equal(new PinnedApp("Terminal", Terminal, null), TaskGrouping.AppFor(Window(1, appId: Terminal), "Terminal"));
        Assert.Equal(new PinnedApp("Notepad", null, Notepad), TaskGrouping.AppFor(Window(1), "Notepad"));
    }

    [Fact]
    public void Combined_pinned_apps_come_first_then_running_apps_in_order_of_appearance()
    {
        var pinnedCode = new PinnedApp("Code", Path: Code);
        var pinnedTerminal = new PinnedApp("Terminal", AppUserModelId: Terminal);
        WindowInfo notepad1 = Window(1), code = Window(2, Code), notepad2 = Window(3), other = Window(4, @"C:\other.exe");

        IReadOnlyList<TaskButtonModel> buttons = TaskListBuilder.Build([pinnedCode, pinnedTerminal], [notepad1, code, notepad2, other], combine: true);

        Assert.Equal([Code, Terminal, Notepad, @"C:\other.exe"], buttons.Select(b => b.Key));
        Assert.Equal([pinnedCode, pinnedTerminal, null, null], buttons.Select(b => b.Pinned));
        Assert.Equal([[code], [], [notepad1, notepad2], [other]], buttons.Select(b => b.Windows));
    }

    [Fact]
    public void Uncombined_each_window_is_a_button_and_pinned_windows_take_the_pinned_place()
    {
        var pinnedNotepad = new PinnedApp("Notepad", Path: Notepad);
        var pinnedTerminal = new PinnedApp("Terminal", AppUserModelId: Terminal);
        WindowInfo code = Window(1, Code), notepad1 = Window(2), notepad2 = Window(3);

        IReadOnlyList<TaskButtonModel> buttons = TaskListBuilder.Build([pinnedNotepad, pinnedTerminal], [code, notepad1, notepad2], combine: false);

        Assert.Equal(["window:2", "window:3", Terminal, "window:1"], buttons.Select(b => b.Key));
        Assert.Equal([pinnedNotepad, pinnedNotepad, pinnedTerminal, null], buttons.Select(b => b.Pinned));
    }

    [Fact]
    public void A_window_matching_two_pinned_apps_goes_to_the_first()
    {
        var first = new PinnedApp("Notepad", Path: Notepad);
        var second = new PinnedApp("Notepad again", Path: Notepad);

        IReadOnlyList<TaskButtonModel> buttons = TaskListBuilder.Build([first, second], [Window(1)], combine: true);

        Assert.Single(buttons[0].Windows);
        Assert.Empty(buttons[1].Windows);
    }

    [Fact]
    public void Grouping_ignores_path_case()
    {
        IReadOnlyList<TaskButtonModel> buttons = TaskListBuilder.Build([], [Window(1, Notepad), Window(2, Notepad.ToUpperInvariant())], combine: true);

        Assert.Single(buttons);
    }

    [Theory]
    [InlineData(CombineButtons.Always, 10_000, true)]
    [InlineData(CombineButtons.Never, 10, false)]
    [InlineData(CombineButtons.WhenFull, 2 * TaskListBuilder.LabeledButtonWidth + TaskListBuilder.CombinedButtonWidth, false)]
    [InlineData(CombineButtons.WhenFull, 2 * TaskListBuilder.LabeledButtonWidth + TaskListBuilder.CombinedButtonWidth - 1, true)]
    public void Combine_mode_decides_when_to_combine(CombineButtons mode, double available, bool expected)
    {
        // Two running windows (labeled) and one pinned app that isn't running (icon only).
        IReadOnlyList<TaskButtonModel> uncombined = TaskListBuilder.Build([new PinnedApp("Code", Path: Code)], [Window(1), Window(2)], combine: false);

        Assert.Equal(expected, TaskListBuilder.ShouldCombine(mode, uncombined, available));
    }

    [Theory]
    [InlineData(0, 1)] // none of them in front: the first
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 1)] // the last in front: around to the first
    public void Win_number_goes_through_a_buttons_windows(int foreground, int expected)
    {
        Assert.Equal((nint)expected, TaskActivation.NextWindow([1, 2, 3], foreground));
    }

    [Fact]
    public void Win_number_on_a_button_without_windows_activates_nothing()
    {
        Assert.Equal(0, TaskActivation.NextWindow([], 5));
    }

    [Theory]
    [InlineData(9, 3)] // another app in front: the button's window highest in the z-order
    [InlineData(3, 1)] // one of them in front already: on round the button's windows
    [InlineData(2, 3)]
    public void Win_ctrl_number_brings_the_last_active_window(int foreground, int expected)
    {
        Assert.Equal((nint)expected, TaskActivation.LastActiveWindow([1, 2, 3], [9, 4, 3, 1, 2], foreground));
    }

    [Fact]
    public void Win_ctrl_number_falls_back_to_the_first_window_and_to_nothing()
    {
        Assert.Equal(1, TaskActivation.LastActiveWindow([1, 2], [9], 9));
        Assert.Equal(0, TaskActivation.LastActiveWindow([], [9], 9));
    }

    [Theory]
    [InlineData(1, null, true)]
    [InlineData(1, Terminal, true)]
    [InlineData(null, null, false)]  // never set: off
    [InlineData(0, null, false)]
    [InlineData(2, null, false)]     // Explorer wants exactly 1
    [InlineData("1", null, false)]   // not a DWORD
    [InlineData(1, "Microsoft.Windows.Explorer", false)]
    [InlineData(1, "microsoft.windows.explorer", false)]
    public void End_task_is_offered_when_the_developer_setting_is_1_but_never_for_file_explorer(
        object? setting, string? appId, bool offered)
    {
        Assert.Equal(offered, TaskEnding.IsOffered(setting, appId));
    }

    [Fact]
    public void End_task_ends_a_uwp_apps_package_rather_than_its_frame_host()
    {
        const string calculator = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        Assert.True(TaskEnding.EndsPackage([Window(1, appId: calculator, className: "ApplicationFrameWindow")], calculator));
        // A packaged desktop app (Terminal, Paint) ends by its windows, as Explorer's taskbar does.
        Assert.False(TaskEnding.EndsPackage([Window(1, appId: Terminal, className: "CASCADIA_HOSTING_WINDOW_CLASS")], Terminal));
        Assert.False(TaskEnding.EndsPackage([Window(1)], null));
    }
}
