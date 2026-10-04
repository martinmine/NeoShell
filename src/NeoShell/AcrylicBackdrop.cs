using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NeoShell;

/// <summary>
/// Desktop acrylic that stays acrylic while the window is inactive. WinUI's own <c>DesktopAcrylicBackdrop</c> turns
/// into a solid colour then, and shell surfaces like the taskbar are inactive nearly all the time.
/// </summary>
internal sealed class AcrylicBackdrop : SystemBackdrop
{
    private readonly SystemBackdropConfiguration _configuration = new() { IsInputActive = true };
    private DesktopAcrylicController? _controller;
    private Color? _tint;

    public ElementTheme Theme
    {
        set => _configuration.Theme = value == ElementTheme.Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
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
        _controller = new DesktopAcrylicController();
        _controller.AddSystemBackdropTarget(target);
        _controller.SetSystemBackdropConfiguration(_configuration);
        ApplyTint();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
    }

    private void ApplyTint()
    {
        if (_controller is null)
            return;

        if (_tint is { } tint)
        {
            // Mostly colour, as on Windows' own taskbar, with a little of what's behind showing through.
            _controller.TintColor = tint;
            _controller.TintOpacity = 0.75f;
            _controller.LuminosityOpacity = 0.9f;
        }
        else
        {
            _controller.ResetProperties();
        }
    }
}
