using ForgeLinkSms.Core.Data;

namespace ForgeLinkSms.Core.Tests.Data;

public class QuickReplyRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"quick-replies-test-{Guid.NewGuid()}.db3");
    private QuickReplyRepository _repository;

    public QuickReplyRepositoryTests()
    {
        _repository = new QuickReplyRepository(_dbPath);
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
    public async Task A_new_install_starts_with_the_starter_replies_in_order()
    {
        var replies = await _repository.GetAllAsync();

        Assert.Equal(QuickReplyRepository.StarterReplies, replies.Select(r => r.Text));
    }

    [Fact]
    public async Task AddAsync_appends_to_the_end()
    {
        await _repository.AddAsync("Call me when you can");

        Assert.Equal("Call me when you can", (await _repository.GetAllAsync())[^1].Text);
    }

    [Fact]
    public async Task UpdateAsync_changes_the_text_in_place()
    {
        var first = (await _repository.GetAllAsync())[0];

        await _repository.UpdateAsync(first.Id, "Running 5 minutes late");

        Assert.Equal("Running 5 minutes late", (await _repository.GetAllAsync())[0].Text);
    }

    [Fact]
    public async Task Deleted_starter_replies_do_not_come_back_on_the_next_launch()
    {
        foreach (var reply in await _repository.GetAllAsync())
        {
            await _repository.DeleteAsync(reply.Id);
        }
        _repository.Dispose();

        _repository = new QuickReplyRepository(_dbPath);
        await _repository.InitializeAsync();

        Assert.Empty(await _repository.GetAllAsync());
    }

    [Fact]
    public async Task Starter_replies_are_added_if_a_first_launch_was_interrupted_before_adding_them()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quick-replies-interrupted-{Guid.NewGuid()}.db3");
        var interrupted = new SQLite.SQLiteAsyncConnection(path);
        await interrupted.CreateTableAsync<ForgeLinkSms.Core.Models.QuickReply>();
        await interrupted.CloseAsync();

        using var repository = new QuickReplyRepository(path);
        await repository.InitializeAsync();

        Assert.Equal(QuickReplyRepository.StarterReplies, (await repository.GetAllAsync()).Select(r => r.Text));
    }

    // A SynchronizationContext whose queued continuations never run — exactly Android's main
    // thread while MauiProgram blocks it on .GetAwaiter().GetResult() during startup.
    private sealed class BlockedMainThreadContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }

    [Fact]
    public void InitializeAsync_finishes_when_startup_blocks_the_main_thread_on_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quick-replies-startup-{Guid.NewGuid()}.db3");
        var completed = false;
        var startup = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new BlockedMainThreadContext());
            var repository = new QuickReplyRepository(path);
            repository.InitializeAsync().GetAwaiter().GetResult();
            completed = true;
        })
        { IsBackground = true };

        startup.Start();

        Assert.True(startup.Join(TimeSpan.FromSeconds(10)) && completed, "InitializeAsync deadlocked, which hangs app startup (ANR)");
    }
}
