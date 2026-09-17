namespace SmsMessenger.Core.Services;

public interface INavigationService
{
    Task NavigateToAsync(string route);
}
