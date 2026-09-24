namespace ForgeLinkSms.Core.Services;

public interface IIncomingMessageNotifier
{
    event Action<long>? MessageReceived;
    void NotifyMessageReceived(long threadId);
}
