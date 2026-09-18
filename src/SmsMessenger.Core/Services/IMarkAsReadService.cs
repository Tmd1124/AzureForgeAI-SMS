namespace SmsMessenger.Core.Services;

public interface IMarkAsReadService
{
    Task<IReadOnlyList<long>> MarkAllAsReadAsync();
    Task MarkThreadsAsUnreadAsync(IReadOnlyList<long> threadIds);
}
