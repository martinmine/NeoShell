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
internal sealed class ShellBackdrop(Backdrop kind) : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? s_compositor;

    private readonly SystemBackdropConfiguration _configuration = new() { IsInputActive = true };
    private DesktopAcrylicController? _acrylic;
    private MicaController? _mica;
    private Windows.UI.Composition.CompositionColorBrush? _brush;
    private Color? _tint;

    public Backdrop Kind { get; } = kind == Backdrop.Mica && !MicaController.IsSupported() ? Backdrop.Acrylic : kind;

    public ElementTheme Theme
    {
        set
        {
            _configuration.Theme = value == ElementTheme.Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
            ApplyTint();
        }
    }

    /// <summary>A colour in place of the theme's grey: the accent colour, when Windows shows it on Start and taskbar.</summary>
    public Color? Tint
    {
        set
        {
            _tint = value;
            ApplyTint();
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        switch (Kind)
        {
            case Backdrop.Acrylic:
                _acrylic = new DesktopAcrylicController();
                _acrylic.AddSystemBackdropTarget(target);
                _acrylic.SetSystemBackdropConfiguration(_configuration);
                break;
            case Backdrop.Mica:
                _mica = new MicaController();
                _mica.AddSystemBackdropTarget(target);
                _mica.SetSystemBackdropConfiguration(_configuration);
                break;
            case Backdrop.Translucent:
            case Backdrop.Transparent:
                // The system backdrop slot takes a brush from the system compositor, not WinUI's.
                s_compositor ??= new Windows.UI.Composition.Compositor();
                _brush = s_compositor.CreateColorBrush();
                target.SystemBackdrop = _brush;
                break;
        }
        ApplyTint();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        _acrylic?.RemoveSystemBackdropTarget(target);
        _acrylic?.Dispose();
        _acrylic = null;
        _mica?.RemoveSystemBackdropTarget(target);
        _mica?.Dispose();
        _mica = null;
        if (_brush is not null)
        {
            target.SystemBackdrop = null;
            _brush.Dispose();
            _brush = null;
        }
    }

    private void ApplyTint()
    {
        // Mostly colour, as on Windows' own taskbar, with a little of what's behind showing through.
        const float tintOpacity = 0.75f;
        const float luminosityOpacity = 0.9f;
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
