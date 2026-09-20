using SQLite;
using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Data;

public class ArchiveRepository : IArchiveRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public ArchiveRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<ArchivedThread>();

    public Task ArchiveThreadAsync(long threadId) =>
        _db.InsertOrReplaceAsync(new ArchivedThread { ThreadId = threadId, ArchivedAtUtc = DateTimeOffset.UtcNow });

    public Task UnarchiveThreadAsync(long threadId) => _db.DeleteAsync<ArchivedThread>(threadId);

    public async Task<bool> IsArchivedAsync(long threadId) =>
        await _db.FindAsync<ArchivedThread>(threadId) is not null;

    public async Task<IReadOnlyList<long>> GetArchivedThreadIdsAsync()
    {
        var rows = await _db.Table<ArchivedThread>().ToListAsync();
        return rows.Select(r => r.ThreadId).ToList();
    }

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
