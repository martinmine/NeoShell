using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>The signed-in user's picture and name, with the time and date.</summary>
internal sealed partial class ProfileWidget : WidgetView
{
    private readonly DispatcherQueueTimer _timer;

    public ProfileWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        string name = UserAccount.DisplayName;
        NameText.Text = name;
        Picture.DisplayName = name;
        LoadPicture();

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => ShowTime();
        ShowTime();
    }

    private bool ShowSeconds => Settings.ShowSeconds ?? false;

    public override void Close() => _timer.Stop();

    public override FrameworkElement CreateSettings()
    {
        var seconds = new ToggleSwitch { Header = "Show seconds", IsOn = ShowSeconds };
        seconds.Toggled += (_, _) =>
        {
            SaveSettings(Settings with { ShowSeconds = seconds.IsOn });
            ShowTime();
        };
        return seconds;
    }

    protected override bool LoadsContent => true;

    private async void LoadPicture()
    {
        if (await UserAccount.LoadPictureAsync(192) is { } picture)
            Picture.ProfilePicture = picture;
        MarkReady();
    }

    // Ticks just after each second or minute turns, so the clock never runs late.
    private void ShowTime()
    {
        DateTime now = DateTime.Now;
        CultureInfo culture = CultureInfo.CurrentCulture;
        TimeText.Text = now.ToString(ShowSeconds ? culture.DateTimeFormat.LongTimePattern : culture.DateTimeFormat.ShortTimePattern, culture);
        DateText.Text = now.ToString("D", culture);

        double untilNext = ShowSeconds ? 1000 - now.Millisecond : (60 - now.Second) * 1000 - now.Millisecond;
        _timer.Interval = TimeSpan.FromMilliseconds(untilNext + 20);
        _timer.Start();
    }
}
