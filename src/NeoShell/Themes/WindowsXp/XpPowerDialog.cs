using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;

namespace NeoShell.Themes.WindowsXp;

/// <summary>
/// XP's Log Off Windows and Turn off computer dialogs, approximated (docs/design/theme-xp.md): a dark blue box with its
/// title and the Windows flag, a lighter band with the choices as big round-cornered buttons, and Cancel. The choices
/// run Windows 11's: Switch User, Log Off (sign out), Stand By (sleep; hibernate with Shift, as XP), Turn Off, Restart.
/// XP also greyed the screen behind; this doesn't.
/// </summary>
internal sealed class XpPowerDialog
{
    private const int Width = 314, Height = 200;
    private const uint Blue = 0x003399;

    private static XpPowerDialog? s_open;

    private readonly XpPopup _popup = new(activates: true);
    private readonly string _title;
    private readonly (string Key, string Text, PowerChoice Choice, uint Color, string Glyph)[] _choices;
    private readonly XpToolTip _toolTip = new();
    private string? _hot;

    private XpPowerDialog(bool turnOff)
    {
        _title = turnOff ? "Turn off computer" : "Log Off Windows";
        _choices = turnOff
            ?
            [
                ("standby", "Stand By", PowerChoice.Sleep, 0xE8B23A, ""),
                ("turnoff", "Turn Off", PowerChoice.ShutDown, 0xE35B3A, ""),
                ("restart", "Restart", PowerChoice.Restart, 0x3C9E3C, ""),
            ]
            :
            [
                ("switchuser", "Switch User", PowerChoice.SwitchUser, 0x3C9E3C, ""),
                ("logoff", "Log Off", PowerChoice.SignOut, 0xE89A2E, ""),
            ];
        int count = _choices.Length;
        for (int i = 0; i < count; i++)
        {
            (string key, string text, PowerChoice choice, _, _) = _choices[i];
            Button hit = XpHit.Create(key == "turnoff" ? "TurnOffDialogButton" : $"{choice}DialogButton", text);
            XpHit.Place(hit, ChoiceX(i) - 20, 78, 72, 56);
            hit.PointerEntered += (_, _) =>
            {
                _hot = key;
                Paint();
                _toolTip.Request(PowerItems.Description(choice), () =>
                    (new PointInt32(Cursor.Position().X, Cursor.Position().Y + 20), false, MonitorOf()));
            };
            hit.PointerExited += (_, _) =>
            {
                _hot = null;
                _toolTip.Cancel();
                Paint();
            };
            hit.Click += (_, _) =>
            {
                PowerChoice run = choice == PowerChoice.Sleep && PowerItems.IsShiftDown() ? PowerChoice.Hibernate : choice;
                Close();
                PowerItems.Run(run, shift: false);
            };
            _popup.Overlay.Children.Add(hit);
        }
        Button cancel = XpHit.Create("CancelDialogButton", "Cancel");
        XpHit.Place(cancel, 244, 169, 58, 19);
        cancel.Click += (_, _) => Close();
        _popup.Overlay.Children.Add(cancel);
        _popup.Root.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
                Close();
        };
        _popup.Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                Close();
        };
        Paint();
    }

    /// <summary>Opens the dialog in the middle of the monitor, or brings back the open one.</summary>
    public static void Open(bool turnOff, DisplayMonitor monitor)
    {
        s_open?.Close();
        var dialog = new XpPowerDialog(turnOff);
        s_open = dialog;
        double scale = monitor.Dpi / 96.0;
        RectInt32 screen = monitor.Bounds;
        dialog._popup.ShowAt(new PointInt32(screen.X + (screen.Width - (int)(Width * scale)) / 2,
            screen.Y + (screen.Height - (int)(Height * scale)) / 2), scale);
    }

    private DisplayMonitor MonitorOf() =>
        DisplayMonitor.GetAll().FirstOrDefault(m => m.Handle == DisplayMonitor.HandleFromRect(_popup.ScreenBounds, nearest: true))
        ?? DisplayMonitor.GetAll()[0];

    // The choices spread across the middle band: XP's two at 84 and 199, three at 56, 141 and 226.
    private int ChoiceX(int index) => _choices.Length == 2 ? 84 + index * 115 : 56 + index * 85;

    private void Close()
    {
        _toolTip.Shut();
        _popup.Shut();
        if (s_open == this)
            s_open = null;
    }

    private void Paint()
    {
        var canvas = new XpCanvas(Width, Height);
        canvas.Fill(0, 0, Width, Height, Blue);
        // The middle band: lightest in the middle (96B5F5), darker towards the ends (5A7EDE), as measured on XP.
        for (int x = 1; x < Width - 1; x++)
        {
            double t = Math.Abs(x - Width / 2.0) / (Width / 2.0);
            canvas.Fill(x, 46, 1, 112, XpPainting.Mix(0x96B5F5, 0x5A7EDE, t * t));
        }
        canvas.Fill(0, 45, Width, 1, 0x5A7EDE);
        canvas.Text(_title, 12, 10, 240, 24, new GdiFont("Trebuchet MS", 19, Bold: false), 0xFFFFFF);
        canvas.PatchOver(XpArt.Flag, 270, 8);
        for (int i = 0; i < _choices.Length; i++)
        {
            (string key, string text, _, uint color, string glyph) = _choices[i];
            int x = ChoiceX(i);
            PaintChoice(canvas, x, 81, color, glyph, key == _hot);
            int textWidth = GdiText.Measure(text, XpArt.TahomaBold);
            canvas.Text(text, x + 16 - textWidth / 2, 120, textWidth + 2, 13, XpArt.TahomaBold, 0xFFFFFF);
        }
        // Cancel: a plain XP push button.
        canvas.Fill(244, 169, 58, 19, 0x003C74);
        canvas.Fill(245, 170, 56, 17, 0xFFFFFF);
        canvas.Fill(245, 184, 56, 3, 0xD6D0C5);
        int cancelWidth = GdiText.Measure("Cancel", XpArt.Tahoma);
        canvas.Text("Cancel", 273 - cancelWidth / 2, 172, cancelWidth + 2, 13, XpArt.Tahoma, 0x000000);
        _popup.Draw(canvas);
    }

    // A choice's button: a 32-pixel square with rounded corners, a light rim, a top-lit face in its colour and a white glyph.
    private static void PaintChoice(XpCanvas canvas, int x, int y, uint color, string glyph, bool hot)
    {
        const int Size = 32;
        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column < Size; column++)
            {
                bool corner = (row is 0 or Size - 1) && (column is 0 or Size - 1);
                if (corner)
                    continue;
                bool rim = row is 0 or Size - 1 || column is 0 or Size - 1;
                double light = 0.45 * (1 - row / (double)Size);
                uint face = XpPainting.Mix(color, 0xFFFFFF, hot ? light + 0.15 : light);
                canvas.Set(x + column, y + row, rim ? 0xFFFFFF : face);
            }
        }
        canvas.Text(glyph, x + 6, y + 6, 22, 22, new GdiFont("Segoe Fluent Icons", 20, Bold: false), 0xFFFFFF);
    }
}
