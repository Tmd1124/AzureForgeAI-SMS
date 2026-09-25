using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class DraftRepository : IDraftRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public DraftRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<Draft>();

    public async Task<string?> GetAsync(long threadId) =>
        (await _db.FindAsync<Draft>(threadId).ConfigureAwait(false))?.Text;

    public Task SaveAsync(long threadId, string text) => string.IsNullOrWhiteSpace(text)
        ? _db.DeleteAsync<Draft>(threadId)
        : _db.InsertOrReplaceAsync(new Draft { ThreadId = threadId, Text = text, UpdatedAtUtc = DateTimeOffset.UtcNow });

    public async Task<IReadOnlyDictionary<long, string>> GetAllAsync() =>
        (await _db.Table<Draft>().ToListAsync().ConfigureAwait(false)).ToDictionary(d => d.ThreadId, d => d.Text);

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
