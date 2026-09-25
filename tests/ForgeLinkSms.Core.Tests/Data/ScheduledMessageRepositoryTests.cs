using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Data;

public class ScheduledMessageRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ScheduledMessageRepository _repository;

    public ScheduledMessageRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"scheduled-test-{Guid.NewGuid()}.db3");
        _repository = new ScheduledMessageRepository(_dbPath);
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
    public async Task GetAllAsync_is_empty_initially()
    {
        Assert.Empty(await _repository.GetAllAsync());
    }

    [Fact]
    public async Task AddAsync_assigns_an_id_and_the_message_is_then_retrievable()
    {
        var sendAt = DateTimeOffset.UtcNow.AddHours(1);

        var id = await _repository.AddAsync(new ScheduledMessage { Address = "5551234567", Body = "hi", SendAtUtc = sendAt });

        var stored = await _repository.GetAsync(id);
        Assert.NotNull(stored);
        Assert.Equal("5551234567", stored!.Address);
        Assert.Equal("hi", stored.Body);
        Assert.Equal(sendAt, stored.SendAtUtc);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_id()
    {
        Assert.Null(await _repository.GetAsync(999));
    }

    [Fact]
    public async Task RemoveAsync_deletes_the_message()
    {
        var id = await _repository.AddAsync(new ScheduledMessage { Address = "555", Body = "hi", SendAtUtc = DateTimeOffset.UtcNow });

        await _repository.RemoveAsync(id);

        Assert.Null(await _repository.GetAsync(id));
    }

    [Fact]
    public async Task GetAllAsync_returns_messages_ordered_by_send_time()
    {
        var now = DateTimeOffset.UtcNow;
        await _repository.AddAsync(new ScheduledMessage { Address = "b", Body = "later", SendAtUtc = now.AddHours(2) });
        await _repository.AddAsync(new ScheduledMessage { Address = "a", Body = "sooner", SendAtUtc = now.AddHours(1) });

        var all = await _repository.GetAllAsync();

        Assert.Equal(["sooner", "later"], all.Select(m => m.Body));
    }

    [Fact]
    public async Task A_group_message_keeps_its_conversation_and_recipients()
    {
        var id = await _repository.AddAsync(new ScheduledMessage
        {
            Address = "5551234567",
            Body = "hi all",
            SendAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            ThreadId = 7,
            GroupAddresses = "5551234567,5559876543"
        });

        var stored = await _repository.GetAsync(id);

        Assert.Equal(7, stored!.ThreadId);
        Assert.Equal(new[] { "5551234567", "5559876543" }, stored.Recipients);
        Assert.True(stored.IsGroup);
    }
}
