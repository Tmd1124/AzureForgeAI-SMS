using SQLite;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Data;

public class TrashRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TrashRepository _repository;

    public TrashRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trash-test-{Guid.NewGuid()}.db3");
        _repository = new TrashRepository(_dbPath);
        _repository.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _repository.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task GetTrashedThreadIdsAsync_is_empty_initially()
    {
        var ids = await _repository.GetTrashedThreadIdsAsync();

        Assert.Empty(ids);
    }

    [Fact]
    public async Task TrashThreadAsync_then_GetTrashedThreadIdsAsync_returns_it()
    {
        await _repository.TrashThreadAsync(42);

        var ids = await _repository.GetTrashedThreadIdsAsync();

        Assert.Equal(new long[] { 42 }, ids);
    }

    [Fact]
    public async Task IsTrashedAsync_reflects_trashed_state()
    {
        Assert.False(await _repository.IsTrashedAsync(7));

        await _repository.TrashThreadAsync(7);

        Assert.True(await _repository.IsTrashedAsync(7));
    }

    [Fact]
    public async Task RestoreThreadAsync_removes_it_from_the_trashed_list()
    {
        await _repository.TrashThreadAsync(42);

        await _repository.RestoreThreadAsync(42);

        Assert.Empty(await _repository.GetTrashedThreadIdsAsync());
    }

    [Fact]
    public async Task GetExpiredThreadIdsAsync_excludes_threads_trashed_after_the_cutoff()
    {
        await _repository.TrashThreadAsync(1);

        var expired = await _repository.GetExpiredThreadIdsAsync(DateTimeOffset.UtcNow.AddDays(-30));

        Assert.Empty(expired);
    }

    [Fact]
    public async Task GetExpiredThreadIdsAsync_includes_threads_trashed_before_the_cutoff()
    {
        await _repository.TrashThreadAsync(1);
        await BackdateTrashedAtAsync(threadId: 1, trashedAtUtc: DateTimeOffset.UtcNow.AddDays(-31));

        var expired = await _repository.GetExpiredThreadIdsAsync(DateTimeOffset.UtcNow.AddDays(-30));

        Assert.Equal(new long[] { 1 }, expired);
    }

    private async Task BackdateTrashedAtAsync(long threadId, DateTimeOffset trashedAtUtc)
    {
        var db = new SQLiteAsyncConnection(_dbPath);
        var row = await db.FindAsync<TrashedThread>(threadId);
        row.TrashedAtUtc = trashedAtUtc;
        await db.UpdateAsync(row);
        await db.CloseAsync();
    }
}
