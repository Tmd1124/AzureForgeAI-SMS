using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

public class LocationService : ILocationService
{
    public async Task<(double Latitude, double Longitude)?> GetCurrentLocationAsync()
    {
        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            return null;
        }

        var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)))
            ?? await Geolocation.Default.GetLastKnownLocationAsync();

        return location is null ? null : (location.Latitude, location.Longitude);
    }
}
