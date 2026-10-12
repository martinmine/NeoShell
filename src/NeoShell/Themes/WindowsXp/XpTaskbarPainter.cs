using static NeoShell.Themes.WindowsXp.XpTaskbarLayout;

namespace NeoShell.Themes.WindowsXp;

/// <summary>How a button of XP's looks: pressed is also how the active window's task button looks.</summary>
public enum XpButtonState { Normal, Hot, Pressed, PressedHot }

/// <summary>A task button: a window's (its title) or a group's (its count and program).</summary>
public sealed record XpTaskSlot(int X, int Width, string Title, int? GroupCount, XpButtonState State, bool Flashing);

/// <summary>
/// The notification area: its left edge, whether it has the chevron (and which way it points: expanded shows hidden
/// icons and points right), and the clock.
/// </summary>
public sealed record XpTraySlot(int X, bool? ChevronExpanded, bool ChevronHot, string Clock, int ClockX);

/// <summary>Everything drawn on one XP taskbar; icons are shown over it.</summary>
public sealed record XpTaskbarScene(
    int Width,
    XpButtonState Start,
    IReadOnlyList<(int X, XpButtonState State)> QuickLaunch,
    (int X, XpButtonState State)? QuickLaunchChevron,
    IReadOnlyList<XpTaskSlot> Tasks,
    XpTraySlot? Tray);

/// <summary>Draws XP's taskbar from its measured art (see <see cref="XpArt"/>).</summary>
public static class XpTaskbarPainter
{
    public static void Paint(XpCanvas canvas, XpTaskbarScene scene)
    {
        for (int row = 0; row < Height; row++)
            canvas.Fill(0, row, scene.Width, 1, XpArt.TaskbarRows[row]);
        PaintStart(canvas, scene.Start);
        foreach ((int x, XpButtonState state) in scene.QuickLaunch)
            PaintToolbarButton(canvas, x, QuickLaunchButton, state);
        if (scene.QuickLaunchChevron is { } chevron)
        {
            // The chevron's frame is inset in its slot (4 left, 2 right), its two arrows 8 in.
            PaintToolbarButton(canvas, chevron.X + 4, QuickLaunchButton - 6, chevron.State);
            for (int row = 0; row < XpArt.QuickLaunchChevron.Length; row++)
            {
                for (int column = 0; column < XpArt.QuickLaunchChevron[row].Length; column++)
                {
                    if (XpArt.QuickLaunchChevron[row][column] == '#')
                        canvas.Set(chevron.X + 8 + column, 7 + row, 0x7BB6E8);
                }
            }
        }
        foreach (XpTaskSlot task in scene.Tasks)
            PaintTask(canvas, task);
        if (scene.Tray is { } tray)
            PaintTray(canvas, tray, scene.Width);
    }

    private static void PaintStart(XpCanvas canvas, XpButtonState state)
    {
        uint[][] mesh = state switch
        {
            XpButtonState.Normal => XpArt.StartMeshNormal,
            XpButtonState.Hot => XpArt.StartMeshHot,
            _ => XpArt.StartMeshPressed,
        };
        int[] nodes = XpArt.StartMeshNodes;
        for (int y = 0; y < Height; y++)
        {
            int node = 0;
            for (int x = 0; x < StartWidth; x++)
            {
                while (node < nodes.Length - 2 && x >= nodes[node + 1])
                    node++;
                float t = Math.Clamp((x - nodes[node]) / (float)(nodes[node + 1] - nodes[node]), 0, 1);
                canvas.Set(x, y, Mix(mesh[y][node], mesh[y][node + 1], t));
            }
        }
        canvas.PatchOver(XpArt.Flag, 10, 4);
        canvas.ShadowedText("start", 35, 3, 60, 27, XpArt.StartFont, XpArt.White, 0x304010, 0.9);
    }

    private static void PaintToolbarButton(XpCanvas canvas, int x, int width, XpButtonState state)
    {
        if (state == XpButtonState.Normal)
            return;
        canvas.Band(state == XpButtonState.Hot ? XpArt.ToolbarHot : XpArt.ToolbarPressed, x, ButtonTop, width);
    }

    private static void PaintTask(XpCanvas canvas, XpTaskSlot task)
    {
        XpBand art = task.State switch
        {
            XpButtonState.Normal => XpArt.TaskButtonNormal,
            XpButtonState.Hot => XpArt.TaskButtonHot,
            XpButtonState.Pressed => XpArt.TaskButtonPressed,
            _ => XpArt.TaskButtonPressedHot,
        };
        if (task.Flashing && task.State is XpButtonState.Normal or XpButtonState.Hot)
            art = XpArt.TaskButtonFlashing;
        canvas.Band(art, task.X, ButtonTop, task.Width);

        int shift = task.State is XpButtonState.Pressed or XpButtonState.PressedHot ? 1 : 0;
        int top = ButtonTop + LabelTop + shift;
        int right = task.X + task.Width - LabelRight;
        if (task.GroupCount is { } count)
        {
            string number = count.ToString(System.Globalization.CultureInfo.CurrentCulture);
            int countX = task.X + CountLeft + shift;
            canvas.Text(number, countX, top, right - countX, 14, XpArt.TahomaBold, XpArt.GroupCount);
            int nameX = countX + Interop.Imaging.GdiText.Measure(number, XpArt.TahomaBold) + CountGap;
            // The group's arrow takes the right end.
            canvas.Text(task.Title, nameX, top, right - 10 - nameX, 14, XpArt.Tahoma, XpArt.White);
            PaintGroupArrow(canvas, task.X + task.Width - 9 + shift, ButtonTop + 11 + shift);
        }
        else
        {
            int labelX = task.X + LabelLeft + shift;
            canvas.Text(task.Title, labelX, top, right - labelX, 14, XpArt.Tahoma, XpArt.White);
        }
    }

    // A small white triangle pointing down: 5, 3 and 1 pixels.
    private static void PaintGroupArrow(XpCanvas canvas, int x, int y)
    {
        for (int row = 0; row < 3; row++)
            canvas.Fill(x + row, y + row, 5 - 2 * row, 1, XpArt.White);
    }

    private static void PaintTray(XpCanvas canvas, XpTraySlot tray, int width)
    {
        canvas.Band(XpArt.Tray, tray.X, 0, width - tray.X);
        if (tray.ChevronExpanded is { } expanded)
        {
            uint[][] chevron = (expanded, tray.ChevronHot) switch
            {
                (false, false) => XpArt.ChevronLeft,
                (false, true) => XpArt.ChevronLeftHot,
                (true, false) => XpArt.ChevronRight,
                _ => XpArt.ChevronRightHot,
            };
            canvas.Patch(chevron, tray.X - 10, 4);
        }
        canvas.Text(tray.Clock, tray.ClockX, ClockTop, width - tray.ClockX, 14, XpArt.Tahoma, XpArt.White);
    }

    private static uint Mix(uint a, uint b, float t)
    {
        uint Channel(int shift) => (uint)MathF.Round(((a >> shift) & 255) * (1 - t) + ((b >> shift) & 255) * t);
        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }
}
