using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Logging;
using NeoShell.Settings;

namespace NeoShell.Widgets;

/// <summary>A note, saved as it's typed to a text file of its own (notes\&lt;id&gt;.txt next to the settings).</summary>
internal sealed partial class NotesWidget : WidgetView
{
    private static readonly (string Name, double Size)[] s_sizes = [("Small", 12), ("Medium", 14), ("Large", 18)];

    private readonly DispatcherQueueTimer _saveTimer;
    private readonly string _path;

    public NotesWidget(WidgetSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        _path = Path.Combine(Program.DataDirectory, "notes", settings.Id + ".txt");
        NoteBox.FontSize = Settings.FontSize ?? 14;
        NoteBox.Height = Settings.ContentHeight ?? 160;

        _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => Save();
        Load();
    }

    public override bool CanResize => true;

    public override bool ContentUnderButtons => true;

    public override double ContentHeight
    {
        get => NoteBox.Height;
        set => NoteBox.Height = value;
    }

    public override void Close()
    {
        // Typed in the last half second.
        if (_saveTimer.IsRunning)
        {
            _saveTimer.Stop();
            Save();
        }
    }

    public override FrameworkElement CreateSettings()
    {
        var size = new ComboBox { Header = "Text size", MinWidth = 160 };
        foreach ((string name, _) in s_sizes)
            size.Items.Add(name);
        size.SelectedIndex = Math.Max(0, Array.FindIndex(s_sizes, s => s.Size == NoteBox.FontSize));
        size.SelectionChanged += (_, _) =>
        {
            NoteBox.FontSize = s_sizes[size.SelectedIndex].Size;
            SaveSettings(Settings with { FontSize = NoteBox.FontSize });
        };
        return size;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                NoteBox.Text = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not read {_path}", ex);
        }
    }

    // Also once after loading (the event comes later): the same text is written back.
    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // The text box ends lines with a lone CR; the file gets Windows' line ends.
            File.WriteAllText(_path, NoteBox.Text.ReplaceLineEndings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not save {_path}", ex);
        }
    }
}
