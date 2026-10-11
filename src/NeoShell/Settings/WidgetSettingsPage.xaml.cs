using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using NeoShell.Taskbar;
using NeoShell.Widgets;

namespace NeoShell.Settings;

/// <summary>The widget sidebar's settings, which its right-click menu and the taskbar's held until this window.</summary>
internal sealed partial class WidgetSettingsPage : SettingsPageView
{
    public WidgetSettingsPage(Taskbars taskbars)
        : base(taskbars)
    {
        InitializeComponent();
        WidthSlider.Minimum = SidebarLayout.MinWidth;
        WidthSlider.Maximum = SidebarLayout.MaxWidth;
    }

    protected override void Load()
    {
        SidebarToggle.IsOn = Current.ShowWidgetSidebar;
        PanelToggle.IsOn = Current.ShowWidgetPanel;
        WidthSlider.Value = Current.WidgetSidebarWidth;
    }

    private void SidebarToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsFilling)
            Save(Current with { ShowWidgetSidebar = SidebarToggle.IsOn });
    }

    private void PanelToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsFilling)
            Save(Current with { ShowWidgetPanel = PanelToggle.IsOn });
    }

    // The sidebar follows each step of a drag, as it does its edge being dragged.
    private void WidthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!IsFilling)
            Save(Current with { WidgetSidebarWidth = Math.Clamp(e.NewValue, SidebarLayout.MinWidth, SidebarLayout.MaxWidth) });
    }
}
