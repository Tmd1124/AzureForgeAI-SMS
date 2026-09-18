using SQLite;
using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Data;

public class BlockedNumberRepository : IBlockedNumberRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public BlockedNumberRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<BlockedNumber>();

    public Task BlockAsync(string phoneNumber) =>
        _db.InsertOrReplaceAsync(new BlockedNumber { PhoneNumber = phoneNumber, BlockedAtUtc = DateTimeOffset.UtcNow });

    public Task UnblockAsync(string phoneNumber) => _db.DeleteAsync<BlockedNumber>(phoneNumber);

    public async Task<bool> IsBlockedAsync(string phoneNumber) =>
        await _db.FindAsync<BlockedNumber>(phoneNumber) is not null;

    public async Task<IReadOnlyList<BlockedNumber>> GetBlockedNumbersAsync()
    {
        var rows = await _db.Table<BlockedNumber>().ToListAsync();
        return rows;
    }

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
