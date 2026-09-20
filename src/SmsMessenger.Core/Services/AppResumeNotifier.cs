namespace SmsMessenger.Core.Services;

public class AppResumeNotifier : IAppResumeNotifier
{
    public event Action? Resumed;

    public void NotifyResumed() => Resumed?.Invoke();
}
