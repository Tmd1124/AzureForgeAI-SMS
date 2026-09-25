using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class SnoozeRepository : ISnoozeRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public SnoozeRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<SnoozedThread>();

    public Task SnoozeThreadAsync(long threadId, DateTimeOffset untilUtc) =>
        _db.InsertOrReplaceAsync(new SnoozedThread { ThreadId = threadId, UntilUtc = untilUtc });

    public Task UnsnoozeThreadAsync(long threadId) => _db.DeleteAsync<SnoozedThread>(threadId);

    public async Task<SnoozedThread?> GetAsync(long threadId) => await _db.FindAsync<SnoozedThread>(threadId);

    public async Task<IReadOnlyList<SnoozedThread>> GetAllAsync() => await _db.Table<SnoozedThread>().ToListAsync();

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
