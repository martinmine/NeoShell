using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoShell.Settings;
using Windows.UI;

namespace NeoShell;

/// <summary>
/// The backdrop of NeoShell's surfaces. Acrylic and Mica stay on while the window is inactive: WinUI's own
/// <c>DesktopAcrylicBackdrop</c> and <c>MicaBackdrop</c> turn into a solid colour then, and shell surfaces like the
/// taskbar are inactive nearly all the time. Translucent is a plain see-through colour, without blur; Transparent
/// shows the desktop as it is.
/// </summary>
internal sealed partial class ShellBackdrop(Backdrop kind) : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? s_compositor;

    private readonly SystemBackdropConfiguration _configuration = new() { IsInputActive = true };
    private DesktopAcrylicController? _acrylic;
    private MicaController? _mica;
    private Windows.UI.Composition.CompositionColorBrush? _brush;
    private Color? _tint;
    private int _targets;

    public Backdrop Kind { get; } = kind == Backdrop.Mica && !MicaController.IsSupported() ? Backdrop.Acrylic : kind;

    public ElementTheme Theme
    {
        set
        {
            _configuration.Theme = value == ElementTheme.Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
            ApplyTint();
        }
    }

    /// <summary>How much of <see cref="Tint"/> and of its luminosity covers what's behind (0.75 and 0.9 unless set).</summary>
    public (float Tint, float Luminosity) TintOpacities { get; init; } = (0.75f, 0.9f);

    /// <summary>A colour in place of the theme's grey: the accent colour, when Windows shows it on Start and taskbar.</summary>
    public Color? Tint
    {
        set
        {
            _tint = value;
            ApplyTint();
        }
    }

    // A menu's backdrop is shared by its submenus' popups, so one backdrop can have several targets at once. Each
    // gets the same controller (or brush), which goes with the last of them: a controller left behind closes itself
    // when the dispatcher shuts down and touches a popup that's gone (a crash in CPopup::GetSystemBackdrop).
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        _targets++;
        switch (Kind)
        {
            case Backdrop.Acrylic:
                if (_acrylic is null)
                {
                    _acrylic = new DesktopAcrylicController();
                    _acrylic.SetSystemBackdropConfiguration(_configuration);
                }
                _acrylic.AddSystemBackdropTarget(target);
                break;
            case Backdrop.Mica:
                if (_mica is null)
                {
                    _mica = new MicaController();
                    _mica.SetSystemBackdropConfiguration(_configuration);
                }
                _mica.AddSystemBackdropTarget(target);
                break;
            case Backdrop.Translucent:
            case Backdrop.Transparent:
                // The system backdrop slot takes a brush from the system compositor, not WinUI's.
                s_compositor ??= new Windows.UI.Composition.Compositor();
                _brush ??= s_compositor.CreateColorBrush();
                target.SystemBackdrop = _brush;
                break;
        }
        ApplyTint();
    }

    // WinUI calls this when the theme of the target's content changes, to update the configuration it hands out
    // through GetDefaultSystemBackdropConfiguration. The controllers here follow NeoShell's own theme and accent
    // (the Theme and Tint setters) instead. The base implementation must not run: WinUI holds the target only
    // weakly, a window's target is already gone by then, and the base throws ArgumentException on the null
    // target, which WinUI turns into a fail-fast (it crashed NeoShell whenever a surface changed theme).
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        _acrylic?.RemoveSystemBackdropTarget(target);
        _mica?.RemoveSystemBackdropTarget(target);
        if (_brush is not null)
            target.SystemBackdrop = null;

        if (--_targets > 0)
            return;
        _acrylic?.Dispose();
        _acrylic = null;
        _mica?.Dispose();
        _mica = null;
        _brush?.Dispose();
        _brush = null;
    }

    private void ApplyTint()
    {
        // Mostly colour, as on Windows' own taskbar, with a little of what's behind showing through.
        (float tintOpacity, float luminosityOpacity) = TintOpacities;
        if (_acrylic is not null)
        {
            if (_tint is { } tint)
            {
                _acrylic.TintColor = tint;
                _acrylic.TintOpacity = tintOpacity;
                _acrylic.LuminosityOpacity = luminosityOpacity;
            }
            else
            {
                _acrylic.ResetProperties();
            }
        }
        else if (_mica is not null)
        {
            if (_tint is { } tint)
            {
                _mica.TintColor = tint;
                _mica.TintOpacity = tintOpacity;
                _mica.LuminosityOpacity = luminosityOpacity;
            }
            else
            {
                _mica.ResetProperties();
            }
        }
        else if (_brush is not null)
        {
            // Just enough of a shade to keep the icons and clock apart from a busy wallpaper.
            _brush.Color = Kind == Backdrop.Transparent ? Color.FromArgb(0, 0, 0, 0)
                : _tint is { } tint ? Color.FromArgb(0x80, tint.R, tint.G, tint.B)
                : _configuration.Theme == SystemBackdropTheme.Light ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x30, 0x00, 0x00, 0x00);
        }
    }
}
