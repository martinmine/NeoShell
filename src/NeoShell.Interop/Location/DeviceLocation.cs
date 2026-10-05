using Windows.Devices.Geolocation;

namespace NeoShell.Interop.Location;

/// <summary>Where the computer is, from Windows' location service.</summary>
public static class DeviceLocation
{
    /// <summary>
    /// The latitude and longitude, or null when location is turned off, desktop apps may not use it (Settings → Privacy
    /// &amp; security → Location) or no position could be found.
    /// </summary>
    public static Task<(double Latitude, double Longitude)?> GetAsync() =>
        // From the thread pool: asked from the UI thread, Windows answers with a prompt to turn location on (and the
        // answer waits for the user), which a shell shouldn't show at sign-in.
        Task.Run(ReadAsync);

    private static async Task<(double Latitude, double Longitude)?> ReadAsync()
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
                return null;

            // Weather needs no more than the town: a position from the last hour is fine and comes at once.
            Geoposition position = await new Geolocator().GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(15));
            BasicGeoposition point = position.Coordinate.Point.Position;
            return (point.Latitude, point.Longitude);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or TaskCanceledException)
        {
            return null;
        }
    }
}
