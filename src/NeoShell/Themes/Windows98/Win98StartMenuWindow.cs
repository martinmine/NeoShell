using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NeoShell.Desktop;
using NeoShell.Interop.Imaging;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.StartMenu;
using NeoShell.Taskbar;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using static NeoShell.Themes.Windows98.Win98StartMenuPainter;

namespace NeoShell.Themes.Windows98;

/// <summary>
/// Windows 98's Start menu (see docs/design/theme-98.md): the "Windows98" banner and the top-level items, opening above
/// the Start button. The groundwork: the items that open a folder, a tool or the power dialog do; the submenus
/// (Programs, Favorites, Documents, Settings, Find) and 98's slide aren't built yet.
/// </summary>
internal sealed class Win98StartMenuWindow : IStartMenuView
{
    // 98's items, top to bottom (null: a separator), with their icons from shell32.dll.
    private static readonly (string Key, string Text, bool Submenu, string? Icon)?[] s_items =
    [
        ("update", "Windows Update", false, @"%SystemRoot%\System32\shell32.dll,46"),
        null,
        ("programs", "&Programs", true, @"%SystemRoot%\System32\shell32.dll,19"),
        ("favorites", "F&avorites", true, @"%SystemRoot%\System32\shell32.dll,43"),
        ("documents", "&Documents", true, @"%SystemRoot%\System32\shell32.dll,20"),
        ("settings", "&Settings", true, @"%SystemRoot%\System32\shell32.dll,21"),
        ("find", "&Find", true, @"%SystemRoot%\System32\shell32.dll,22"),
        ("help", "&Help", false, @"%SystemRoot%\System32\shell32.dll,23"),
        ("run", "&Run...", false, @"%SystemRoot%\System32\shell32.dll,24"),
        null,
        ("logoff", "&Log Off {0}...", false, @"%SystemRoot%\System32\shell32.dll,44"),
        ("shutdown", "Sh&ut Down...", false, @"%SystemRoot%\System32\shell32.dll,27"),
    ];

    private readonly Taskbars _owner;
    private readonly ThemePopup _popup = new(activates: true);
    private Win98StartLayout? _layout;
    private ITaskbarView? _taskbar;
    private string? _hot;
    private nint _previousForeground;
    private long _deactivatedAt;

    public Win98StartMenuWindow(Taskbars owner)
    {
        _owner = owner;
        _popup.Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && IsOpen)
            {
                _deactivatedAt = Environment.TickCount64;
                Hide(restoreForeground: false);
            }
        };
        _popup.Root.KeyDown += Root_KeyDown;
        _popup.Root.PointerExited += (_, _) => SetHot(null);
    }

    public bool IsOpen { get; private set; }

    public bool WasJustDeactivated => Environment.TickCount64 - _deactivatedAt < 400;

    public void Show(ITaskbarView taskbar)
    {
        _taskbar = taskbar;
        _hot = null;
        Build();
        Paint();

        // Start's bottom edge is the row above the Start button, over the taskbar's top edge.
        double scale = taskbar.Monitor.Dpi / 96.0;
        RectInt32 bar = taskbar.ScreenBounds;
        int bottom = bar.Y + (int)Math.Round(Win98TaskbarLayout.ButtonTop * scale);
        _previousForeground = TopLevelWindows.GetForeground();
        IsOpen = true;
        _popup.ShowAt(new PointInt32(bar.X + (int)Math.Round(Win98TaskbarLayout.StartLeft * scale), bottom - (int)Math.Round(_layout!.Height * scale)), scale);
        (taskbar as Win98TaskbarWindow)?.ShowStartOpen(true);
    }

    public void Hide() => Hide(restoreForeground: true);

    public void Close()
    {
        Hide(restoreForeground: false);
        _popup.Shut();
    }

    private void Hide(bool restoreForeground)
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        if (restoreForeground && TopLevelWindows.GetForeground() == _popup.Handle && _previousForeground != 0 && TopLevelWindows.Exists(_previousForeground))
            TopLevelWindows.Activate(_previousForeground);
        _popup.Hide();
        (_taskbar as Win98TaskbarWindow)?.ShowStartOpen(false);
    }

    private void Build()
    {
        string user = UserAccount.DisplayName;
        IReadOnlyList<(string Key, string Text, bool Submenu)?> entries =
            [.. s_items.Select(item => item is { } i ? (i.Key, string.Format(i.Text, user), i.Submenu) : ((string, string, bool)?)null)];
        int textWidth = entries.Max(entry => entry is { } e ? GdiText.Measure(e.Text.Replace("&", ""), Win98Art.Font) : 0);
        _layout = Layout(entries, textWidth);

        _popup.Overlay.Children.Clear();
        foreach (Win98StartItem item in _layout.Items)
        {
            Button hit = ThemeHit.Create($"{char.ToUpperInvariant(item.Key[0])}{item.Key[1..]}MenuItem", item.Text.Replace("&", ""));
            ThemeHit.Place(hit, ItemLeft, item.Top, _layout.Width - ItemRight - ItemLeft + 1, ItemHeight);
            hit.PointerEntered += (_, _) => SetHot(item.Key);
            hit.Click += (_, _) => Choose(item.Key);
            _popup.Overlay.Children.Add(hit);
            if (s_items.First(entry => entry?.Key == item.Key)?.Icon is { } icon)
                _ = AddIcon(ThemeIcons.FromResource(icon, 32), IconLeft, item.Top);
        }
    }

    private async Task AddIcon(Task<ImageSource?> icon, int x, int y)
    {
        if (await icon is not { } source)
            return;
        var image = new Image { Source = source, IsHitTestVisible = false };
        ThemeHit.Place(image, x, y, 32, 32);
        _popup.Overlay.Children.Insert(0, image);
    }

    private void SetHot(string? key)
    {
        if (_hot == key)
            return;
        _hot = key;
        Paint();
    }

    private void Paint()
    {
        if (_layout is not null)
            _popup.Draw(Win98StartMenuPainter.Paint(_layout, _hot));
    }

    private void Choose(string key)
    {
        string? target = key switch
        {
            "update" => "ms-settings:windowsupdate",
            "help" => "ms-contact-support:",
            _ => null,
        };
        // The submenus aren't built yet: their items stay open and do nothing.
        if (target is null && key is not ("run" or "logoff" or "shutdown"))
            return;

        Hide(restoreForeground: false);
        if (key == "run")
        {
            _owner.ShowRunDialog();
        }
        else if (key is "logoff" or "shutdown")
        {
            // 98's own Log Off and Shut Down Windows dialogs are still to come; Windows 11's offers the same choices.
            ShutDownDialog.Open(_owner.Theme);
        }
        else
        {
            try
            {
                ShellLaunch.Open(target!);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not open {target}", ex);
            }
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }
}
