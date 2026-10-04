using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NeoShell.Interop.Audio;

namespace NeoShell.Tray;

/// <summary>An app's row in the volume flyout's mixer: its icon, name, volume slider and mute.</summary>
internal sealed class MixerApp(string key) : INotifyPropertyChanged
{
    private AudioApp? _audio;
    private string _name = "";
    private ImageSource? _icon;
    private double _volume;
    private bool _isMuted;
    private bool _updating;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; } = key;

    public string Name { get => _name; private set => Set(ref _name, value); }

    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (Set(ref _icon, value))
            {
                Raise(nameof(IconVisibility));
                Raise(nameof(GlyphVisibility));
            }
        }
    }

    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>A speaker in place of an icon: for the system sounds, or until the app's icon has loaded.</summary>
    public Visibility GlyphVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The app's volume in percent; moving the slider sets it, and turning it up unmutes, as in Windows.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (!Set(ref _volume, value))
                return;

            Raise(nameof(VolumePercent));
            if (_updating || _audio is null)
                return;

            _audio.Volume = (float)(value / 100);
            if (IsMuted && value > 0)
                IsMuted = false;
        }
    }

    public string VolumePercent => Math.Round(Volume).ToString();

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (!Set(ref _isMuted, value))
                return;

            Raise(nameof(MutedVisibility));
            Raise(nameof(MuteName));
            if (!_updating && _audio is not null)
                _audio.IsMuted = value;
        }
    }

    public Visibility MutedVisibility => IsMuted ? Visibility.Visible : Visibility.Collapsed;

    public string MuteName => IsMuted ? $"Unmute {Name}" : $"Mute {Name}";

    /// <summary>Shows the app's current state; doesn't set anything back.</summary>
    public void Update(AudioApp audio, string name, ImageSource? icon)
    {
        _audio = audio;
        Name = name;
        Icon = icon;
        _updating = true;
        Volume = Math.Round(audio.Volume * 100);
        IsMuted = audio.IsMuted;
        _updating = false;
        Raise(nameof(MuteName));
    }

    // UI Automation names list items by their ToString.
    public override string ToString() => Name;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
