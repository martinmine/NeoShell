using NeoShell.Interop.Notifications;
using NeoShell.Interop.Shell;
using NeoShell.Interop.Windowing;
using NeoShell.Logging;
using NeoShell.Notifications;
using NeoShell.Interop.Tray;
using Windows.UI;

namespace NeoShell.AutoPlay;

/// <summary>
/// AutoPlay as the shell: Windows' own runs only inside Explorer (twinui needs Explorer's place in the notification
/// band), so without it nothing happens when a USB stick, memory card or disc arrives. As Explorer: it works out
/// what the media holds, runs the user's saved choice, or asks with a banner toast ("Select what happens with
/// removable drives.") whose click opens the choices; a choice is remembered and runs.
/// </summary>
internal sealed class VolumeAutoPlay : IDisposable
{
    private const string ToastAppId = "Windows.SystemToast.AutoPlay";
    private const string Twinui = @"@%SystemRoot%\system32\twinui.dll,-";
    private const string Shell32 = @"@%SystemRoot%\system32\shell32.dll,-";

    private readonly VolumeArrivals _arrivals = new();
    private readonly Func<ToastPopups?> _toasts;
    // Drives being asked about, by root, with their flyout once opened.
    private readonly Dictionary<string, AutoPlayFlyout?> _asking = new(StringComparer.OrdinalIgnoreCase);

    public VolumeAutoPlay(Func<ToastPopups?> toasts)
    {
        _toasts = toasts;
        _arrivals.Arrived += OnArrived;
        _arrivals.Removed += OnRemoved;
    }

    public void Dispose()
    {
        _arrivals.Dispose();
        foreach (string root in _asking.Keys.ToList())
            OnRemoved(root);
    }

    private async void OnArrived(string root)
    {
        if (_asking.ContainsKey(root))
            return;

        _asking[root] = null;
        try
        {
            if (!await StartAsync(root))
                _asking.Remove(root);
        }
        catch (Exception ex)
        {
            Log.Warn($"AutoPlay for {root} failed", ex);
            _asking.Remove(root);
        }
    }

    // The volume's media, then the saved choice or a toast asking; true while it asks.
    private async Task<bool> StartAsync(string root)
    {
        AutoPlayVolume? volume = await Task.Run(() => AutoPlayVolumes.Inspect(root));
        if (volume is null || AutoPlayVolumes.IsTurnedOff() || AutoPlayVolumes.IsBlockedByPolicy(root, volume.Type) || AutoPlayVolumes.IsGameRunning())
            return false;

        uint autorunPolicy = AutoPlayVolumes.AutorunPolicy();
        bool perMediaType = AutoPlayHandlers.GetChoice(true, "StorageOnArrival", null) == AutoPlayRules.UseAdvancedStorageOptions;
        AutoPlayContent content = AutoPlayRules.Initial(volume, perMediaType, autorunPolicy);
        List<AutoPlayContent>? found = null;
        if (content == AutoPlayContent.None)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            IReadOnlyList<MediaKind> kinds = await Task.Run(() => AutoPlayVolumes.Sniff(root, timeout.Token));
            found = [.. kinds.Select(kind => kind switch
            {
                MediaKind.Music => AutoPlayContent.Music,
                MediaKind.Pictures => AutoPlayContent.Pictures,
                _ => AutoPlayContent.Videos,
            })];
            content = AutoPlayRules.Final(content, found);
        }
        if (AutoPlayRules.EventFor(content) is not { } autoPlayEvent)
            return false;

        // An app in front (a disc burner) may stop it; Explorer waits 3 seconds for the answer.
        Task<bool> cancels = Task.Run(() => AutoPlayVolumes.AppCancels(volume, AutoPlayRules.ArContent(content)));
        if (await Task.WhenAny(cancels, Task.Delay(TimeSpan.FromSeconds(3))) == cancels && cancels.Result)
        {
            Log.Info($"AutoPlay for {root} cancelled by an app");
            return false;
        }

        AutoPlayHandler? program = content.HasFlag(AutoPlayContent.Autorun) && volume.Autorun is { } autorun
            ? await Task.Run(() => ProgramChoice(autorun))
            : null;
        string? saved = autorunPolicy == 2 && program is not null
            ? AutoPlayRules.AutoRun
            : AutoPlayHandlers.GetChoice(true, autoPlayEvent.Name, autoPlayEvent.ChoiceGroup);
        AutoPlayHandler? savedHandler = saved == AutoPlayRules.AutoRun ? program : saved is null ? null : AutoPlayHandlers.Read(saved);
        Log.Info($"AutoPlay for {root}: {content}, {autoPlayEvent.Name}, saved choice {saved ?? "none"}");

        switch (AutoPlayRules.Start(saved, savedHandler is not null))
        {
            case AutoPlayStart.Nothing:
                return false;
            case AutoPlayStart.RunSaved:
                AutoPlayHandlers.SetChoice(false, autoPlayEvent.Name, autoPlayEvent.ChoiceGroup, savedHandler!.Name);
                Run(savedHandler, volume);
                return false;
        }

        IReadOnlyList<AutoPlayGroup> groups = await Task.Run(() =>
            AutoPlayRules.Groups(content, program, AutoPlayHandlers.ForEvent, AutoPlayHandlers.Read, found));
        if (!AutoPlayRules.HasChoices(groups))
            return false;

        string description = AutoPlayHandlers.Text(Shell32 + autoPlayEvent.Description) ?? "";
        var toast = new ToastInfo(
            0, ToastAppId, AutoPlayHandlers.Text(Twinui + 9914) ?? "AutoPlay", DateTimeOffset.Now, volume.DisplayName,
            AutoPlayHandlers.Format(AutoPlayHandlers.Text(Twinui + 9992) ?? "Select what happens with %1.", description));
        bool remember = AutoPlayRules.RemembersChoice(content, saved);
        return _toasts()?.ShowBanner(ToastKey(root), toast, result =>
        {
            if (result == BalloonEvent.Clicked)
                Open(volume, content, autoPlayEvent, description, groups, remember);
            else
                _asking.Remove(root);
        }) == true;
    }

    // A disc's program, as shell32 names it: autorun.inf's action or "Run setup.exe", and its signer.
    private static AutoPlayHandler ProgramChoice(AutorunInf autorun)
    {
        string program = AutoPlayVolumes.SplitCommand(autorun.Command!).Program;
        string action = AutoPlayHandlers.Text(autorun.Action)
            ?? AutoPlayHandlers.Format(AutoPlayHandlers.Text(Shell32 + 17427) ?? "Run %1", Path.GetFileName(program));
        return new AutoPlayHandler(AutoPlayRules.AutoRun, action, AutoPlayHandlers.PublisherText(program), autorun.Icon, 0);
    }

    private void Open(
        AutoPlayVolume volume, AutoPlayContent content, AutoPlayEvent autoPlayEvent, string description,
        IReadOnlyList<AutoPlayGroup> groups, bool remember)
    {
        if (!_asking.ContainsKey(volume.Root) || DisplayMonitor.GetAll().FirstOrDefault(m => m.IsPrimary) is not { } monitor)
            return;

        // Any other drive's choices go: there's one flyout at a time.
        foreach ((string root, AutoPlayFlyout? open) in _asking.Where(a => a.Value is not null).ToList())
            open!.Shut();

        uint tile = ImmersiveColors.Get("ImmersiveStartDesktopTilesBackground") ?? 0xFF0063B1;
        var chooser = new AutoPlayChooser(
            volume.DisplayName,
            AutoPlayHandlers.Format(AutoPlayHandlers.Text(Twinui + 9978) ?? "Choose what to do with %1.", description),
            groups,
            (AutoPlayHandlers.Text(Twinui + 9926) ?? "", AutoPlayHandlers.Text(Twinui + 9927) ?? "", AutoPlayHandlers.Text(Twinui + 9904) ?? ""),
            Color.FromArgb(0xFF, (byte)(tile >> 16), (byte)(tile >> 8), (byte)tile),
            volume.Root,
            monitor.Dpi / 96.0,
            AutoPlayHandlers.GetChoice(false, autoPlayEvent.Name, autoPlayEvent.ChoiceGroup));
        var flyout = new AutoPlayFlyout(chooser, monitor);
        chooser.Chosen += handler =>
        {
            // As twinui: the choice is selected next time; it runs by itself from now on unless this content always
            // asks, which is then written down as "Ask me every time".
            AutoPlayHandlers.SetChoice(false, autoPlayEvent.Name, autoPlayEvent.ChoiceGroup, handler.Name);
            AutoPlayHandlers.SetChoice(true, autoPlayEvent.Name, autoPlayEvent.ChoiceGroup, remember ? handler.Name : AutoPlayRules.PromptEachTime);
            Log.Info($"AutoPlay for {volume.Root}: chose {handler.Name}");
            flyout.Shut();
            Run(handler, volume);
        };
        flyout.Closed += (_, _) =>
        {
            if (_asking.TryGetValue(volume.Root, out AutoPlayFlyout? current) && current == flyout)
                _asking.Remove(volume.Root);
        };
        _asking[volume.Root] = flyout;
        flyout.Open();
    }

    private static void Run(AutoPlayHandler handler, AutoPlayVolume volume)
    {
        void Failed(Exception ex) => Log.Warn($"AutoPlay could not run {handler.Name} for {volume.Root}", ex);

        if (handler.Name == AutoPlayRules.TakeNoAction)
            return;
        if (handler.Name == AutoPlayRules.AutoRun && volume.Autorun is { } autorun)
            AutoPlayHandlers.RunAutorun(autorun, volume.Root, Failed);
        else
            AutoPlayHandlers.Invoke(handler, volume.Root, Failed);
    }

    // The toast and flyout go with the media, as Explorer's.
    private void OnRemoved(string root)
    {
        if (!_asking.Remove(root, out AutoPlayFlyout? flyout))
            return;
        _toasts()?.Hide(ToastKey(root));
        flyout?.Shut();
    }

    private static string ToastKey(string root) => "AutoPlay " + root;
}
