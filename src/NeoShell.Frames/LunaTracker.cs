using NeoShell.Interop.Imaging;
using NeoShell.Interop.Windowing;
using Windows.Graphics;

namespace NeoShell.Frames;

/// <summary>
/// XP's Luna frame over one app window: five overlay windows owned by it (the caption, the left, right and bottom
/// borders, and the caption buttons) following it as it moves, sizes, activates, minimizes and restores, and Luna blue
/// set on the app's own caption underneath, which is what shows when a fast move outruns the overlays. Lives on the
/// overlay thread of the window's app thread (<see cref="AppThread"/>).
/// </summary>
internal sealed class LunaTracker : IDisposable
{
    // The restore animation's length: the frame shows again once the window has its place.
    private static readonly TimeSpan s_restoreDelay = TimeSpan.FromMilliseconds(300);
    // Windows marks a window active on its own WM_NCACTIVATE, which can come after the foreground event.
    private static readonly TimeSpan s_activationRecheck = TimeSpan.FromMilliseconds(250);

    private readonly nint _target;
    private readonly uint _threadId;
    private readonly MessageLoop _loop;
    // Bottom to top, as they're stacked above the app.
    private readonly OverlayWindow _bottom, _right, _left, _caption, _buttons;
    private LunaLayout? _layout;
    // The read _layout was made from.
    private WindowCaption? _window;
    private (LunaButton Button, LunaButtonState State)[] _buttonStates = [];
    private RectInt32[] _buttonRects = [];
    private string _title;
    private uint[]? _icon;
    private bool _active;
    private bool? _attributesActive;
    private bool _redraw = true;
    // Minimized, or restoring: the overlays wait off screen until the window has its place.
    private bool _parked;
    // The app stopped responding, and Windows shows its ghost over it with Windows' frame.
    private bool _hung;
    private LunaButton? _hot, _pressed;
    private bool _disposed;

    /// <exception cref="System.ComponentModel.Win32Exception">The window can't be owned (access denied).</exception>
    public LunaTracker(nint target, MessageLoop loop)
    {
        _target = target;
        _threadId = TopLevelWindows.GetThreadId(target);
        _loop = loop;
        var created = new List<OverlayWindow>();
        try
        {
            foreach (bool clickThrough in (ReadOnlySpan<bool>)[true, true, true, true, false])
                created.Add(new OverlayWindow(target, clickThrough));
        }
        catch
        {
            created.ForEach(window => window.Dispose());
            throw;
        }
        (_bottom, _right, _left, _caption, _buttons) = (created[0], created[1], created[2], created[3], created[4]);
        _buttons.Mouse += OnButtonsMouse;

        FrameProgram.MarkLuna(target);
        _title = WindowInfo.ReadTitle(target);
        ReadIconLater();
        _active = IsActive();
        _parked = TopLevelWindows.IsMinimized(target);
        Update();
        Restack();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (OverlayWindow window in Overlays)
            window.Dispose();
        FrameProgram.PutBack(_target);
    }

    private OverlayWindow[] Overlays => [_bottom, _right, _left, _caption, _buttons];

    public void OnEvent(WindowEvent kind)
    {
        switch (kind)
        {
            case WindowEvent.LocationChanged:
                if (!_parked && !Follow())
                    Update();
                break;
            case WindowEvent.MoveSizeEnd:
                _redraw = true;
                Update();
                break;
            case WindowEvent.NameChanged:
                _title = WindowInfo.ReadTitle(_target);
                ReadIconLater();
                _redraw = true;
                Update();
                break;
            case WindowEvent.Hidden or WindowEvent.Cloaked:
                // Owned windows hide with a minimized owner, not with a hidden or cloaked one (another virtual desktop).
                Hide();
                break;
            case WindowEvent.Shown or WindowEvent.Uncloaked:
                _redraw = true;
                Update();
                Restack();
                break;
            case WindowEvent.MinimizeStart:
                // OverlayWindow parked them as the window minimized, but a paint since (a click on our minimize
                // button paints its release) may have put them back, where the system would show them on restore.
                _parked = true;
                foreach (OverlayWindow window in Overlays)
                    window.Park();
                break;
            case WindowEvent.MinimizeEnd:
                _loop.After(s_restoreDelay, () =>
                {
                    _parked = false;
                    _redraw = true;
                    CheckActive();
                    Update();
                });
                break;
        }
    }

    /// <summary>
    /// Follows the foreground: active when Windows draws the caption active and the app's thread has the foreground
    /// (a thread's last active window keeps <c>WS_ACTIVECAPTION</c> when another app takes over). Called on foreground
    /// changes, once more a moment later, and while the app is in front by a poll (an app recovering from a hang
    /// repaints its caption with no event). Restacks the overlays if something got between them and the window.
    /// </summary>
    public void CheckActive()
    {
        if (_disposed)
            return;
        bool active = IsActive();
        if (active != _active)
        {
            _active = active;
            _redraw = true;
            Update();
        }
        if (!InOrder())
            Restack();
    }

    public void CheckActiveSoon() => _loop.After(s_activationRecheck, CheckActive);

    /// <summary>
    /// Hides the frame while the app doesn't respond: the ghost Windows shows over its window has Windows' frame, which
    /// ours would stick out of. Back when the app is.
    /// </summary>
    public void CheckHung()
    {
        bool hung = TopLevelWindows.IsHung(_target);
        if (hung == _hung)
            return;
        _hung = hung;
        if (hung)
        {
            Hide();
        }
        else
        {
            _redraw = true;
            Update();
        }
    }

    private bool IsActive() =>
        TopLevelWindows.IsCaptionActive(_target) && TopLevelWindows.GetThreadId(TopLevelWindows.GetForeground()) == _threadId;

    private void Update()
    {
        if (_disposed || _parked || _hung || !TopLevelWindows.IsOnScreen(_target))
            return;
        // DWM is asked for the frame again only when there's more to it than a move or a resize.
        if (WindowCaption.Read(_target, _redraw ? null : _window) is not { } window)
            return;

        var states = LunaLayout.ButtonsFor(window, _hot, _pressed);
        if (LunaLayout.From(window, states.Length) is not { } layout)
        {
            Hide();
            _layout = null;
            return;
        }

        _window = window;
        SetAttributes();
        bool redraw = _redraw || _layout is null || layout.Kind != _layout.Kind || layout.Maximized != _layout.Maximized
            || !SameSize(layout.Caption, _layout.Caption) || !SameSize(layout.Left, _layout.Left)
            || !SameSize(layout.Buttons, _layout.Buttons) || !states.SequenceEqual(_buttonStates);
        _layout = layout;
        _buttonStates = states;
        _redraw = false;
        if (redraw)
            Paint(layout);
        else
            Move(layout);
    }

    /// <summary>
    /// Follows a move that kept the window's size by the offset of its rectangle, without asking DWM for the frame
    /// (its answers can wait for its next composition), so the overlays keep up with a drag. False when there's more
    /// to it than a move.
    /// </summary>
    private bool Follow()
    {
        if (_disposed || _layout is null || _window is null || _redraw)
            return false;
        RectInt32 bounds = TopLevelWindows.GetBounds(_target), before = _window.Window;
        if (!SameSize(bounds, before) || TopLevelWindows.IsMaximized(_target) != _layout.Maximized)
            return false;
        if (bounds.X != before.X || bounds.Y != before.Y)
        {
            int dx = bounds.X - before.X, dy = bounds.Y - before.Y;
            _layout = _layout.Offset(dx, dy);
            _window = _window with
            {
                Window = bounds,
                Visible = Offset(_window.Visible, dx, dy),
                Client = Offset(_window.Client, dx, dy),
                TitleBarBottom = _window.TitleBarBottom + dy,
                CaptionButtonsLeft = _window.CaptionButtonsLeft + dx,
            };
            Move(_layout);
        }
        return true;
    }

    private static RectInt32 Offset(RectInt32 rect, int dx, int dy) => new(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);

    private static bool SameSize(RectInt32 a, RectInt32 b) => a.Width == b.Width && a.Height == b.Height;

    private void Paint(LunaLayout layout)
    {
        var luna = new LunaWindow(layout.Caption.Width, layout.Caption.Height, layout.Kind, _active, layout.Maximized, _title,
            _icon, _buttonStates);
        uint[] caption = LunaFrame.RenderCaption(luna);
        _buttonRects = LunaFrame.ButtonRects(luna);
        _caption.Paint(caption, layout.Caption);
        if (layout.Buttons.Width > 0)
            _buttons.Paint(Crop(caption, layout.Caption.Width, layout.Buttons.X - layout.Caption.X, layout.Buttons.Width), layout.Buttons);
        else
            _buttons.Hide();

        if (layout.Maximized || layout.Left.Height <= 0)
        {
            _left.Hide();
            _right.Hide();
            _bottom.Hide();
            return;
        }
        (uint[] left, uint[] right, uint[] bottom) = LunaFrame.RenderBorders(luna, layout.Left.Height);
        _left.Paint(left, layout.Left);
        _right.Paint(right, layout.Right);
        _bottom.Paint(bottom, layout.Bottom);
    }

    private void Move(LunaLayout layout)
    {
        _caption.Move(layout.Caption.X, layout.Caption.Y);
        _buttons.Move(layout.Buttons.X, layout.Buttons.Y);
        if (layout.Maximized)
            return;
        _left.Move(layout.Left.X, layout.Left.Y);
        _right.Move(layout.Right.X, layout.Right.Y);
        _bottom.Move(layout.Bottom.X, layout.Bottom.Y);
    }

    private static uint[] Crop(uint[] pixels, int width, int x, int cropWidth)
    {
        int height = pixels.Length / width;
        var crop = new uint[cropWidth * height];
        for (int y = 0; y < height; y++)
            Array.Copy(pixels, y * width + x, crop, y * cropWidth, cropWidth);
        return crop;
    }

    private void Hide()
    {
        foreach (OverlayWindow window in Overlays)
            window.Hide();
    }

    /// <summary>
    /// The app's own caption in Luna's colours, active or inactive: what shows for a moment when a move outruns the
    /// overlays. Set again on each activation change, as apps may set their own.
    /// </summary>
    private void SetAttributes()
    {
        if (_attributesActive == _active)
            return;
        _attributesActive = _active;
        WindowFrame.Apply(_target, new FrameAttributes
        {
            CaptionColor = _active ? 0xFF0054E3 : 0xFF7A96DF,
            TextColor = _active ? 0xFFFFFFFF : 0xFFD8E4F8,
            BorderColor = _active ? 0xFF0831D9 : 0xFF7480DC,
        });
    }

    /// <summary>
    /// Puts the overlays right above the window: a new owned window starts at the top of the z-order, and an owner made
    /// topmost can leave one of them below it. From then on the system moves them with their owner.
    /// </summary>
    private void Restack()
    {
        nint above = TopLevelWindows.WindowAbove(_target);
        while (above != 0 && IsOurs(above))
            above = TopLevelWindows.WindowAbove(above);
        // Each goes right below the same window, so the last placed ends up on top.
        foreach (OverlayWindow window in Overlays)
            window.PlaceBelow(above);
    }

    /// <summary>Whether the overlays lie right above the window, in their order.</summary>
    private bool InOrder()
    {
        nint window = _target;
        foreach (OverlayWindow overlay in Overlays)
        {
            window = TopLevelWindows.WindowAbove(window);
            if (window != overlay.Handle)
                return false;
        }
        return true;
    }

    private bool IsOurs(nint hwnd) => Array.Exists(Overlays, overlay => overlay.Handle == hwnd);

    /// <summary>The title bar icon, read off this thread: asking a busy app for it can take up to the timeout.</summary>
    private void ReadIconLater() =>
        ThreadPool.QueueUserWorkItem(_ =>
        {
            IconBitmap? icon = WindowInfo.ReadSmallIcon(_target);
            uint[]? pixels = icon is null ? null : LunaFrame.Icon16(icon.Width, icon.Height, icon.Pixels);
            _loop.Post(() =>
            {
                _icon = pixels;
                _redraw = true;
                Update();
            });
        });

    private void OnButtonsMouse(OverlayMouse kind, PointInt32 point)
    {
        if (_layout is null)
            return;
        // To the caption's coordinates, where the buttons were laid out.
        var at = new PointInt32(point.X + _layout.Buttons.X - _layout.Caption.X, point.Y + _layout.Buttons.Y - _layout.Caption.Y);
        int index = Array.FindIndex(_buttonRects, rect =>
            at.X >= rect.X && at.X < rect.X + rect.Width && at.Y >= rect.Y && at.Y < rect.Y + rect.Height);
        LunaButton? over = index >= 0 && index < _buttonStates.Length ? _buttonStates[index].Button : null;
        bool enabled = index >= 0 && index < _buttonStates.Length && _buttonStates[index].State != LunaButtonState.Disabled;

        switch (kind)
        {
            case OverlayMouse.Move:
                SetHot(over);
                break;
            case OverlayMouse.Leave:
                SetHot(null);
                break;
            case OverlayMouse.Press or OverlayMouse.DoubleClick when over is { } button:
                if (enabled)
                {
                    _pressed = button;
                    _buttons.Capture(true);
                    Update();
                }
                break;
            case OverlayMouse.Press:
                // Left of the buttons, over Windows' own: still the caption.
                CaptionInput.BeginDrag(_target);
                break;
            case OverlayMouse.DoubleClick:
                CaptionInput.DoubleClick(_target);
                break;
            case OverlayMouse.Release when _pressed is { } pressed:
                _buttons.Capture(false);
                _pressed = null;
                if (over == pressed)
                    CaptionInput.Send(_target, Command(pressed));
                Update();
                break;
            case OverlayMouse.RightClick when over is null:
                CaptionInput.OpenWindowMenu(_target);
                break;
        }
    }

    private void SetHot(LunaButton? button)
    {
        if (button == _hot)
            return;
        _hot = button;
        Update();
    }

    private static CaptionCommand Command(LunaButton button) => button switch
    {
        LunaButton.Minimize => CaptionCommand.Minimize,
        LunaButton.Maximize => CaptionCommand.Maximize,
        LunaButton.Restore => CaptionCommand.Restore,
        LunaButton.Help => CaptionCommand.Help,
        _ => CaptionCommand.Close,
    };
}
