using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class FavoriteRepository : IFavoriteRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public FavoriteRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<FavoriteThread>();

    public Task FavoriteThreadAsync(long threadId) =>
        _db.InsertOrReplaceAsync(new FavoriteThread { ThreadId = threadId, FavoritedAtUtc = DateTimeOffset.UtcNow });

    public Task UnfavoriteThreadAsync(long threadId) => _db.DeleteAsync<FavoriteThread>(threadId);

    public async Task<bool> IsFavoriteAsync(long threadId) =>
        await _db.FindAsync<FavoriteThread>(threadId) is not null;

    public async Task<IReadOnlyList<long>> GetFavoriteThreadIdsAsync()
    {
        var rows = await _db.Table<FavoriteThread>().ToListAsync();
        return rows.Select(r => r.ThreadId).ToList();
    }

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
