namespace ForgeLinkSms.Core.Services;

public interface IMarkAsReadService
{
    Task<IReadOnlyList<long>> MarkAllAsReadAsync();
    Task MarkThreadAsReadAsync(long threadId);
    Task MarkThreadsAsReadAsync(IReadOnlyList<long> threadIds);
    Task MarkThreadsAsUnreadAsync(IReadOnlyList<long> threadIds);
}
