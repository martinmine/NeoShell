using static NeoShell.Themes.Windows98.Win98Art;
using static NeoShell.Themes.Windows98.Win98Painting;
using static NeoShell.Themes.Windows98.Win98TaskbarLayout;

namespace NeoShell.Themes.Windows98;

/// <summary>How a button on 98's taskbar looks; a task button has no hot look, a Quick Launch button rises under the pointer.</summary>
public enum Win98ButtonState { Normal, Hot, Pressed }

/// <summary>A task button: its window's title, bold and pushed when it's the active window's.</summary>
public sealed record Win98TaskSlot(int X, int Width, string Title, bool Active, bool Pressed);

/// <summary>Everything drawn on one 98 taskbar; icons are shown over it. <paramref name="TrayX"/> is null without a notification area.</summary>
public sealed record Win98TaskbarScene(
    int Width,
    bool StartPressed,
    IReadOnlyList<(int X, Win98ButtonState State)> QuickLaunch,
    IReadOnlyList<Win98TaskSlot> Tasks,
    int? TrayX,
    string Clock,
    int ClockX);

/// <summary>Draws 98's taskbar from its measured numbers (see <see cref="Win98TaskbarLayout"/>).</summary>
public static class Win98TaskbarPainter
{
    public static void Paint(ThemeCanvas canvas, Win98TaskbarScene scene)
    {
        // The taskbar's own raised top edge; its bottom edge is below the screen.
        canvas.Fill(0, 0, scene.Width, 1, Light);
        canvas.Fill(0, 1, scene.Width, 1, Highlight);
        canvas.Fill(0, 2, scene.Width, Height - 2, Face);

        PaintStart(canvas, scene.StartPressed);
        if (scene.QuickLaunch.Count > 0)
        {
            Band(canvas, FirstBand);
            foreach ((int x, Win98ButtonState state) in scene.QuickLaunch)
            {
                if (state == Win98ButtonState.Hot)
                    ThinRaised(canvas, x, ButtonTop, QuickLaunchButton, ButtonHeight);
                else if (state == Win98ButtonState.Pressed)
                    ThinSunken(canvas, x, ButtonTop, QuickLaunchButton, ButtonHeight);
            }
        }
        Band(canvas, TaskButtonsLeft(scene.QuickLaunch.Count) - ContentLeft);
        foreach (Win98TaskSlot task in scene.Tasks)
            PaintTask(canvas, task);
        if (scene.TrayX is { } trayX)
            PaintTray(canvas, trayX, scene.Width, scene.Clock, scene.ClockX);
    }

    private static void Band(ThemeCanvas canvas, int x)
    {
        EtchedLine(canvas, x, ButtonTop, ButtonHeight);
        Gripper(canvas, x + GripperLeft, GripperTop, GripperHeight);
    }

    // Pushed (and while Start is open): the edge turned in, a dotted focus rectangle, the flag and "Start" a pixel down
    // and right.
    private static void PaintStart(ThemeCanvas canvas, bool pressed)
    {
        int shift = pressed ? 1 : 0;
        if (pressed)
        {
            PushedButton(canvas, StartLeft, ButtonTop, StartWidth, ButtonHeight);
            FocusRectangle(canvas, StartLeft + 3, ButtonTop + 3, StartWidth - 6, ButtonHeight - 6);
        }
        else
        {
            RaisedButton(canvas, StartLeft, ButtonTop, StartWidth, ButtonHeight);
        }
        Draw(canvas, Flag, StartLeft + 4 + shift, ButtonTop + 3 + shift);
        canvas.Text("Start", StartLeft + 22 + shift, ButtonTop + 4 + shift, 30, 13, BoldFont, Text);
    }

    private static void PaintTask(ThemeCanvas canvas, Win98TaskSlot task)
    {
        bool pushed = task.Active || task.Pressed;
        if (pushed)
        {
            PushedButton(canvas, task.X, ButtonTop, task.Width, ButtonHeight);
            Checker(canvas, task.X + 2, ButtonTop + 2, task.Width - 4, ButtonHeight - 4, 0, 0);
        }
        else
        {
            RaisedButton(canvas, task.X, ButtonTop, task.Width, ButtonHeight);
        }
        int labelX = task.X + LabelLeft;
        // The label's box takes the checkered face as it is: GDI draws the text onto it.
        canvas.Text(task.Title, labelX, ButtonTop + LabelTop + (pushed ? 1 : 0), task.X + task.Width - LabelRight - labelX, 13,
            task.Active ? BoldFont : Font, Text);
    }

    private static void PaintTray(ThemeCanvas canvas, int x, int width, string clock, int clockX)
    {
        EtchedLine(canvas, x - TrayEtch, ButtonTop, ButtonHeight);
        ThinSunken(canvas, x, ButtonTop, width - TrayRight - x, ButtonHeight);
        canvas.Text(clock, clockX, ButtonTop + ClockTop, width - TrayRight - ClockRight - clockX + 1, 13, Font, Text);
    }
}
