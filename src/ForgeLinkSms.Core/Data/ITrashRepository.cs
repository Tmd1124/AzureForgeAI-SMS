namespace ForgeLinkSms.Core.Data;

public interface ITrashRepository
{
    Task InitializeAsync();
    Task TrashThreadAsync(long threadId);
    Task RestoreThreadAsync(long threadId);
    Task<bool> IsTrashedAsync(long threadId);
    Task<IReadOnlyList<long>> GetTrashedThreadIdsAsync();

    /// Thread IDs trashed before olderThan — i.e. eligible for the 30-day auto-purge.
    Task<IReadOnlyList<long>> GetExpiredThreadIdsAsync(DateTimeOffset olderThan);
}
