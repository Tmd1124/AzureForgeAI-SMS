using ForgeLinkSms.Core.Data;

namespace ForgeLinkSms.Core.Tests.Data;

public class DraftRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"drafts-test-{Guid.NewGuid()}.db3");
    private readonly DraftRepository _repository;

    public DraftRepositoryTests()
    {
        _repository = new DraftRepository(_dbPath);
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
    public async Task A_saved_draft_comes_back()
    {
        await _repository.SaveAsync(4, "See you at");

        Assert.Equal("See you at", await _repository.GetAsync(4));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Saving_blank_text_removes_the_draft(string text)
    {
        await _repository.SaveAsync(4, "See you at");

        await _repository.SaveAsync(4, text);

        Assert.Null(await _repository.GetAsync(4));
        Assert.Empty(await _repository.GetAllAsync());
    }

    [Fact]
    public async Task GetAllAsync_returns_every_conversation_with_a_draft()
    {
        await _repository.SaveAsync(4, "one");
        await _repository.SaveAsync(9, "two");

        var drafts = await _repository.GetAllAsync();

        Assert.Equal("one", drafts[4]);
        Assert.Equal("two", drafts[9]);
    }
}
