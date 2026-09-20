namespace ForgeLinkSms.Core.Services;

public interface ILocationService
{
    Task<(double Latitude, double Longitude)?> GetCurrentLocationAsync();
}
