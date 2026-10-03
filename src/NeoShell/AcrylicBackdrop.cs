using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace NeoShell;

/// <summary>
/// Desktop acrylic that stays acrylic while the window is inactive. WinUI's own <c>DesktopAcrylicBackdrop</c> turns
/// into a solid colour then, and shell surfaces like the taskbar are inactive nearly all the time.
/// </summary>
internal sealed class AcrylicBackdrop : SystemBackdrop
{
    private readonly SystemBackdropConfiguration _configuration = new() { IsInputActive = true };
    private DesktopAcrylicController? _controller;

    public ElementTheme Theme
    {
        set => _configuration.Theme = value == ElementTheme.Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        _controller = new DesktopAcrylicController();
        _controller.AddSystemBackdropTarget(target);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
    }
}
