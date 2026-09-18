using SQLite;
using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Data;

public class TrashRepository : ITrashRepository, IAsyncDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public TrashRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<TrashedThread>();

    public Task TrashThreadAsync(long threadId) =>
        _db.InsertOrReplaceAsync(new TrashedThread { ThreadId = threadId, TrashedAtUtc = DateTimeOffset.UtcNow });

    public Task RestoreThreadAsync(long threadId) => _db.DeleteAsync<TrashedThread>(threadId);

    public async Task<bool> IsTrashedAsync(long threadId) =>
        await _db.FindAsync<TrashedThread>(threadId) is not null;

    public async Task<IReadOnlyList<long>> GetTrashedThreadIdsAsync()
    {
        var rows = await _db.Table<TrashedThread>().ToListAsync();
        return rows.Select(r => r.ThreadId).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.CloseAsync();
    }
}
