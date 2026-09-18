using SmsMessenger.Core.Data;

namespace SmsMessenger.Core.Tests.Data;

public class BlockedNumberRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly BlockedNumberRepository _repository;

    public BlockedNumberRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"blocked-test-{Guid.NewGuid()}.db3");
        _repository = new BlockedNumberRepository(_dbPath);
        _repository.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task GetBlockedNumbersAsync_is_empty_initially()
    {
        var numbers = await _repository.GetBlockedNumbersAsync();

        Assert.Empty(numbers);
    }

    [Fact]
    public async Task BlockAsync_then_GetBlockedNumbersAsync_returns_it()
    {
        await _repository.BlockAsync("5550142231");

        var numbers = await _repository.GetBlockedNumbersAsync();

        Assert.Single(numbers);
        Assert.Equal("5550142231", numbers[0].PhoneNumber);
    }

    [Fact]
    public async Task IsBlockedAsync_reflects_blocked_state()
    {
        Assert.False(await _repository.IsBlockedAsync("5550142231"));

        await _repository.BlockAsync("5550142231");

        Assert.True(await _repository.IsBlockedAsync("5550142231"));
    }

    [Fact]
    public async Task UnblockAsync_removes_it_from_the_blocked_list()
    {
        await _repository.BlockAsync("5550142231");

        await _repository.UnblockAsync("5550142231");

        Assert.Empty(await _repository.GetBlockedNumbersAsync());
    }
}
