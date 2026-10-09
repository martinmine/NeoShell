using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace NeoShell.Interop.Audio;

/// <summary>
/// Plays toast sounds as Explorer's toasts do (ShellExperienceHost's audio player): a sound of the user's sound scheme
/// (<c>ms-winsoundevent:Notification.*</c>, Control Panel → Sound → Sounds) or a file, as an alert, looped while asked.
/// One sound at a time: a new one takes the place of the last.
/// </summary>
public sealed class NotificationSound : IDisposable
{
    public const string Default = "ms-winsoundevent:Notification.Default";
    private const string SoundEvent = "ms-winsoundevent:";

    private readonly MediaPlayer _player = new() { AudioCategory = MediaPlayerAudioCategory.Alerts };

    public NotificationSound()
    {
        // Not a media session: it would show in the media controls of Quick Settings and the media widget.
        _player.CommandManager.IsEnabled = false;
        // A sound the scheme doesn't have, or a file that won't play: Explorer plays the default sound instead.
        _player.MediaFailed += (player, _) =>
        {
            if (player.Source is MediaSource { Uri: { } uri } && uri.OriginalString != Default)
                Start(new Uri(Default), player.IsLoopingEnabled);
        };
        // Opened ahead, not played: the first sound would otherwise start a tenth of a second after its toast's.
        _player.Source = MediaSource.CreateFromUri(new Uri(Default));
    }

    /// <param name="appId">The toast's app, which a package-relative file (<c>ms-appx:///</c>, <c>ms-appdata:///local/</c>) is in.</param>
    /// <param name="source">An <c>ms-winsoundevent:</c> sound or a file URI, as a toast's <c>src</c>.</param>
    public void Play(string appId, string source, bool loop)
    {
        string? found = Locate(source, () => PackageFolder(appId), () => PackageData(appId));
        Start(found is null ? new Uri(Default) : new Uri(found), loop);
    }

    public void Stop() => _player.Source = null;

    public void Dispose() => _player.Dispose();

    private void Start(Uri sound, bool loop)
    {
        _player.IsLoopingEnabled = loop;
        _player.Source = MediaSource.CreateFromUri(sound);
        _player.Play();
    }

    /// <summary>
    /// The sound to play: an <c>ms-winsoundevent:</c> sound as it is, else the file's path, as the notification platform
    /// takes them (<c>ms-appx:///</c> and <c>ms-appdata:///local/</c> in the app's package, <c>file:///</c>); null for
    /// a source it doesn't play, which gets the default sound.
    /// </summary>
    internal static string? Locate(string source, Func<string?> packageFolder, Func<string?> packageData)
    {
        if (source.StartsWith(SoundEvent, StringComparison.OrdinalIgnoreCase))
            return source;
        if (Relative(source, "ms-appx:///") is { } inPackage)
            return packageFolder() is { } folder ? Path.Combine(folder, inPackage) : null;
        if (Relative(source, "ms-appdata:///local/") is { } inData)
            return packageData() is { } data ? Path.Combine(data, inData) : null;
        return source.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(source, UriKind.Absolute, out Uri? file)
            ? file.LocalPath
            : null;
    }

    private static string? Relative(string source, string prefix) =>
        source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? Uri.UnescapeDataString(source[prefix.Length..]).Replace('/', Path.DirectorySeparatorChar)
            : null;

    /// <summary>A packaged app's install folder, where <c>ms-appx:///</c> points; null for an unpackaged app.</summary>
    internal static string? PackageFolder(string appId)
    {
        try
        {
            return appId.Contains('!') ? AppInfo.GetFromAppUserModelId(appId).Package.InstalledLocation.Path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or COMException)
        {
            return null;
        }
    }

    /// <summary>The package's local app data, where <c>ms-appdata:///local/</c> points.</summary>
    internal static string? PackageData(string appId) =>
        appId.Split('!') is [var family, _]
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", family, "LocalState")
            : null;
}
