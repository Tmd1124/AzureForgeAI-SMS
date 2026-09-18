namespace SmsMessenger.Core.Data;

public interface ITrashRepository
{
    Task InitializeAsync();
    Task TrashThreadAsync(long threadId);
    Task RestoreThreadAsync(long threadId);
    Task<bool> IsTrashedAsync(long threadId);
    Task<IReadOnlyList<long>> GetTrashedThreadIdsAsync();
}
