using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public interface ISnoozeRepository
{
    Task InitializeAsync();
    Task SnoozeThreadAsync(long threadId, DateTimeOffset untilUtc);
    Task UnsnoozeThreadAsync(long threadId);
    Task<SnoozedThread?> GetAsync(long threadId);
    Task<IReadOnlyList<SnoozedThread>> GetAllAsync();
}
