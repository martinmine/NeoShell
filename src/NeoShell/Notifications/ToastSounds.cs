using NeoShell.Interop.Audio;
using NeoShell.Interop.Notifications;

namespace NeoShell.Notifications;

/// <summary>A toast's sound: an <c>ms-winsoundevent:</c> sound or a file URI, looped while the toast shows or not.</summary>
public sealed record ToastSound(string Source, bool Loop);

/// <summary>
/// Which sound a toast plays, as Windows' notification platform picks it (NotificationController.dll's
/// <c>SoundPropertiesFactory</c>, Windows 11 25H2) for the toast host to play.
/// </summary>
public static class ToastSounds
{
    private const string LoopingAlarm = "ms-winsoundevent:Notification.Looping.Alarm";
    private const string LoopingCall = "ms-winsoundevent:Notification.Looping.Call";

    /// <summary>The sound, or null for none.</summary>
    /// <param name="audio">What the toast asks for; null plays the default sound.</param>
    /// <param name="soundsAllowed">Settings → System → Notifications → "Allow notifications to play sounds".</param>
    /// <param name="appSoundFile">The app's <c>SoundFile</c> setting: null (or <c>*default*</c>) for the toast's own
    /// sound, empty for none (the app's "Play a sound when a notification arrives" off), else a file to play instead.</param>
    public static ToastSound? Choose(ToastAudio? audio, bool soundsAllowed, string? appSoundFile)
    {
        if (!soundsAllowed || audio?.Silent == true || appSoundFile?.Length == 0)
            return null;

        // An alarm and an incoming call always ring with one of their looping sounds (played once unless the toast
        // asks for a loop).
        string source = audio?.Source ?? NotificationSound.Default;
        if (IsScenario(audio, "alarm") && !StartsWith(source, LoopingAlarm))
            source = LoopingAlarm;
        else if (IsScenario(audio, "incomingCall") && !StartsWith(source, LoopingCall))
            source = LoopingCall;

        // The app's own sound file wins over all of these.
        if (appSoundFile is not null && appSoundFile != "*default*")
            source = Path.IsPathFullyQualified(appSoundFile) ? new Uri(appSoundFile).AbsoluteUri : appSoundFile;
        return new ToastSound(source, audio?.Loop == true);
    }

    private static bool IsScenario(ToastAudio? audio, string scenario) =>
        string.Equals(audio?.Scenario, scenario, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWith(string source, string prefix) => source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
