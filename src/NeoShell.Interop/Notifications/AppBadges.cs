using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NeoShell.Interop.Com;
using NeoShell.Interop.Native;

namespace NeoShell.Interop.Notifications;

/// <summary>The glyphs of badge notifications, numbered as Windows' <c>BadgeGlyphKind</c>.</summary>
public enum BadgeGlyph
{
    None = 0,
    Activity = 1,
    Alert = 2,
    Alarm = 3,
    Available = 4,
    Away = 5,
    Busy = 6,
    NewMessage = 7,
    Paused = 8,
    Playing = 9,
    Unavailable = 10,
    Error = 11,
    Attention = 12,
}

/// <summary>An app's badge notification: a count (<see cref="Glyph"/> is None), or a glyph.</summary>
public sealed record AppBadge(uint Number, BadgeGlyph Glyph)
{
    /// <summary>What Windows' badge store holds, as shown: nothing for no badge, a count of 0 or an unknown glyph.</summary>
    internal static AppBadge? From(int kind, uint number, int glyph) => kind switch
    {
        1 when number > 0 => new AppBadge(number, BadgeGlyph.None),
        2 when glyph is >= (int)BadgeGlyph.Activity and <= (int)BadgeGlyph.Attention => new AppBadge(0, (BadgeGlyph)glyph),
        _ => null,
    };
}

/// <summary>
/// The badges apps set with <c>BadgeUpdateManager</c> (only apps with package identity can), read from Windows' badge
/// store as Explorer's taskbar reads them.
/// </summary>
/// <remarks>
/// Windows keeps an app's badge whether or not it runs, and only tells a reader about the apps it has asked about:
/// <see cref="Get"/> starts watching an app, and its value arrives a moment later with <see cref="Changed"/>. The store
/// is only touched on thread-pool threads, as Explorer does: it holds a lock while it raises its change events, and an
/// event marshalled to a UI thread that waits for that lock would never arrive.
/// </remarks>
public sealed class AppBadges : IDisposable
{
    private readonly Lazy<IBadgeProvider?> _provider = new(CreateProvider);
    private readonly ConcurrentDictionary<string, AppBadge?> _badges = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<(IBadge Badge, long Token)> _watched = [];
    private readonly Lock _reading = new();

    /// <summary>An app's badge changed, by its AppUserModelID. Raised on a thread-pool thread.</summary>
    public event Action<string>? Changed;

    /// <summary>The app's badge as last read, or null for none; watches the app from now on.</summary>
    public AppBadge? Get(string appId)
    {
        if (_badges.TryGetValue(appId, out AppBadge? badge))
            return badge;
        if (_badges.TryAdd(appId, null))
            ThreadPool.QueueUserWorkItem(_ => Watch(appId));
        return null;
    }

    public void Dispose()
    {
        foreach ((IBadge badge, long token) in _watched)
            badge.RemoveChanged(token);
        _watched.Clear();
    }

    private static IBadgeProvider? CreateProvider()
    {
        try
        {
            var statics = Combase.GetActivationFactory<IBadgeProviderStatics>("WindowsUdk.UI.StartScreen.BadgeProvider");
            Marshal.ThrowExceptionForHR(statics.GetForUser(0, out IBadgeProvider provider));
            return provider;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // Not there in this Windows build: no badges.
            return null;
        }
    }

    private void Watch(string appId)
    {
        try
        {
            if (_provider.Value is not { } provider)
                return;
            Marshal.ThrowExceptionForHR(Combase.WindowsCreateString(appId, (uint)appId.Length, out nint name));
            IBadge badge;
            try
            {
                Marshal.ThrowExceptionForHR(provider.GetRegisteredBadge(name, out badge));
            }
            finally
            {
                Combase.WindowsDeleteString(name);
            }
            // Read once the event has returned and the store has let go of its locks.
            var handler = new BadgeChangedHandler(() => ThreadPool.QueueUserWorkItem(_ => Read(appId, badge)));
            if (badge.AddChanged(handler, out long token) >= 0)
                _watched.Add((badge, token));
            Read(appId, badge);
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
    }

    private void Read(string appId, IBadge badge)
    {
        try
        {
            AppBadge? value;
            // One read at a time, so that an older value can't land after a newer one.
            lock (_reading)
            {
                badge.GetKind(out int kind);
                badge.GetNumber(out uint number);
                badge.GetGlyph(out int glyph);
                value = AppBadge.From(kind, number, glyph);
                if (_badges.TryGetValue(appId, out AppBadge? old) && old == value)
                    return;
                _badges[appId] = value;
            }
            Changed?.Invoke(appId);
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
    }
}

[GeneratedComClass]
internal sealed partial class BadgeChangedHandler(Action onChange) : IBadgeChangedHandler
{
    public int Invoke(nint sender, nint args)
    {
        try
        {
            onChange();
        }
        catch (Exception ex)
        {
            NativeCallback.Report(ex);
        }
        return 0;
    }
}
