using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class FilterRepository : IFilterRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public FilterRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public async Task InitializeAsync()
    {
        await _db.CreateTableAsync<Filter>();
        await _db.CreateTableAsync<ThreadFilterAssignment>();
    }

    public async Task<IReadOnlyList<Filter>> GetAllFiltersAsync() =>
        await _db.Table<Filter>().ToListAsync();

    public async Task<Filter> CreateFilterAsync(string name, string colorHex)
    {
        var filter = new Filter { Name = name, ColorHex = colorHex };
        await _db.InsertAsync(filter);
        return filter;
    }

    public Task RenameFilterAsync(long filterId, string newName) =>
        _db.ExecuteAsync("UPDATE Filter SET Name = ? WHERE Id = ?", newName, filterId);

    public Task SetFilterColorAsync(long filterId, string colorHex) =>
        _db.ExecuteAsync("UPDATE Filter SET ColorHex = ? WHERE Id = ?", colorHex, filterId);

    public async Task DeleteFilterAsync(long filterId)
    {
        await _db.ExecuteAsync("DELETE FROM ThreadFilterAssignment WHERE FilterId = ?", filterId);
        await _db.DeleteAsync<Filter>(filterId);
    }

    public async Task<IReadOnlyDictionary<long, List<long>>> GetAllAssignmentsAsync()
    {
        var rows = await _db.Table<ThreadFilterAssignment>().ToListAsync();
        return rows
            .GroupBy(r => r.ThreadId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.FilterId).ToList());
    }

    public async Task AssignFilterAsync(long threadId, long filterId)
    {
        var existing = await _db.Table<ThreadFilterAssignment>()
            .Where(a => a.ThreadId == threadId && a.FilterId == filterId)
            .FirstOrDefaultAsync();
        if (existing is null)
        {
            await _db.InsertAsync(new ThreadFilterAssignment { ThreadId = threadId, FilterId = filterId });
        }
    }

    public Task UnassignFilterAsync(long threadId, long filterId) =>
        _db.ExecuteAsync("DELETE FROM ThreadFilterAssignment WHERE ThreadId = ? AND FilterId = ?", threadId, filterId);

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
