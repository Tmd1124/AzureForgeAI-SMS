namespace ForgeLinkSms.Core.Services;

public interface ISnoozeService
{
    Task SnoozeAsync(long threadId, DateTimeOffset untilUtc);
    Task UnsnoozeAsync(long threadId);
    Task<bool> WakeAsync(long threadId);
    Task WakeExpiredAsync(DateTimeOffset now);
    Task<IReadOnlyDictionary<long, DateTimeOffset>> GetSnoozedAsync();
    Task RearmAllAsync();
}
