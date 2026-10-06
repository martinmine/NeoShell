using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using NeoShell.Interop.Shell;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>A slideshow of the pictures in a folder (the user's Pictures, and the folders in it), in random order.</summary>
internal sealed partial class PicturesWidget : WidgetView
{
    // The picture each pictures widget shows, by widget: a widget moved between the sidebar and the desktop gets a
    // new view, which goes on from that picture rather than another.
    private static readonly Dictionary<string, string> s_shown = [];

    private static readonly HashSet<string> s_extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    // Enough for a slideshow, without walking a whole photo archive.
    private const int MaxPictures = 2000;

    private readonly DispatcherQueueTimer _timer;
    private CancellationTokenSource? _scan;
    private string[] _pictures = [];
    private int _next;
    private string? _shown;

    public PicturesWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => ShowNext();
        Load();
    }

    public override bool FillsCard => true;

    protected override bool LoadsContent => true;

    private string Folder => Settings.PictureFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    private int Seconds => Math.Clamp(Settings.SlideSeconds ?? 10, 3, 3600);

    public override void Close()
    {
        _timer.Stop();
        _scan?.Cancel();
    }

    public override FrameworkElement CreateSettings()
    {
        var folder = new TextBlock { Text = Folder, TextWrapping = TextWrapping.Wrap };
        var choose = new Button { Content = "Choose folder…" };
        choose.Click += async (_, _) =>
        {
            var picker = new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
            if (await picker.PickSingleFolderAsync() is not { } picked)
                return;
            SaveSettings(Settings with { PictureFolder = picked.Path });
            folder.Text = Folder;
            Load();
        };
        var seconds = new NumberBox
        {
            Header = "Seconds per picture",
            Minimum = 3,
            Maximum = 3600,
            Value = Seconds,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        seconds.ValueChanged += (_, _) =>
        {
            if (double.IsNaN(seconds.Value))
                return;
            SaveSettings(Settings with { SlideSeconds = (int)seconds.Value });
            _timer.Interval = TimeSpan.FromSeconds(Seconds);
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Folder", Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        panel.Children.Add(folder);
        panel.Children.Add(choose);
        panel.Children.Add(seconds);
        return panel;
    }

    private async void Load()
    {
        _scan?.Cancel();
        var scan = _scan = new CancellationTokenSource();
        _timer.Stop();
        string folder = Folder;
        string[] pictures = await Task.Run(() => Find(folder, scan.Token));
        if (scan.IsCancellationRequested)
            return;

        Random.Shared.Shuffle(pictures);
        if (s_shown.TryGetValue(Settings.Id, out string? shown) && Array.IndexOf(pictures, shown) is > 0 and var at)
            (pictures[0], pictures[at]) = (pictures[at], pictures[0]);
        _pictures = pictures;
        _next = 0;
        if (pictures.Length == 0)
        {
            Picture.Background = null;
            _shown = null;
            StatusText.Text = $"No pictures in {folder}";
            MarkReady();
            return;
        }
        StatusText.Text = "";
        ShowNext();
        _timer.Interval = TimeSpan.FromSeconds(Seconds);
        _timer.Start();
    }

    private static string[] Find(string folder, CancellationToken cancel)
    {
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            };
            return [.. Directory.EnumerateFiles(folder, "*", options)
                .TakeWhile(_ => !cancel.IsCancellationRequested)
                .Where(path => s_extensions.Contains(Path.GetExtension(path)))
                .Take(MaxPictures)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn($"Could not list the pictures in {folder}", ex);
            return [];
        }
    }

    private async void ShowNext()
    {
        if (_pictures.Length == 0)
            return;

        string path = _pictures[_next++ % _pictures.Length];
        try
        {
            // Decoded at the size shown, not the camera's: a slideshow of large photos stays light.
            double scale = XamlRoot?.RasterizationScale ?? 1;
            var image = new BitmapImage { DecodePixelWidth = (int)Math.Max(1, Math.Round(Math.Max(ActualWidth, 300) * scale)) };
            byte[] bytes = await File.ReadAllBytesAsync(path);
            using var stream = new MemoryStream(bytes);
            await image.SetSourceAsync(stream.AsRandomAccessStream());
            Picture.Background = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
            _shown = s_shown[Settings.Id] = path;
        }
        catch (Exception ex)
        {
            // Moved, deleted or not really a picture; the next one shows on the next tick.
            Log.Warn($"Could not show {path}", ex);
        }
        MarkReady();
    }

    private void Picture_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_shown is null)
            return;

        try
        {
            ShellLaunch.Open(_shown);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {_shown}", ex);
        }
    }
}
