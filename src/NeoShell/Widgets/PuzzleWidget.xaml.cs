using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using NeoShell.Logging;
using NeoShell.Settings;
using Windows.Foundation;

namespace NeoShell.Widgets;

/// <summary>
/// Vista's picture puzzle: a picture cut into fifteen tiles to slide back into place, timed, with a button held to
/// see the picture whole. Windows' own wallpaper by default; without a picture the tiles are numbered.
/// </summary>
internal sealed partial class PuzzleWidget : WidgetView
{
    private static readonly string s_windowsPicture =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Web\Wallpaper\Windows\img0.jpg");

    private readonly SlidingPuzzle _puzzle = new();
    // By tile number less one: the tile's button and the piece of the picture on it.
    private readonly Button[] _tiles = new Button[SlidingPuzzle.Size * SlidingPuzzle.Size - 1];
    private readonly Image[] _pieces = new Image[SlidingPuzzle.Size * SlidingPuzzle.Size - 1];
    private readonly TextBlock[] _numbers = new TextBlock[SlidingPuzzle.Size * SlidingPuzzle.Size - 1];
    private readonly DispatcherQueueTimer _clock;
    private readonly Stopwatch _time = new();
    private BitmapImage? _picture;

    public PuzzleWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        BuildBoard();
        _clock = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => ShowTime();
        PeekButton.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => ShowWhole(true)), true);
        PeekButton.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => ShowWhole(false)), true);
        PeekButton.PointerCaptureLost += (_, _) => ShowWhole(false);
        PeekButton.PointerExited += (_, _) => ShowWhole(false);
        Shuffle();
        LoadPicture();
    }

    private string PicturePath => Settings.PuzzlePicture ?? s_windowsPicture;

    public override void Close() => _clock.Stop();

    public override FrameworkElement CreateSettings()
    {
        var name = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void ShowName() => name.Text = Settings.PuzzlePicture is null ? "Windows' wallpaper" : Path.GetFileName(Settings.PuzzlePicture);
        ShowName();

        var choose = new Button { Content = "Choose picture…" };
        choose.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            };
            foreach (string type in (string[])[".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"])
                picker.FileTypeFilter.Add(type);
            if (await picker.PickSingleFileAsync() is not { } picked)
                return;
            ChangePicture(picked.Path);
            ShowName();
        };
        var windows = new Button { Content = "Use Windows' wallpaper" };
        windows.Click += (_, _) =>
        {
            ChangePicture(null);
            ShowName();
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Picture", Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        panel.Children.Add(name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(choose);
        buttons.Children.Add(windows);
        panel.Children.Add(buttons);
        return panel;
    }

    private void ChangePicture(string? path)
    {
        SaveSettings(Settings with { PuzzlePicture = path });
        Shuffle();
        LoadPicture();
    }

    private void BuildBoard()
    {
        for (int i = 0; i < SlidingPuzzle.Size; i++)
        {
            Board.RowDefinitions.Add(new RowDefinition());
            Board.ColumnDefinitions.Add(new ColumnDefinition());
        }
        for (int i = 0; i < _tiles.Length; i++)
        {
            int tile = i + 1;
            // The piece is the whole picture, moved so the tile's part shows through a clip of the tile's size.
            var piece = new Image { Stretch = Stretch.UniformToFill };
            var number = new TextBlock
            {
                Text = tile.ToString(CultureInfo.CurrentCulture),
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new Grid();
            content.Children.Add(new Canvas { Children = { piece } });
            content.Children.Add(number);
            var button = new Button { Style = (Style)Resources["TileButtonStyle"], Padding = new Thickness(0), Content = content };
            AutomationProperties.SetAutomationId(button, "PuzzleTile");
            AutomationProperties.SetName(button, $"Tile {tile}");
            button.Click += (_, _) => Slide(tile);
            Board.Children.Add(button);
            _tiles[i] = button;
            _pieces[i] = piece;
            _numbers[i] = number;
        }
    }

    // Square, as wide as the card; the pieces follow the tiles' size.
    private void BoardArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double side = e.NewSize.Width;
        if (Math.Abs(BoardArea.Height - side) > 0.5)
            BoardArea.Height = side;

        double cell = side / SlidingPuzzle.Size;
        double tileSize = Math.Max(0, cell - 2);
        for (int i = 0; i < _tiles.Length; i++)
        {
            Image piece = _pieces[i];
            var canvas = (Canvas)piece.Parent;
            canvas.Width = canvas.Height = tileSize;
            canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, tileSize, tileSize) };
            piece.Width = piece.Height = side;
            // Where the tile belongs, less the tile's margin.
            Canvas.SetLeft(piece, -(i % SlidingPuzzle.Size * cell + 1));
            Canvas.SetTop(piece, -(i / SlidingPuzzle.Size * cell + 1));
        }
    }

    private async void LoadPicture()
    {
        string path = PicturePath;
        BitmapImage? picture = null;
        if (File.Exists(path))
        {
            try
            {
                // Decoded at about the size shown (wallpapers are 4K): a landscape picture is cut to a square by its height.
                double scale = XamlRoot?.RasterizationScale ?? 1;
                picture = new BitmapImage { DecodePixelHeight = (int)Math.Round(Math.Max(BoardArea.ActualWidth, 300) * scale) };
                byte[] bytes = await File.ReadAllBytesAsync(path);
                using var stream = new MemoryStream(bytes);
                await picture.SetSourceAsync(stream.AsRandomAccessStream());
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not show {path}", ex);
                picture = null;
            }
        }
        // Another picture was chosen meanwhile.
        if (path != PicturePath)
            return;

        _picture = picture;
        WholeImage.Source = picture;
        for (int i = 0; i < _tiles.Length; i++)
        {
            _pieces[i].Source = picture;
            _numbers[i].Visibility = picture is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Shuffle()
    {
        _puzzle.Shuffle(Random.Shared);
        for (int tile = 1; tile <= _tiles.Length; tile++)
            Place(tile);
        Board.IsHitTestVisible = true;
        _clock.Stop();
        _time.Reset();
        ShowTime();
        ShowWhole(false);
    }

    private void Place(int tile)
    {
        int place = _puzzle.PlaceOf(tile);
        Grid.SetRow(_tiles[tile - 1], place / SlidingPuzzle.Size);
        Grid.SetColumn(_tiles[tile - 1], place % SlidingPuzzle.Size);
    }

    private void Slide(int tile)
    {
        if (!_puzzle.Move(tile))
            return;

        Place(tile);
        if (!_time.IsRunning)
        {
            _time.Start();
            _clock.Start();
        }
        if (_puzzle.IsSolved)
        {
            _time.Stop();
            _clock.Stop();
            Board.IsHitTestVisible = false;
            ShowWhole(false);
        }
        ShowTime();
    }

    private void ShowTime()
    {
        string time = _time.Elapsed.ToString(_time.Elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.CurrentCulture);
        TimeText.Text = _puzzle.IsSolved ? $"Solved in {time}" : time;
    }

    // Held, or once solved: a solved puzzle shows its picture whole, without the gaps between the tiles.
    private void ShowWhole(bool peeking) =>
        WholePicture.Visibility = (peeking || _puzzle.IsSolved) && _picture is not null ? Visibility.Visible : Visibility.Collapsed;

    private void ShuffleButton_Click(object sender, RoutedEventArgs e) => Shuffle();
}
