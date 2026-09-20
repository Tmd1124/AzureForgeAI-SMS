namespace SmsMessenger.Core.Services;

public interface IAppResumeNotifier
{
    event Action? Resumed;
    void NotifyResumed();
}
