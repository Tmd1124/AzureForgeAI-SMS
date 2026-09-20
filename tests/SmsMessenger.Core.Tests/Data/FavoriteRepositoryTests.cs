using SmsMessenger.Core.Data;

namespace SmsMessenger.Core.Tests.Data;

public class FavoriteRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FavoriteRepository _repository;

    public FavoriteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"favorite-test-{Guid.NewGuid()}.db3");
        _repository = new FavoriteRepository(_dbPath);
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
    public async Task GetFavoriteThreadIdsAsync_is_empty_initially()
    {
        var ids = await _repository.GetFavoriteThreadIdsAsync();

        Assert.Empty(ids);
    }

    [Fact]
    public async Task FavoriteThreadAsync_then_GetFavoriteThreadIdsAsync_returns_it()
    {
        await _repository.FavoriteThreadAsync(42);

        var ids = await _repository.GetFavoriteThreadIdsAsync();

        Assert.Equal(new long[] { 42 }, ids);
    }

    [Fact]
    public async Task IsFavoriteAsync_reflects_favorite_state()
    {
        Assert.False(await _repository.IsFavoriteAsync(7));

        await _repository.FavoriteThreadAsync(7);

        Assert.True(await _repository.IsFavoriteAsync(7));
    }

    [Fact]
    public async Task UnfavoriteThreadAsync_removes_it_from_the_favorite_list()
    {
        await _repository.FavoriteThreadAsync(42);

        await _repository.UnfavoriteThreadAsync(42);

        Assert.Empty(await _repository.GetFavoriteThreadIdsAsync());
    }
}
