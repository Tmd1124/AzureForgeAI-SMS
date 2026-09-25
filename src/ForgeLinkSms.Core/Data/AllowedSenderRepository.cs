using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class AllowedSenderRepository : IAllowedSenderRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public AllowedSenderRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<AllowedSender>();

    public Task AllowAsync(string normalizedAddress) =>
        _db.InsertOrReplaceAsync(new AllowedSender { NormalizedAddress = normalizedAddress, AllowedAtUtc = DateTimeOffset.UtcNow });

    public Task DisallowAsync(string normalizedAddress) => _db.DeleteAsync<AllowedSender>(normalizedAddress);

    public async Task<bool> IsAllowedAsync(string normalizedAddress) =>
        await _db.FindAsync<AllowedSender>(normalizedAddress) is not null;

    public async Task<IReadOnlySet<string>> GetAllAsync() =>
        (await _db.Table<AllowedSender>().ToListAsync()).Select(r => r.NormalizedAddress).ToHashSet();

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
