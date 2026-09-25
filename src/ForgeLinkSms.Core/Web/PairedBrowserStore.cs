using SQLite;

namespace ForgeLinkSms.Core.Web;

public class PairedBrowser
{
    [PrimaryKey]
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset PairedAtUtc { get; set; }
}

public interface IPairedBrowserStore
{
    Task InitializeAsync();
    Task AddAsync(string tokenHash);
    Task<bool> ContainsAsync(string tokenHash);
    Task<int> CountAsync();
    Task ClearAsync();
}

public class PairedBrowserStore : IPairedBrowserStore, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public PairedBrowserStore(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<PairedBrowser>();

    public Task AddAsync(string tokenHash) =>
        _db.InsertOrReplaceAsync(new PairedBrowser { TokenHash = tokenHash, PairedAtUtc = DateTimeOffset.UtcNow });

    public async Task<bool> ContainsAsync(string tokenHash) =>
        await _db.FindAsync<PairedBrowser>(tokenHash).ConfigureAwait(false) is not null;

    public Task<int> CountAsync() => _db.Table<PairedBrowser>().CountAsync();

    public Task ClearAsync() => _db.DeleteAllAsync<PairedBrowser>();

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
