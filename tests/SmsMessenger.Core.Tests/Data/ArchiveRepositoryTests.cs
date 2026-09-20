using SmsMessenger.Core.Data;

namespace SmsMessenger.Core.Tests.Data;

public class ArchiveRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ArchiveRepository _repository;

    public ArchiveRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"archive-test-{Guid.NewGuid()}.db3");
        _repository = new ArchiveRepository(_dbPath);
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
    public async Task GetArchivedThreadIdsAsync_is_empty_initially()
    {
        var ids = await _repository.GetArchivedThreadIdsAsync();

        Assert.Empty(ids);
    }

    [Fact]
    public async Task ArchiveThreadAsync_then_GetArchivedThreadIdsAsync_returns_it()
    {
        await _repository.ArchiveThreadAsync(42);

        var ids = await _repository.GetArchivedThreadIdsAsync();

        Assert.Equal(new long[] { 42 }, ids);
    }

    [Fact]
    public async Task IsArchivedAsync_reflects_archived_state()
    {
        Assert.False(await _repository.IsArchivedAsync(7));

        await _repository.ArchiveThreadAsync(7);

        Assert.True(await _repository.IsArchivedAsync(7));
    }

    [Fact]
    public async Task UnarchiveThreadAsync_removes_it_from_the_archived_list()
    {
        await _repository.ArchiveThreadAsync(42);

        await _repository.UnarchiveThreadAsync(42);

        Assert.Empty(await _repository.GetArchivedThreadIdsAsync());
    }
}
