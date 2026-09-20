namespace SmsMessenger.Core.Services;

public interface IMarkAsReadService
{
    Task<IReadOnlyList<long>> MarkAllAsReadAsync();
    Task MarkThreadAsReadAsync(long threadId);
    Task MarkThreadsAsUnreadAsync(IReadOnlyList<long> threadIds);
}
