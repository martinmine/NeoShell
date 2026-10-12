using Microsoft.UI.Dispatching;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Windowing;
using Windows.Graphics;

namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// XP's tooltip: Tahoma on the yellow info colour in a black border, with a soft shadow. It shows half a second after
/// the pointer settles on something (the double-click time, XP's initial delay), at once when moving between things
/// while one shows, and goes after five seconds.
/// </summary>
internal sealed class XpToolTip
{
    private const int MaxTextWidth = 400;
    private static readonly TimeSpan s_initialDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan s_reshowDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan s_showTime = TimeSpan.FromSeconds(5);

    private readonly ThemePopup _popup = new(activates: false);
    private readonly DispatcherQueueTimer _showTimer;
    private readonly DispatcherQueueTimer _hideTimer;
    private (string Text, Func<(PointInt32 Anchor, bool Above, DisplayMonitor Monitor)> Place)? _pending;

    public XpToolTip()
    {
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        _showTimer = dispatcher.CreateTimer();
        _showTimer.IsRepeating = false;
        _showTimer.Tick += (_, _) => ShowPending();
        _hideTimer = dispatcher.CreateTimer();
        _hideTimer.IsRepeating = false;
        _hideTimer.Interval = s_showTime;
        _hideTimer.Tick += (_, _) => _popup.Hide();
        // Moving onto the tip itself must not keep it: it goes as the pointer leaves what it's about.
        _popup.Root.IsHitTestVisible = false;
    }

    public bool IsShown => _popup.IsShown;

    /// <summary>
    /// Shows <paramref name="text"/> after the delay. <paramref name="place"/> says where when it shows: the tip's top
    /// left at the anchor, or its bottom left when <c>Above</c> (over the taskbar).
    /// </summary>
    public void Request(string text, Func<(PointInt32 Anchor, bool Above, DisplayMonitor Monitor)> place)
    {
        _pending = (text, place);
        _showTimer.Interval = _popup.IsShown ? s_reshowDelay : s_initialDelay;
        _showTimer.Start();
    }

    /// <summary>The pointer left what the tip is about, or pressed it.</summary>
    public void Cancel()
    {
        _pending = null;
        _showTimer.Stop();
        _hideTimer.Stop();
        _popup.Hide();
    }

    public void Shut()
    {
        _showTimer.Stop();
        _hideTimer.Stop();
        _popup.Shut();
    }

    private void ShowPending()
    {
        if (_pending is not { } pending || pending.Text.Length == 0)
            return;

        (PointInt32 anchor, bool above, DisplayMonitor monitor) = pending.Place();
        double scale = monitor.Dpi / 96.0;
        ThemeCanvas canvas = Paint(pending.Text);
        _popup.Draw(canvas);
        // The shadow's pixels are see-through margin; the tip itself is what's placed.
        int width = (int)Math.Round((canvas.Width - Shadow) * scale), height = (int)Math.Round((canvas.Height - Shadow) * scale);
        RectInt32 screen = monitor.Bounds;
        int x = Math.Clamp(anchor.X, screen.X, screen.X + screen.Width - width);
        int y = above ? anchor.Y - height : anchor.Y;
        if (!above && y + height > screen.Y + screen.Height)
            y = anchor.Y - height - (int)(24 * scale);
        _popup.ShowAt(new PointInt32(x, Math.Max(screen.Y, y)), scale);
        _hideTimer.Start();
    }

    private static int Shadow => XpArt.DropShadow.Length;

    /// <summary>The tip with its shadow: lines (wrapped at 400 pixels) 13 apart, 3 in from the border's left, 2 from its top.</summary>
    public static ThemeCanvas Paint(string text)
    {
        IReadOnlyList<string> lines = Wrap(text, MaxTextWidth);
        int textWidth = lines.Max(line => GdiText.Measure(line, XpArt.Tahoma));
        int width = textWidth + 6, height = lines.Count * 13 + 4;
        var canvas = new ThemeCanvas(width + Shadow, height + Shadow);
        XpPainting.DropShadow(canvas, 0, 0, width, height);
        canvas.Fill(0, 0, width, height, 0x000000);
        canvas.Fill(1, 1, width - 2, height - 2, XpArt.ToolTipBackground);
        for (int i = 0; i < lines.Count; i++)
            canvas.Text(lines[i], 3, 2 + i * 13, textWidth + 1, 13, XpArt.Tahoma, 0x000000);
        return canvas;
    }

    /// <summary>Lines no wider than <paramref name="maxWidth"/>, broken between words (a line break in the text starts a new one).</summary>
    public static IReadOnlyList<string> Wrap(string text, int maxWidth)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r", "").Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string longer = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && GdiText.Measure(longer, XpArt.Tahoma) > maxWidth)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = longer;
                }
            }
            lines.Add(line);
        }
        return lines;
    }
}
