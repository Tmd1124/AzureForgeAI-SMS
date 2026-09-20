namespace SmsMessenger.Core.Data;

public interface IArchiveRepository
{
    Task InitializeAsync();
    Task ArchiveThreadAsync(long threadId);
    Task UnarchiveThreadAsync(long threadId);
    Task<bool> IsArchivedAsync(long threadId);
    Task<IReadOnlyList<long>> GetArchivedThreadIdsAsync();
}
