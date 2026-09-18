using SmsMessenger.Core.Data;

namespace SmsMessenger.Core.Tests.Data;

public class TrashRepositoryTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly TrashRepository _repository;

    public TrashRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trash-test-{Guid.NewGuid()}.db3");
        _repository = new TrashRepository(_dbPath);
    }

    public async Task InitializeAsync()
    {
        await _repository.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _repository.DisposeAsync();
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
}
