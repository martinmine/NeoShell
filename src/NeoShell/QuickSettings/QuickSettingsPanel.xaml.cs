using System.Collections.ObjectModel;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NeoShell.Interop.Accessibility;
using NeoShell.Interop.Audio;
using NeoShell.Interop.Bluetooth;
using NeoShell.Interop.Display;
using NeoShell.Interop.Network;
using NeoShell.Interop.Power;
using NeoShell.Interop.Radios;
using NeoShell.Logging;
using NeoShell.Settings;
using NeoShell.Tray;

namespace NeoShell.QuickSettings;

/// <summary>The page Quick Settings opens on.</summary>
public enum QuickSettingsPage { Main, SoundOutput, Cast, Project }

/// <summary>
/// Quick Settings: what the network and volume icons open. Lives in the primary taskbar's flyout; the state comes from
/// <see cref="Indicators"/> and is only read while the flyout is open.
/// </summary>
internal sealed partial class QuickSettingsPanel : UserControl
{
    private readonly List<QuickTile> _tiles = [];
    private readonly ObservableCollection<QuickTile> _pageTiles = [];
    private readonly ObservableCollection<MixerApp> _mixerApps = [];
    private readonly ObservableCollection<WifiItem> _wifiNetworks = [];
    private readonly AssistiveFeature[] _assistiveFeatures;
    private Indicators? _indicators;
    private AppIcons? _icons;
    private RunMode _runMode;
    private FrameworkElement _page;
    private int _tilePage;
    private bool _isOpen;
    private bool _updatingVolumeSlider;
    private bool _updatingBrightnessSlider;
    private bool _brightnessSliderPressed;
    private bool _volumeSliderPressed;
    private bool _updatingLists;
    private IReadOnlyList<AudioDevice> _outputs = [];
    private WifiNetworks? _wifi;
    private bool _scanning;
    // Whether the Bluetooth page's list was read with the radio on: it's read again when that changes.
    private bool? _bluetoothListedOn;

    public QuickSettingsPanel()
    {
        InitializeComponent();
        _page = MainPage;
        TileGrid.ItemsSource = _pageTiles;
        MixerList.ItemsSource = _mixerApps;
        WifiList.ItemsSource = _wifiNetworks;
        // Handled events too: the slider handles the pointer itself.
        VolumeSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(VolumeSlider_PointerPressed), handledEventsToo: true);
        VolumeSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(VolumeSlider_PointerReleased), handledEventsToo: true);
        VolumeSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(VolumeSlider_PointerReleased), handledEventsToo: true);
        BrightnessSlider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _brightnessSliderPressed = true), handledEventsToo: true);
        BrightnessSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => _brightnessSliderPressed = false), handledEventsToo: true);
        BrightnessSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => _brightnessSliderPressed = false), handledEventsToo: true);

        _tiles.AddRange(
        [
            new QuickTile(QuickTileKind.WiFi, "Wi-Fi", "") { Name = "Wi-Fi", HasSwitch = true, HasPage = true },
            new QuickTile(QuickTileKind.Bluetooth, "Bluetooth", "") { Name = "Bluetooth", HasSwitch = true, HasPage = true },
            new QuickTile(QuickTileKind.AirplaneMode, "Airplane mode", "") { Name = "Airplane mode", HasSwitch = true },
            new QuickTile(QuickTileKind.Accessibility, "Accessibility", "") { Name = "Accessibility", HasPage = true },
            // These show and switch Windows' own quick actions (QuickActions), hidden until Windows says the PC has them.
            new QuickTile(QuickTileKind.Vpn, "VPN", "") { Name = "VPN", HasSwitch = true, HasPage = true, IsShown = false, PageName = "Manage VPN connections" },
            new QuickTile(QuickTileKind.RotationLock, "Rotation lock", "") { Name = "Rotation lock", HasSwitch = true, IsShown = false },
            new QuickTile(QuickTileKind.EnergySaver, "Energy saver", "") { Name = "Energy saver", HasSwitch = true },
            new QuickTile(QuickTileKind.LiveCaptions, "Live captions", "") { Name = "Live captions", HasSwitch = true },
            // Windows' night light icon is an animation, drawn smaller than its glyph and ending on a moon while it's on.
            new QuickTile(QuickTileKind.NightLight, "Night light", "")
            {
                Name = "Night light", HasSwitch = true, IsShown = false, OffGlyphSize = 14, OnGlyph = "", OnGlyphSize = 12,
            },
            new QuickTile(QuickTileKind.MobileHotspot, "Mobile hotspot", "") { Name = "Mobile hotspot", HasSwitch = true, IsShown = false },
            new QuickTile(QuickTileKind.NearbySharing, "Nearby sharing", "") { Name = "Nearby sharing", HasSwitch = true, HasPage = true, IsShown = false },
            new QuickTile(QuickTileKind.Cast, "Cast", "") { Name = "Cast", HasPage = true },
            new QuickTile(QuickTileKind.Project, "Project", "") { Name = "Project", HasPage = true },
        ]);

        _assistiveFeatures =
        [
            Tool("Magnifier", "See words and images better", "", "Magnify.exe"),
            Tool("Narrator", "Your built-in screen reader", "", "Narrator.exe"),
            Link("Colour filters", "Distinguish among colours easily", "", "ms-settings:easeofaccess-colorfilter"),
            Tool("Live captions", "Real time audio transcription", "", "LiveCaptions.exe"),
            Link("Mono audio", "Combine left and right audio channels", "", "ms-settings:easeofaccess-audio"),
            Tool("Voice access", "Interact with your PC using voice", "", "VoiceAccess.exe"),
            new AssistiveFeature
            {
                Name = "Sticky keys",
                Description = "Use shortcuts one key at a time",
                Glyph = "",
                AutomationId = "StickyKeysSwitch",
                Read = () => StickyKeys.IsOn,
                Write = on => Try("sticky keys", () => StickyKeys.IsOn = on),
            },
        ];
        VisionFeatures.ItemsSource = _assistiveFeatures[..3];
        HearingFeatures.ItemsSource = _assistiveFeatures[3..5];
        MobilityFeatures.ItemsSource = _assistiveFeatures[5..];
    }

    /// <summary>The panel asks for the flyout to close: something else opens (Settings, a mixer) in its place.</summary>
    public event Action? CloseRequested;

    public void Attach(Indicators indicators, RunMode runMode, AppIcons icons)
    {
        _indicators = indicators;
        _runMode = runMode;
        _icons = icons;
        indicators.Changed += Refresh;
        icons.Loaded += RefreshMixerIcons;
        Refresh();
    }

    public void Detach()
    {
        if (_indicators is not null)
            _indicators.Changed -= Refresh;
        if (_icons is not null)
            _icons.Loaded -= RefreshMixerIcons;
    }

    /// <summary>Call as the flyout opens.</summary>
    public void Opening(QuickSettingsPage page)
    {
        _isOpen = true;
        ShowPage(PageFor(page), animate: false);
        // Adapters come and go, and so do VPNs; the tiles follow when they're found.
        _ = _indicators?.RefreshRadiosAsync();
        _indicators?.QuickActions.Refresh();
    }

    /// <summary>Goes to a page while open, as a shortcut for another page does.</summary>
    public void Navigate(QuickSettingsPage page) => ShowPage(PageFor(page), animate: true);

    public void Closed()
    {
        _isOpen = false;
        _tilePage = 0;
    }

    /// <summary>Shows the current state; while closed, only what the taskbar's icons need is kept up to date.</summary>
    private void Refresh()
    {
        if (_indicators is not { } indicators || !_isOpen)
            return;

        RefreshTiles(indicators);
        RefreshVolume(indicators);
        RefreshBrightness(indicators.QuickActions.State.Brightness);
        if (indicators.Battery is { } battery)
        {
            BatteryInfo.Visibility = Visibility.Visible;
            BatteryIcon.Glyph = QuickSettingsDisplay.BatteryGlyph(battery);
            BatteryText.Text = $"{battery.Percent}%";
            ToolTipService.SetToolTip(BatteryInfo, QuickSettingsDisplay.BatteryToolTip(battery));
            AutomationProperties.SetName(BatteryInfo, QuickSettingsDisplay.BatteryToolTip(battery));
        }
        else
        {
            BatteryInfo.Visibility = Visibility.Collapsed;
        }

        if (_page == SoundOutputPage)
        {
            RefreshOutputs();
            RefreshSpatialSound();
            RefreshMixer();
        }
        else if (_page == WifiPage)
        {
            RefreshWifiSwitch();
        }
        else if (_page == BluetoothPage && indicators.IsRadioOn(RadioType.Bluetooth) != _bluetoothListedOn)
        {
            _ = RefreshBluetoothAsync();
        }
        else if (_page == VpnPage)
        {
            RefreshVpn();
        }
        else if (_page == NearbySharingPage)
        {
            RefreshNearbySharing();
        }
    }

    // Assistive technologies report no changes; they're read when a page showing them opens, not on every change of
    // the indicators (each step of a volume slider being dragged).
    private void RefreshAssistiveTools()
    {
        _tiles.First(tile => tile.Kind == QuickTileKind.LiveCaptions).IsOn = AssistiveTools.IsRunning("LiveCaptions.exe");
        foreach (AssistiveFeature feature in _assistiveFeatures)
            feature.Refresh();
    }

    private void RefreshTiles(Indicators indicators)
    {
        QuickActionsState actions = indicators.QuickActions.State;
        bool hasWifi = indicators.HasRadio(RadioType.WiFi);
        bool hasBluetooth = indicators.HasRadio(RadioType.Bluetooth);
        foreach (QuickTile tile in _tiles)
        {
            switch (tile.Kind)
            {
                case QuickTileKind.WiFi:
                    tile.IsShown = hasWifi;
                    tile.IsOn = indicators.IsRadioOn(RadioType.WiFi);
                    tile.Label = tile.IsOn && indicators.Network is { Kind: NetworkKind.WiFi, Name: { } ssid } ? ssid : "Wi-Fi";
                    break;
                case QuickTileKind.Bluetooth:
                    tile.IsShown = hasBluetooth;
                    tile.IsOn = indicators.IsRadioOn(RadioType.Bluetooth);
                    break;
                case QuickTileKind.AirplaneMode:
                    tile.IsShown = indicators.AirplaneMode is not null && actions.HasAirplaneMode;
                    tile.IsOn = indicators.AirplaneMode == true;
                    break;
                case QuickTileKind.EnergySaver:
                    tile.IsShown = indicators.IsEnergySaverAvailable;
                    tile.IsOn = indicators.IsEnergySaverOn;
                    break;
                case QuickTileKind.Vpn:
                    tile.Show(actions.Vpn);
                    break;
                case QuickTileKind.RotationLock:
                    tile.Show(actions.RotationLock);
                    break;
                case QuickTileKind.NightLight:
                    tile.Show(actions.NightLight);
                    break;
                case QuickTileKind.MobileHotspot:
                    tile.Show(actions.MobileHotspot);
                    break;
                case QuickTileKind.NearbySharing:
                    tile.Show(actions.NearbySharing);
                    break;
                case QuickTileKind.Cast:
                    tile.IsOn = actions.Cast.IsOn;
                    tile.Label = actions.Cast.Label ?? tile.Name;
                    break;
            }
        }
        ShowTiles(_tilePage, direction: 0);
    }

    private FrameworkElement PageFor(QuickSettingsPage page) => page switch
    {
        QuickSettingsPage.SoundOutput => SoundOutputPage,
        QuickSettingsPage.Cast => CastPage,
        QuickSettingsPage.Project => ProjectPage,
        _ => MainPage,
    };

    // Tiles

    private IReadOnlyList<QuickTile> ShownTiles => [.. _tiles.Where(tile => tile.IsShown)];

    /// <summary>Shows a page of tiles, sliding up or down from the side it lies on (<paramref name="direction"/> 0: none).</summary>
    private void ShowTiles(int page, int direction)
    {
        IReadOnlyList<QuickTile> shown = ShownTiles;
        _tilePage = QuickSettingsDisplay.ClampPage(page, shown.Count);
        QuickTile[] wanted = [.. shown.Skip(_tilePage * QuickSettingsDisplay.TilesPerPage).Take(QuickSettingsDisplay.TilesPerPage)];
        if (!_pageTiles.SequenceEqual(wanted))
        {
            _pageTiles.Clear();
            foreach (QuickTile tile in wanted)
                _pageTiles.Add(tile);
        }

        int pages = QuickSettingsDisplay.PageCount(shown.Count);
        PreviousTilesButton.Visibility = _tilePage > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextTilesButton.Visibility = _tilePage < pages - 1 ? Visibility.Visible : Visibility.Collapsed;
        PageDots.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        PageDots.Children.Clear();
        for (int i = 0; i < pages; i++)
        {
            PageDots.Children.Add(new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                Opacity = i == _tilePage ? 1 : 0.4,
            });
        }

        if (direction != 0)
            Slide(TileGrid, new Vector3(0, 40 * direction, 0));
    }

    private void Tiles_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(TileGrid).Properties.MouseWheelDelta;
        TurnTiles(delta < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void PreviousTiles_Click(object sender, RoutedEventArgs e) => TurnTiles(-1);

    private void NextTiles_Click(object sender, RoutedEventArgs e) => TurnTiles(1);

    private void TurnTiles(int step)
    {
        int page = QuickSettingsDisplay.ClampPage(_tilePage + step, ShownTiles.Count);
        if (page != _tilePage)
            ShowTiles(page, step);
    }

    private async void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: QuickTile tile } button || _indicators is not { } indicators)
            return;

        // The tile shows the feature's state, which follows once it has changed; not the click itself.
        button.IsChecked = tile.IsOn;
        bool on = !tile.IsOn;
        switch (tile.Kind)
        {
            case QuickTileKind.WiFi:
                await indicators.SetRadioAsync(RadioType.WiFi, on);
                break;
            case QuickTileKind.Bluetooth:
                await indicators.SetRadioAsync(RadioType.Bluetooth, on);
                break;
            case QuickTileKind.AirplaneMode:
                indicators.SetAirplaneMode(on);
                break;
            case QuickTileKind.EnergySaver:
                indicators.SetEnergySaver(on);
                break;
            case QuickTileKind.LiveCaptions:
                AssistiveTools.Set("Live captions", "LiveCaptions.exe", on);
                tile.IsOn = on;
                break;
            case QuickTileKind.Accessibility:
                ShowPage(AccessibilityPage, animate: true);
                break;
            case QuickTileKind.Cast:
                ShowPage(CastPage, animate: true);
                break;
            case QuickTileKind.Project:
                ShowPage(ProjectPage, animate: true);
                break;
            case QuickTileKind.Vpn when !tile.HasSwitch:
                ShowPage(VpnPage, animate: true);
                break;
            case QuickTileKind.Vpn:
                indicators.QuickActions.ToggleVpn(on);
                break;
            case QuickTileKind.RotationLock:
                indicators.QuickActions.SetRotationLock(on);
                break;
            case QuickTileKind.NightLight:
                indicators.QuickActions.SetNightLight(on);
                break;
            case QuickTileKind.MobileHotspot:
                indicators.QuickActions.SetMobileHotspot(on);
                break;
            case QuickTileKind.NearbySharing:
                indicators.QuickActions.SetNearbySharing(on);
                break;
        }
        button.IsChecked = tile.IsOn;
    }

    private void TilePage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: QuickTile tile } button)
            return;

        button.IsChecked = tile.IsOn;
        ShowPage(tile.Kind switch
        {
            QuickTileKind.WiFi => WifiPage,
            QuickTileKind.Bluetooth => BluetoothPage,
            QuickTileKind.Vpn => VpnPage,
            _ => NearbySharingPage,
        }, animate: true);
    }

    // Pages

    private void Back_Click(object sender, RoutedEventArgs e) => ShowPage(MainPage, animate: true);

    /// <summary>Shows a page; pages slide in from the side they lie on, as Quick Settings' pages do.</summary>
    private void ShowPage(FrameworkElement page, bool animate)
    {
        bool forward = page != MainPage;
        foreach (FrameworkElement each in (FrameworkElement[])[MainPage, SoundOutputPage, WifiPage, BluetoothPage, AccessibilityPage, CastPage, ProjectPage, VpnPage, NearbySharingPage])
            each.Visibility = each == page ? Visibility.Visible : Visibility.Collapsed;
        _page = page;

        if (page == MainPage || page == AccessibilityPage)
            RefreshAssistiveTools();
        else if (page == WifiPage)
            _ = ScanWifiAsync();
        else if (page == BluetoothPage)
            _ = RefreshBluetoothAsync();
        else if (page == ProjectPage)
            RefreshProjection();
        else if (page == CastPage)
            RefreshCast();
        Refresh();

        if (!animate)
            return;

        // As Windows' (recorded at 60 fps): a page's header shows almost at once and its content rises into place as
        // it fades in; going back, the tiles just fade in. Every page is header, content and footer.
        if (forward && page is Grid { Children: [_, UIElement content, ..] })
        {
            FadeIn(page, TimeSpan.FromMilliseconds(80));
            FadeIn(content, TimeSpan.FromMilliseconds(150));
            Rise(content);
        }
        else
        {
            FadeIn(page, TimeSpan.FromMilliseconds(100));
        }
    }

    private static void FadeIn(UIElement element, TimeSpan duration)
    {
        Compositor compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element).Compositor;
        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, compositor.CreateLinearEasingFunction());
        fade.Target = nameof(UIElement.Opacity);
        fade.Duration = duration;
        element.StartAnimation(fade);
    }

    private static void Rise(UIElement element)
    {
        Compositor compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element).Compositor;
        Vector3KeyFrameAnimation rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, 32, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new(0, 0), new(0, 1)));
        rise.Target = nameof(UIElement.Translation);
        rise.Duration = TimeSpan.FromMilliseconds(300);
        element.StartAnimation(rise);
    }

    private static void Slide(UIElement element, Vector3 from)
    {
        Compositor compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element).Compositor;
        CompositionEasingFunction easing = compositor.CreateCubicBezierEasingFunction(new(0.1f, 0.9f), new(0.2f, 1f));
        Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, from);
        slide.InsertKeyFrame(1, Vector3.Zero, easing);
        slide.Target = nameof(UIElement.Translation);
        slide.Duration = TimeSpan.FromMilliseconds(250);
        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, easing);
        fade.Target = nameof(UIElement.Opacity);
        fade.Duration = TimeSpan.FromMilliseconds(250);
        element.StartAnimation(slide);
        element.StartAnimation(fade);
    }

    // Volume

    private void RefreshVolume(Indicators indicators)
    {
        float volume = indicators.Volume;
        bool muted = indicators.IsMuted;
        MuteIcon.Glyph = IndicatorDisplay.VolumeGlyph(indicators.HasAudioDevice, volume, muted);
        AutomationProperties.SetName(MuteButton, muted ? "Unmute" : "Mute");
        // Moving the slider changes the volume, which comes back here; don't set it back while it's being dragged.
        _updatingVolumeSlider = true;
        VolumeSlider.Value = IndicatorDisplay.Percent(volume);
        _updatingVolumeSlider = false;
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_indicators is not null)
            _indicators.IsMuted = !_indicators.IsMuted;
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_indicators is null || _updatingVolumeSlider)
            return;

        _indicators.Volume = (float)(e.NewValue / 100);
        // Turning it up means wanting to hear it, as in Windows' own volume slider.
        if (_indicators.IsMuted && e.NewValue > 0)
            _indicators.IsMuted = false;
        // Changed with the keyboard: each step is let go of at once.
        if (!_volumeSliderPressed && ReferenceEquals(FocusManager.GetFocusedElement(VolumeSlider.XamlRoot), VolumeSlider))
            AudioDevices.PlayVolumeFeedback();
    }

    // Windows plays a sound when the slider is let go, so the new volume can be heard.
    private void VolumeSlider_PointerPressed(object sender, PointerRoutedEventArgs e) => _volumeSliderPressed = true;

    private void VolumeSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_volumeSliderPressed)
            return;

        _volumeSliderPressed = false;
        AudioDevices.PlayVolumeFeedback();
    }

    private void SoundOutputButton_Click(object sender, RoutedEventArgs e) => ShowPage(SoundOutputPage, animate: true);

    // Brightness: only where Windows can set the screen's (an internal panel, or a monitor it controls).

    private void RefreshBrightness(int? percent)
    {
        Visibility visibility = percent is null ? Visibility.Collapsed : Visibility.Visible;
        BrightnessIcon.Visibility = BrightnessSlider.Visibility = visibility;
        // What the slider sets comes back here, a moment later; don't move it back while it's being dragged.
        if (percent is { } value && !_brightnessSliderPressed)
        {
            _updatingBrightnessSlider = true;
            BrightnessSlider.Value = value;
            _updatingBrightnessSlider = false;
        }
    }

    private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingBrightnessSlider)
            _indicators?.QuickActions.SetBrightness((int)e.NewValue);
    }

    private void RefreshOutputs()
    {
        IReadOnlyList<AudioDevice> outputs = _indicators!.OutputDevices();
        if (outputs.SequenceEqual(_outputs))
            return;

        // Filling the list selects items; that isn't the user choosing an output.
        _outputs = outputs;
        _updatingLists = true;
        OutputDevices.Items.Clear();
        foreach (AudioDevice device in outputs)
        {
            ListViewItem item = ListItem("", device.Name, device, "OutputDevice");
            OutputDevices.Items.Add(item);
            if (device.IsDefault)
                OutputDevices.SelectedItem = item;
        }
        _updatingLists = false;
    }

    private void OutputDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingLists && OutputDevices.SelectedItem is ListViewItem { Tag: AudioDevice { IsDefault: false } device })
            _indicators?.SetOutputDevice(device);
    }

    private void RefreshSpatialSound()
    {
        (IReadOnlyList<SpatialFormat> formats, string current) = _indicators!.SpatialFormats();
        SpatialSection.Visibility = formats.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _updatingLists = true;
        SpatialFormats.Items.Clear();
        foreach (SpatialFormat format in formats)
        {
            ListViewItem item = ListItem(null, format.Name, format, "SpatialFormat");
            SpatialFormats.Items.Add(item);
            if (string.Equals(format.Subtype, current, StringComparison.OrdinalIgnoreCase))
                SpatialFormats.SelectedItem = item;
        }
        _updatingLists = false;
    }

    private async void SpatialFormats_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLists || _indicators is null || SpatialFormats.SelectedItem is not ListViewItem { Tag: SpatialFormat format })
            return;

        await _indicators.SetSpatialFormat(format);
        RefreshSpatialSound();
    }

    private static ListViewItem ListItem(string? glyph, string text, object tag, string automationId)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        if (glyph is not null)
            content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var item = new ListViewItem { Content = content, Tag = tag, MinHeight = 40 };
        AutomationProperties.SetAutomationId(item, automationId);
        AutomationProperties.SetName(item, text);
        return item;
    }

    // Updated in place, so a slider being dragged isn't replaced under the pointer.
    private void RefreshMixer()
    {
        IReadOnlyList<AudioApp> apps = _indicators!.MixerApps();
        var keys = apps.Select(app => app.Key).ToHashSet();
        for (int i = _mixerApps.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(_mixerApps[i].Key))
                _mixerApps.RemoveAt(i);
        }
        for (int i = 0; i < apps.Count; i++)
        {
            int existing = -1;
            for (int j = i; j < _mixerApps.Count && existing < 0; j++)
            {
                if (_mixerApps[j].Key == apps[i].Key)
                    existing = j;
            }
            if (existing < 0)
                _mixerApps.Insert(i, new MixerApp(apps[i].Key));
            else if (existing != i)
                _mixerApps.Move(existing, i);

            string name = Indicators.AppName(apps[i]);
            _mixerApps[i].Update(apps[i], name, MixerIcon(apps[i], name));
        }
        NoMixerAppsText.Visibility = _mixerApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private ImageSource? MixerIcon(AudioApp app, string name) =>
        app.PackageAppId is { } appId ? _icons?.Get(new PinnedApp(name, AppUserModelId: appId))
        : app.ProcessPath is { } path ? _icons?.Get(new PinnedApp(name, Path: path))
        : null;

    private void RefreshMixerIcons()
    {
        if (_isOpen && _page == SoundOutputPage)
            RefreshMixer();
    }

    private void MixerMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MixerApp app })
            app.IsMuted = !app.IsMuted;
    }

    // As the shell, the classic mixer: Settings can't start without Explorer.
    private void OpenVolumeMixer_Click(object sender, RoutedEventArgs e) =>
        Open(new PinnedApp("Volume mixer", Path: "ms-settings:apps-volume"), new PinnedApp("Volume mixer", Path: "sndvol.exe"));

    private void SoundSettings_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("Sound settings", "ms-settings:sound", "mmsys.cpl");

    // Wi-Fi

    private void RefreshWifiSwitch()
    {
        _updatingLists = true;
        WifiSwitch.IsOn = _indicators?.IsRadioOn(RadioType.WiFi) == true;
        _updatingLists = false;
    }

    private async void WifiSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingLists || _indicators is null)
            return;

        await _indicators.SetRadioAsync(RadioType.WiFi, WifiSwitch.IsOn);
        RefreshWifiSwitch();
        await ScanWifiAsync();
    }

    private async void WifiRefresh_Click(object sender, RoutedEventArgs e) => await ScanWifiAsync();

    private async Task ScanWifiAsync()
    {
        if (_scanning || _indicators is null)
            return;

        RefreshWifiSwitch();
        if (!_indicators.IsRadioOn(RadioType.WiFi))
        {
            _wifiNetworks.Clear();
            ShowWifiMessage("Wi-Fi is turned off", busy: false);
            return;
        }

        _scanning = true;
        WifiRefreshButton.IsEnabled = false;
        if (_wifiNetworks.Count == 0)
            ShowWifiMessage("Looking for networks", busy: true);
        try
        {
            _wifi ??= await WifiNetworks.FindAsync();
            if (_wifi is null)
            {
                ShowWifiMessage("Windows doesn't let NeoShell use the Wi-Fi adapter", busy: false);
                return;
            }

            IReadOnlyList<WifiNetwork> networks = await _wifi.ScanAsync();
            // An open network keeps its place, so a password being typed isn't lost.
            string? expanded = _wifiNetworks.FirstOrDefault(item => item.IsExpanded)?.Name;
            _wifiNetworks.Clear();
            foreach (WifiNetwork network in networks)
                _wifiNetworks.Add(new WifiItem(network));
            if (_wifiNetworks.FirstOrDefault(item => item.Name == expanded) is { } item)
            {
                WifiList.SelectedItem = item;
                item.IsExpanded = true;
            }
            ShowWifiMessage(networks.Count == 0 ? "No Wi-Fi networks found" : null, busy: false);
        }
        catch (Exception ex)
        {
            Log.Warn("Scanning for Wi-Fi networks failed", ex);
            ShowWifiMessage("Couldn't look for Wi-Fi networks", busy: false);
        }
        finally
        {
            _scanning = false;
            WifiRefreshButton.IsEnabled = true;
        }
    }

    private void ShowWifiMessage(string? text, bool busy)
    {
        WifiMessage.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        WifiMessageText.Text = text ?? "";
        WifiProgress.IsActive = busy;
        WifiProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WifiList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (WifiItem item in _wifiNetworks)
            item.IsExpanded = ReferenceEquals(item, WifiList.SelectedItem);
    }

    private async void WifiAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WifiItem item } || _wifi is null)
            return;

        if (item.Network.IsConnected)
        {
            _wifi.Disconnect();
            Log.Info($"Wi-Fi: disconnected from {item.Name}");
            await ScanWifiAsync();
            return;
        }

        item.IsBusy = true;
        item.Message = "Connecting";
        try
        {
            string? password = item.NeedsPassword ? item.Password : null;
            WifiConnectResult result = await _wifi.ConnectAsync(item.Network, item.ConnectAutomatically, password);
            Log.Info($"Wi-Fi: connecting to {item.Name}: {result}");
            switch (result)
            {
                case WifiConnectResult.Connected:
                    item.Message = null;
                    await ScanWifiAsync();
                    break;
                case WifiConnectResult.NeedsPassword:
                    item.Message = item.NeedsPassword ? "The network security key isn't correct" : null;
                    item.NeedsPassword = true;
                    item.Password = "";
                    break;
                default:
                    item.Message = "Can't connect to this network";
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Connecting to {item.Name} failed", ex);
            item.Message = "Can't connect to this network";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private void WifiCancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WifiItem item })
        {
            item.NeedsPassword = false;
            item.Password = "";
            item.Message = null;
        }
    }

    private void WifiSettings_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("Wi-Fi settings", "ms-settings:network-wifi", "ncpa.cpl");

    // Bluetooth

    private async Task RefreshBluetoothAsync()
    {
        bool on = _indicators?.IsRadioOn(RadioType.Bluetooth) == true;
        _bluetoothListedOn = on;
        _updatingLists = true;
        BluetoothSwitch.IsOn = on;
        _updatingLists = false;
        if (!on)
        {
            BluetoothList.ItemsSource = null;
            ShowBluetoothMessage("Bluetooth is turned off");
            return;
        }

        try
        {
            IReadOnlyList<PairedDevice> devices = await BluetoothDevices.PairedAsync();
            BluetoothList.ItemsSource = devices.Select(device => new BluetoothItem(device)).ToList();
            ShowBluetoothMessage(devices.Count == 0 ? "No paired devices" : null);
        }
        catch (Exception ex)
        {
            Log.Warn("Listing the Bluetooth devices failed", ex);
            ShowBluetoothMessage("Couldn't list the Bluetooth devices");
        }
    }

    private void ShowBluetoothMessage(string? text)
    {
        BluetoothMessage.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        BluetoothMessage.Text = text ?? "";
    }

    private async void BluetoothSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingLists || _indicators is null)
            return;

        await _indicators.SetRadioAsync(RadioType.Bluetooth, BluetoothSwitch.IsOn);
        await RefreshBluetoothAsync();
    }

    // As the shell: Devices and Printers, where Bluetooth devices are added and removed.
    private void BluetoothSettings_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("Bluetooth settings", "ms-settings:bluetooth", "/name Microsoft.DevicesAndPrinters");

    // VPN

    private void RefreshVpn()
    {
        IReadOnlyList<VpnConnection> connections;
        try
        {
            connections = VpnConnections.List();
        }
        catch (Exception ex)
        {
            Log.Warn("Listing the VPN connections failed", ex);
            connections = [];
        }

        // Read again on every network change: keep the list (and the chosen connection) unless something changed.
        var items = VpnList.ItemsSource as IReadOnlyList<VpnItem>;
        if (items is null || !items.Select(item => item.Connection).SequenceEqual(connections))
        {
            string? chosen = (VpnList.SelectedItem as VpnItem)?.Name;
            items = [.. connections.Select(connection => new VpnItem(connection))];
            VpnList.ItemsSource = items;
            // As Windows: the first one is chosen, ready to connect.
            VpnList.SelectedItem = items.FirstOrDefault(item => item.Name == chosen) ?? items.FirstOrDefault();
        }
        VpnMessage.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void VpnList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (VpnItem item in VpnList.Items.Cast<VpnItem>())
            item.IsExpanded = ReferenceEquals(item, VpnList.SelectedItem);
    }

    // Connecting opens Windows' own dialog, which asks for what isn't saved; it takes the focus, so Quick Settings closes.
    private void VpnAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: VpnItem item })
            return;

        try
        {
            if (item.Connection.IsConnected)
            {
                VpnConnections.Disconnect(item.Connection);
                Log.Info($"VPN: disconnected from {item.Name}");
                RefreshVpn();
            }
            else
            {
                CloseRequested?.Invoke();
                VpnConnections.Connect(item.Connection);
                Log.Info($"VPN: connecting to {item.Name}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"VPN: could not {(item.Connection.IsConnected ? "disconnect from" : "connect to")} {item.Name}", ex);
        }
    }

    // As the shell, Network Connections, which lists the VPN connections too.
    private void VpnSettings_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("VPN settings", "ms-settings:network-vpn", "ncpa.cpl");

    // Nearby sharing

    private void RefreshNearbySharing()
    {
        bool on = _indicators?.QuickActions.State.NearbySharing.IsOn == true;
        _updatingLists = true;
        NearbySharingSwitch.IsOn = on;
        _updatingLists = false;
        (NearbySharingTitle.Text, NearbySharingText.Text) = QuickSettingsDisplay.NearbySharingText(on);
    }

    private void NearbySharingSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingLists)
            _indicators?.QuickActions.SetNearbySharing(NearbySharingSwitch.IsOn);
    }

    private void NearbySharingSettings_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("Nearby sharing settings", "ms-settings:crossdevice");

    // Accessibility

    private static AssistiveFeature Tool(string name, string description, string glyph, string executable) => new()
    {
        Name = name,
        Description = description,
        Glyph = glyph,
        AutomationId = $"{name.Replace(" ", "")}Switch",
        Read = () => AssistiveTools.IsRunning(executable),
        Write = on => AssistiveTools.Set(name, executable, on),
    };

    private static AssistiveFeature Link(string name, string description, string glyph, string settingsUri) => new()
    {
        Name = name,
        Description = description,
        Glyph = glyph,
        AutomationId = $"{name.Replace(" ", "")}Link",
        SettingsUri = settingsUri,
    };

    private static void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not change {what}", ex);
        }
    }

    // As the shell, the Ease of Access Center has the classic versions of these.
    private void AssistiveLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssistiveFeature { SettingsUri: { } uri } feature })
            OpenSettings(feature.LinkName, uri, "access.cpl");
    }

    private void AccessibilitySettings_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("Accessibility settings", "ms-settings:easeofaccess", "access.cpl");

    // Cast and Project

    // Miracast needs Wi-Fi; Windows says so on this page when there's none. Connecting to a wireless display has no
    // public API: Settings lists and connects them.
    private void RefreshCast()
    {
        bool wireless = _indicators?.HasRadio(RadioType.WiFi) == true;
        CastTitle.Text = wireless ? "Cast to a wireless display" : "Connect a cable to cast";
        CastText.Text = wireless
            ? "Find wireless displays nearby and connect to one in Settings."
            : "Your device doesn't support Miracast, so you'll need to connect an external display with a cable.";
        FindDisplaysButton.Visibility = wireless && _runMode != RunMode.Shell ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FindDisplays_Click(object sender, RoutedEventArgs e) =>
        OpenSettings("Wireless displays", "ms-settings-connectabledevices:devicediscovery");

    private void RefreshProjection()
    {
        ProjectionMode? current = DisplayProjection.Current();
        _updatingLists = true;
        ProjectionModes.Items.Clear();
        foreach ((ProjectionMode mode, string name) in (ReadOnlySpan<(ProjectionMode, string)>)
            [
                (ProjectionMode.PcScreenOnly, "PC screen only"),
                (ProjectionMode.Duplicate, "Duplicate"),
                (ProjectionMode.Extend, "Extend"),
                (ProjectionMode.SecondScreenOnly, "Second screen only"),
            ])
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            content.Children.Add(ProjectionIcon(mode));
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var item = new ListViewItem { Content = content, Tag = mode, MinHeight = 56 };
            AutomationProperties.SetAutomationId(item, $"Projection{mode}");
            AutomationProperties.SetName(item, name);
            ProjectionModes.Items.Add(item);
            if (mode == current)
                ProjectionModes.SelectedItem = item;
        }
        _updatingLists = false;
    }

    /// <summary>Two screens, the PC's and the second, lit when used; extending spans the picture across both.</summary>
    private static FrameworkElement ProjectionIcon(ProjectionMode mode)
    {
        var icon = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        foreach (bool second in (bool[])[false, true])
        {
            bool used = mode switch
            {
                ProjectionMode.PcScreenOnly => !second,
                ProjectionMode.SecondScreenOnly => second,
                _ => true,
            };
            icon.Children.Add(new Border
            {
                Width = 16,
                Height = 12,
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                Opacity = used ? 1 : 0.4,
                Child = mode == ProjectionMode.Duplicate || (mode == ProjectionMode.Extend && !second)
                    ? new Rectangle { Width = 6, Height = 4, Fill = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] }
                    : null,
            });
        }
        return icon;
    }

    private void ProjectionModes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLists || ProjectionModes.SelectedItem is not ListViewItem { Tag: ProjectionMode mode })
            return;

        try
        {
            DisplayProjection.Set(mode);
            Log.Info($"Project: {mode}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not switch the screens to {mode}", ex);
        }
        RefreshProjection();
    }

    // As the shell, the display adapter's classic properties (Settings can't start without Explorer).
    private void DisplaySettings_Click(object sender, RoutedEventArgs e) =>
        Open(new PinnedApp("Display settings", Path: "ms-settings:display"),
            new PinnedApp("Display settings", Path: "rundll32.exe", Arguments: "display.dll,ShowAdapterSettings 0"));

    private void AllSettings_Click(object sender, RoutedEventArgs e) => OpenSettings("Settings", "ms-settings:");

    private void OpenSettings(string name, string settingsUri, string applet = "")
    {
        CloseRequested?.Invoke();
        Launcher.OpenSettings(_runMode, name, settingsUri, applet);
    }

    private void Open(PinnedApp alongsideExplorer, PinnedApp asShell)
    {
        CloseRequested?.Invoke();
        Launcher.Launch(_runMode == RunMode.Shell ? asShell : alongsideExplorer);
    }
}
